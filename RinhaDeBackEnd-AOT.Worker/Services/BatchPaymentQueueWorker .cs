using RinhaDeBackEnd_AOT.Domain.Interfaces;
using RinhaDeBackEnd_AOT.Domain.Models;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RinhaDeBackEnd_AOT.Worker.Services
{
    public class BatchPaymentQueueWorker : BackgroundService
    {
        #region Membros e Construtor

        private readonly ILogger<BatchPaymentQueueWorker> _logger;
        private readonly IPaymentGatewayService _gatewayService;
        private readonly IDatabase _redis;

        // Configurações de performance
        private readonly int _maxConcurrencyLevel;
        private readonly int _smartBatchSize;

        // Chaves constantes do Redis
        private const string QueueName = "payments:queue";
        private const string PaymentsHashKey = "payments";

        public BatchPaymentQueueWorker(
            IPaymentGatewayService gatewayService,
            IConnectionMultiplexer redis,
            IConfiguration configuration,
            ILogger<BatchPaymentQueueWorker> logger)
        {
            _gatewayService = gatewayService;
            _redis = redis.GetDatabase();
            _logger = logger;

            _maxConcurrencyLevel = configuration.GetValue("Workers:ConcurrencyLevel", 4);
            _smartBatchSize = configuration.GetValue("Workers:BatchSize", 1000);
        }

        #endregion

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker (Go-Style Single Hash) iniciado. Concorrência={Concurrency}, BatchSize={BatchSize}",
                                   _maxConcurrencyLevel, _smartBatchSize);

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
                            await Task.Delay(50, stoppingToken);
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

            var gatewayTasks = requestsWithPayloads.Select(async item =>
            {
                var (request, gatewayResult, success, originalPayload) = await ProcessGatewayCallAsync(_gatewayService, item.Request, item.OriginalPayload, stoppingToken);

                if (success && gatewayResult.HasValue)
                {
                    try
                    {
                        var processorName = gatewayResult.Value == 0 ? "default" : "fallback";
                        var entry = new RedisPaymentEntry
                        {
                            Amount = request.Amount,
                            Processor = processorName,
                            RequestedAt = request.RequestedAt
                        };

                        var jsonPayload = JsonSerializer.Serialize(entry);

                        // Salva no Hash gigante: HSET payments {correlationId} {json}
                        await _redis.HashSetAsync(PaymentsHashKey, request.CorrelationId.ToString(), jsonPayload);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogCritical(ex, "FALHA CRÍTICA AO SALVAR NO REDIS: Transação {CorrelationId} pode ter sido perdida.", request.CorrelationId);
                    }
                }
                else
                {
                    await RequeueBatchAsync(new[] { originalPayload });
                }
            }).ToList();

            await Task.WhenAll(gatewayTasks);
        }

        #region Métodos Auxiliares

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