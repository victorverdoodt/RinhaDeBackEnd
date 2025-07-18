using RinhaDeBackEnd_AOT.Domain.Interfaces;
using RinhaDeBackEnd_AOT.Domain.Models;
using RinhaDeBackEnd_AOT.Infrastructure.Interfaces;
using RinhaDeBackEnd_AOT.Worker.Models;
using StackExchange.Redis;
using System.Data;
using System.Text.Json;
using System.Threading.Channels;
using Npgsql;
using System.Collections.Concurrent;

namespace RinhaDeBackEnd_AOT.Worker.Services
{
    public class BatchPaymentQueueWorker : BackgroundService
    {
        #region Membros e Construtor

        private readonly ILogger<BatchPaymentQueueWorker> _logger;
        private readonly IDbConnectionFactory _dbConnectionFactory;
        private readonly IPaymentGatewayService _gatewayService;
        private readonly IDatabase _redis;

        // Configurações de performance estáticas
        private readonly int _maxConcurrencyLevel;
        private readonly int _initialBatchSize;
        private readonly TimeSpan _initialFlushTimeout;
        private const string QueueName = "payments:queue";

        // Parâmetros para o Ajuste Adaptativo
        private const double TargetLatencyMs = 50.0;  // SLA: latência média alvo
        private const int MinBatchSize = 50;          // Lote mínimo
        private const int MaxBatchSize = 2000;        // Lote máximo
        private const double MinFlushTimeoutMs = 10.0; // Timeout mínimo
        private const double MaxFlushTimeoutMs = 75.0;// Timeout máximo
        private const double SmoothingFactor = 0.25;  // Fator de suavização para a média móvel (EMA)
        private double _smoothedLatencyMs = TargetLatencyMs; // Estado da latência suavizada

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

            _maxConcurrencyLevel = configuration.GetValue("Workers:ConcurrencyLevel", 4);
            _initialBatchSize = configuration.GetValue("Workers:BatchSize", 1000);
            _initialFlushTimeout = TimeSpan.FromMilliseconds(configuration.GetValue("Workers:FlushTimeoutMs", 50));
        }

        #endregion

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "Worker (Adaptive Batching) iniciado. Concorrência={Concurrency}, InitialBatchSize={BatchSize}, InitialFlushTimeout={FlushTimeout}ms",
                _maxConcurrencyLevel, _initialBatchSize, _initialFlushTimeout.TotalMilliseconds);

            using var semaphore = new SemaphoreSlim(_maxConcurrencyLevel);
            while (!stoppingToken.IsCancellationRequested)
            {
                await semaphore.WaitAsync(stoppingToken);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var batchPayloads = await _redis.ListLeftPopAsync(QueueName, _initialBatchSize);

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
                            await Task.Delay(100, stoppingToken);
                        }
                    }
                    catch (OperationCanceledException) { }
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

        private async Task ProcessBatchAsync(RedisValue[] batchPayloads, CancellationToken stoppingToken)
        {
            var requestsWithPayloads = new List<(RedisValue OriginalPayload, QueuedPaymentRequest Request)>(batchPayloads.Length);

            foreach (var payload in batchPayloads)
            {
                if (payload.IsNullOrEmpty) continue;
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

            if (requestsWithPayloads.Count == 0) return;

            var successChannel = Channel.CreateUnbounded<ProcessedItem>();
            var payloadsToRequeue = new ConcurrentQueue<RedisValue>();

            var dbWriterTask = Task.Run(() => RunDbWriter(successChannel.Reader, stoppingToken), stoppingToken);

            var gatewayTasks = requestsWithPayloads.Select(async item =>
            {
                var (request, gatewayResult, success, originalPayload) = await ProcessGatewayCallAsync(_gatewayService, item.Request, item.OriginalPayload, stoppingToken);
                if (success && gatewayResult.HasValue)
                {
                    var gatewayCompletionTime = DateTime.UtcNow;
                    await successChannel.Writer.WriteAsync(new ProcessedItem
                    {
                        Transaction = new Transaction
                        {
                            CorrelationId = request.CorrelationId,
                            Amount = request.Amount,
                            requestedAt = request.RequestedAt,
                            Status = (short)TransactionStatus.Completed,
                            Gateway = (short)gatewayResult.Value
                        },
                        GatewayCompletionTime = gatewayCompletionTime
                    }, stoppingToken);
                }
                else
                {
                    payloadsToRequeue.Enqueue(originalPayload);
                }
            }).ToList();

            await Task.WhenAll(gatewayTasks);
            successChannel.Writer.Complete();
            await dbWriterTask;

            if (!payloadsToRequeue.IsEmpty)
            {
                await RequeueBatchAsync(payloadsToRequeue.ToArray());
                _logger.LogInformation("{RequeuedCount} itens reenfileirados.", payloadsToRequeue.Count);
            }
        }

        #region DB Writer com Lógica Adaptativa

        private async Task RunDbWriter(ChannelReader<ProcessedItem> reader, CancellationToken token)
        {
            using var writerConnection = await _dbConnectionFactory.CreateConnectionAsync(token);

            var currentBatchSize = _initialBatchSize;
            var currentFlushTimeout = _initialFlushTimeout;
            var itemsForNextBatch = new List<ProcessedItem>(MaxBatchSize);

            _logger.LogInformation("DbWriter iniciado com TargetLatency={TargetLatency}ms", TargetLatencyMs);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    await AccumulateBatchAsync(reader, itemsForNextBatch, currentBatchSize, currentFlushTimeout, token);

                    if (itemsForNextBatch.Any())
                    {
                        var transactionsToInsert = itemsForNextBatch.Select(i => i.Transaction).ToList();
                        await FlushCompletedTransactionsAsync(writerConnection, transactionsToInsert, token);
                        UpdateAdaptiveParameters(itemsForNextBatch, ref currentBatchSize, ref currentFlushTimeout);
                        itemsForNextBatch.Clear();
                    }
                    else if (reader.Completion.IsCompleted)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DbWriter: Erro inesperado no loop.");
            }
            finally
            {
                if (itemsForNextBatch.Any())
                {
                    _logger.LogInformation("DbWriter: Fazendo flush final de {Count} itens.", itemsForNextBatch.Count);
                    var transactionsToInsert = itemsForNextBatch.Select(i => i.Transaction).ToList();
                    await FlushCompletedTransactionsAsync(writerConnection, transactionsToInsert, CancellationToken.None);
                }
                _logger.LogInformation("DbWriter finalizado.");
            }
        }

        private async Task AccumulateBatchAsync(ChannelReader<ProcessedItem> reader, List<ProcessedItem> batch, int targetBatchSize, TimeSpan timeout, CancellationToken token)
        {
            if (batch.Count >= targetBatchSize) return;

            try
            {
                using var timeoutCts = new CancellationTokenSource(timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

                if (!batch.Any())
                {
                    if (await reader.WaitToReadAsync(linkedCts.Token))
                    {
                        // Pega o primeiro item.
                        if (reader.TryRead(out var firstItem))
                        {
                            batch.Add(firstItem);
                        }
                    }
                }

                while (batch.Count < targetBatchSize && reader.TryRead(out var item))
                {
                    batch.Add(item);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
               
            }
        }

        private void UpdateAdaptiveParameters(List<ProcessedItem> processedBatch, ref int currentBatchSize, ref TimeSpan currentFlushTimeout)
        {
            var now = DateTime.UtcNow;
            var batchLatencySum = processedBatch.Sum(item => (now - item.GatewayCompletionTime).TotalMilliseconds);
            if (processedBatch.Count == 0) return;
            var averageBatchLatency = batchLatencySum / processedBatch.Count;

            _smoothedLatencyMs = (SmoothingFactor * averageBatchLatency) + ((1 - SmoothingFactor) * _smoothedLatencyMs);

            if (_smoothedLatencyMs > TargetLatencyMs)
            {
                var newBatchSize = (int)(currentBatchSize * 0.75);
                currentBatchSize = Math.Max(MinBatchSize, newBatchSize);
            }
            else
            {
                var newBatchSize = currentBatchSize + 20;
                currentBatchSize = Math.Min(MaxBatchSize, newBatchSize);
            }

            var latencyRatio = _smoothedLatencyMs / TargetLatencyMs;
            var newTimeoutMs = Math.Clamp(
                _initialFlushTimeout.TotalMilliseconds / latencyRatio,
                MinFlushTimeoutMs,
                MaxFlushTimeoutMs
            );
            currentFlushTimeout = TimeSpan.FromMilliseconds(newTimeoutMs);

            _logger.LogDebug(
                "Ajuste Adaptativo: Latência Suavizada={SmoothedLatency:F2}ms. Novo BatchSize={BatchSize}. Novo Timeout={Timeout:F2}ms.",
                _smoothedLatencyMs, currentBatchSize, currentFlushTimeout.TotalMilliseconds);
        }

        #endregion

        #region Métodos Auxiliares

        private async ValueTask FlushCompletedTransactionsAsync(IDbConnection connection, List<Transaction> transactions, CancellationToken stoppingToken)
        {
            if (!transactions.Any()) return;
            try
            {
                await InsertTransactionsWithCopyAsync((NpgsqlConnection)connection, transactions, stoppingToken);
            }
            catch (Exception dbEx)
            {
                _logger.LogCritical(dbEx, "FALHA CRÍTICA DE INSERÇÃO: {Count} transações podem ter sido perdidas. Implemente uma DLQ!", transactions.Count);
            }
        }

        private async ValueTask InsertTransactionsWithCopyAsync(NpgsqlConnection connection, List<Transaction> transactions, CancellationToken stoppingToken)
        {
            const string copyCommand = @"COPY ""Transactions"" (""CorrelationId"", ""Amount"", ""requestedAt"", ""Status"", ""Gateway"") FROM STDIN (FORMAT BINARY)";
            await using var writer = await connection.BeginBinaryImportAsync(copyCommand, stoppingToken);
            foreach (var t in transactions)
            {
                await writer.StartRowAsync(stoppingToken);
                await writer.WriteAsync(t.CorrelationId, NpgsqlTypes.NpgsqlDbType.Uuid, stoppingToken);
                await writer.WriteAsync((decimal)t.Amount, NpgsqlTypes.NpgsqlDbType.Numeric, stoppingToken);
                await writer.WriteAsync(t.requestedAt, NpgsqlTypes.NpgsqlDbType.TimestampTz, stoppingToken);
                await writer.WriteAsync((short)t.Status, NpgsqlTypes.NpgsqlDbType.Smallint, stoppingToken);
                await writer.WriteAsync((short)t.Gateway, NpgsqlTypes.NpgsqlDbType.Smallint, stoppingToken);
            }
            await writer.CompleteAsync(stoppingToken);
            _logger.LogDebug("{Count} transações inseridas via COPY.", transactions.Count);
        }

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