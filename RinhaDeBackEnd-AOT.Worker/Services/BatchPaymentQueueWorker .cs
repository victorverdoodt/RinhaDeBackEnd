using Dapper;
using Npgsql;
using RinhaDeBackEnd_AOT.Domain.Interfaces;
using RinhaDeBackEnd_AOT.Domain.Models;
using RinhaDeBackEnd_AOT.Infrastructure.Interfaces;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Data;
using System.Text.Json;
using System.Threading.Channels;

namespace RinhaDeBackEnd_AOT.Worker.Services
{
    /// <summary>
    /// Um worker de alta performance que processa uma fila de pagamentos do Redis.
    /// Utiliza um padrão Produtor-Consumidor com Channels para processar chamadas de gateway
    /// em paralelo com a escrita em lote no banco de dados, garantindo latência mínima.
    /// </summary>
    public class BatchPaymentQueueWorker : BackgroundService
    {
        #region Membros e Construtor

        private readonly ILogger<BatchPaymentQueueWorker> _logger;
        private readonly IDbConnectionFactory _dbConnectionFactory;
        private readonly IPaymentGatewayService _gatewayService;
        private readonly IDatabase _redis;

        // Configurações de performance
        private readonly int _maxConcurrencyLevel;
        private readonly int _smartBatchSize;
        private readonly TimeSpan _flushTimeout;
        private const string QueueName = "payments:queue";

        public BatchPaymentQueueWorker(
            IDbConnectionFactory dbConnectionFactory,
            IPaymentGatewayService gatewayService,
            IConnectionMultiplexer redis,
            IConfiguration configuration,
            ILogger<BatchPaymentQueueWorker> logger)
        {
            _dbConnectionFactory = dbConnectionFactory;
            _gatewayService = gatewayService;
            _redis = redis.GetDatabase();
            _logger = logger;

            // Carrega configurações com valores padrão robustos
            _maxConcurrencyLevel = configuration.GetValue("Workers:ConcurrencyLevel", 4);
            _smartBatchSize = configuration.GetValue("Workers:BatchSize", 1000);
            _flushTimeout = TimeSpan.FromMilliseconds(configuration.GetValue("Workers:FlushTimeoutMs", 50));
        }

        #endregion

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker (Parallel Batching) iniciado. Concorrência={Concurrency}, BatchSize={BatchSize}, FlushTimeout={FlushTimeout}ms",
                                   _maxConcurrencyLevel, _smartBatchSize, _flushTimeout.TotalMilliseconds);

            using var semaphore = new SemaphoreSlim(_maxConcurrencyLevel);
            while (!stoppingToken.IsCancellationRequested)
            {
                await semaphore.WaitAsync(stoppingToken);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var batchPayloads = await _redis.ListLeftPopAsync(QueueName, _smartBatchSize);

                        if (batchPayloads != null && batchPayloads.Length > 0)
                        {
                            var validPayloads = Array.FindAll(batchPayloads, p => !p.IsNullOrEmpty);
                            if (validPayloads.Length > 0)
                            {
                                await ProcessBatchAsync(validPayloads, stoppingToken);
                            }
                        }
                        else
                        {
                            // Pausa para evitar polling agressivo no Redis quando a fila está vazia
                            await Task.Delay(50, stoppingToken);
                        }
                    }
                    catch (OperationCanceledException) { } // Esperado durante o shutdown
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erro inesperado no loop principal do worker.");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, stoppingToken);
            }
            _logger.LogInformation("Worker finalizando...");
        }

        /// <summary>
        /// Orquestra o processamento de um lote de mensagens do Redis, usando um pipeline
        /// produtor-consumidor para máxima performance e latência mínima.
        /// </summary>
        private async Task ProcessBatchAsync(RedisValue[] batchPayloads, CancellationToken stoppingToken)
        {
            var requestsWithPayloads = new List<(RedisValue OriginalPayload, QueuedPaymentRequest Request)>();
            #region Desserialização de Payloads
            foreach (var payload in batchPayloads)
            {
                try
                {
                    var request = JsonSerializer.Deserialize<QueuedPaymentRequest>(payload!);
                    if (request != null) { requestsWithPayloads.Add((payload, request)); }
                }
                catch (JsonException jex)
                {
                    _logger.LogError(jex, "Falha ao desserializar payload, descartando: {Payload}", (string)payload);
                }
            }
            #endregion

            if (requestsWithPayloads.Count == 0) return;

            // 1. Cria um canal para comunicar os resultados bem-sucedidos e uma fila segura para falhas.
            var successChannel = Channel.CreateUnbounded<Transaction>();
            var payloadsToRequeue = new ConcurrentQueue<RedisValue>();

            // 2. Inicia a tarefa consumidora (DB Writer) que roda de forma independente.
            var dbWriterTask = Task.Run(() => RunDbWriter(successChannel.Reader, stoppingToken), stoppingToken);

            // 3. Inicia todas as tarefas produtoras (chamadas de gateway) em paralelo.
            var gatewayTasks = requestsWithPayloads.Select(async item =>
            {
                var (request, gatewayResult, success, originalPayload) = await ProcessGatewayCallAsync(_gatewayService, item.Request, item.OriginalPayload, stoppingToken);
                if (success && gatewayResult.HasValue)
                {
                    await successChannel.Writer.WriteAsync(new Transaction
                    {
                        Id = request.CorrelationId,
                        Amount = request.Amount,
                        requestedAt = request.RequestedAt,
                        Status = (short)TransactionStatus.Completed,
                        Gateway = (short)gatewayResult.Value
                    }, stoppingToken);
                }
                else
                {
                    payloadsToRequeue.Enqueue(originalPayload);
                }
            }).ToList();

            // 4. Espera que todas as chamadas de gateway terminem.
            // O DB Writer já está rodando e salvando dados em paralelo.
            await Task.WhenAll(gatewayTasks);

            // 5. Sinaliza ao DB Writer que não haverá mais itens (fecha o canal para escrita).
            successChannel.Writer.Complete();

            // 6. Espera que o DB Writer termine de salvar o último lote residual.
            await dbWriterTask;

            // 7. Lida com os itens que falharam.
            if (!payloadsToRequeue.IsEmpty)
            {
                await RequeueBatchAsync(payloadsToRequeue.ToArray());
                _logger.LogInformation("{RequeuedCount} itens reenfileirados.", payloadsToRequeue.Count);
            }
        }

        /// <summary>
        /// O coração da latência baixa. Roda em sua própria tarefa, acordando periodicamente
        /// para salvar o que estiver no canal, garantindo o SLA de flush.
        /// </summary>
        private async Task RunDbWriter(ChannelReader<Transaction> reader, CancellationToken token)
        {
            using var writerConnection = await _dbConnectionFactory.CreateConnectionAsync(token);
            var batchToInsert = new List<Transaction>(_smartBatchSize);

            // Loop principal que continua enquanto o canal não for marcado como concluído.
            while (await reader.WaitToReadAsync(token))
            {
                while (batchToInsert.Count < _smartBatchSize && reader.TryRead(out var transaction))
                {
                    batchToInsert.Add(transaction);
                }

                if (batchToInsert.Count >= _smartBatchSize)
                {
                    await FlushCompletedTransactionsAsync(writerConnection, batchToInsert, token);
                    batchToInsert.Clear();
                    continue;
                }

                if (batchToInsert.Any())
                {
                    try
                    {
                        await Task.Delay(_flushTimeout, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    while (batchToInsert.Count < _smartBatchSize && reader.TryRead(out var transaction))
                    {
                        batchToInsert.Add(transaction);
                    }

                    await FlushCompletedTransactionsAsync(writerConnection, batchToInsert, token);
                    batchToInsert.Clear();
                }
            }

            if (batchToInsert.Any())
            {
                await FlushCompletedTransactionsAsync(writerConnection, batchToInsert, token);
            }
        }

        /// <summary>
        /// Salva um lote de transações concluídas no banco de dados.
        /// </summary>
        private async Task FlushCompletedTransactionsAsync(IDbConnection connection, List<Transaction> transactions, CancellationToken stoppingToken)
        {
            if (!transactions.Any()) return;
            try
            {
                await InsertTransactionsWithCopyAsync((NpgsqlConnection)connection, transactions, stoppingToken);
            }
            catch (Exception dbEx)
            {
                _logger.LogCritical(dbEx, "FALHA CRÍTICA DE INSERÇÃO: {Count} transações processadas pelo gateway podem ter sido perdidas.", transactions.Count);
            }
        }

        #region Métodos Auxiliares

        /// <summary>
        /// Insere transações em lote usando o comando COPY do PostgreSQL, a forma mais eficiente.
        /// </summary>
        private async Task InsertTransactionsWithCopyAsync(NpgsqlConnection connection, List<Transaction> transactions, CancellationToken stoppingToken)
        {
            const string copyCommand = @"COPY ""Transactions"" (""Id"", ""Amount"", ""requestedAt"", ""Status"", ""Gateway"") FROM STDIN (FORMAT BINARY)";
            await using var writer = await connection.BeginBinaryImportAsync(copyCommand, stoppingToken);
            foreach (var t in transactions)
            {
                await writer.StartRowAsync(stoppingToken);
                await writer.WriteAsync(t.Id, NpgsqlTypes.NpgsqlDbType.Uuid, stoppingToken);
                await writer.WriteAsync((decimal)t.Amount, NpgsqlTypes.NpgsqlDbType.Numeric, stoppingToken);
                await writer.WriteAsync(t.requestedAt, NpgsqlTypes.NpgsqlDbType.TimestampTz, stoppingToken);
                await writer.WriteAsync((short)t.Status, NpgsqlTypes.NpgsqlDbType.Smallint, stoppingToken);
                await writer.WriteAsync((short)t.Gateway, NpgsqlTypes.NpgsqlDbType.Smallint, stoppingToken);
            }
            await writer.CompleteAsync(stoppingToken);
            _logger.LogDebug("{Count} transações inseridas via COPY.", transactions.Count);
        }

        /// <summary>
        /// Envolve a chamada ao serviço de gateway para capturar resultados e exceções de forma segura.
        /// </summary>
        private static async Task<(QueuedPaymentRequest Request, int? GatewayResult, bool Success, RedisValue OriginalPayload)> ProcessGatewayCallAsync(IPaymentGatewayService gatewayService, QueuedPaymentRequest request, RedisValue originalPayload, CancellationToken stoppingToken)
        {
            try
            {
                var gatewayResult = await gatewayService.ProcessAsync(new ExternalGatewayRequest(request.CorrelationId, request.Amount, request.RequestedAt), stoppingToken);
                return (request, (int)gatewayResult, true, originalPayload);
            }
            catch (Exception)
            {
                return (request, null, false, originalPayload);
            }
        }

        /// <summary>
        /// Devolve um lote de payloads para o final da fila no Redis.
        /// </summary>
        private async Task RequeueBatchAsync(RedisValue[] payloads)
        {
            if (payloads.Length > 0)
            {
                await _redis.ListRightPushAsync(QueueName, payloads);
            }
        }

        #endregion
    }
}