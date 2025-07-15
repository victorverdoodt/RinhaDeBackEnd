using RinhaDeBackEnd_AOT.Dto;
using RinhaDeBackEnd_AOT.Infra.Contexts;
using RinhaDeBackEnd_AOT.Infra.Entities;
using RinhaDeBackEnd_AOT.Infra.Interfaces;
using StackExchange.Redis;
using System.Text.Json;

namespace RinhaDeBackEnd_AOT.Services
{
    public class BatchPaymentQueueWorker : BackgroundService
    {
        private readonly ILogger<BatchPaymentQueueWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IDatabase _redis;
        private readonly JsonSerializerOptions _jsonSerializerOptions;
        private readonly int _maxConcurrencyLevel;
        private readonly int _batchSize;
        private const string QueueName = "payments:queue";

        public BatchPaymentQueueWorker(
            IServiceProvider serviceProvider,
            IConnectionMultiplexer redis,
            IConfiguration configuration,
            ILogger<BatchPaymentQueueWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _redis = redis.GetDatabase();
            _logger = logger;
            _maxConcurrencyLevel = configuration.GetValue<int>("Workers:ConcurrencyLevel", 5);
            _batchSize = configuration.GetValue<int>("Workers:BatchSize", 50);
            _jsonSerializerOptions = new JsonSerializerOptions
            {
                TypeInfoResolver = AppJsonSerializerContext.Default
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var semaphore = new SemaphoreSlim(_maxConcurrencyLevel);
            while (!stoppingToken.IsCancellationRequested)
            {
                await semaphore.WaitAsync(stoppingToken);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var batchPayloads = await _redis.ListLeftPopAsync(QueueName, _batchSize);

                        if (batchPayloads == null || batchPayloads.Length == 0)
                        {
                            await Task.Delay(50, stoppingToken);
                            return;
                        }

                        var validPayloads = Array.FindAll(batchPayloads, p => !p.IsNullOrEmpty);
                        if (validPayloads.Length > 0)
                        {
                            await ProcessBatchAsync(validPayloads, stoppingToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {

                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erro inesperado no loop de processamento do lote.");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, stoppingToken);
            }
        }

        private async Task ProcessBatchAsync(RedisValue[] batchPayloads, CancellationToken stoppingToken)
        {
            var requestsWithPayloads = new List<(RedisValue OriginalPayload, QueuedPaymentRequest Request)>(batchPayloads.Length);
            foreach (var payload in batchPayloads)
            {
                try
                {
                    var request = JsonSerializer.Deserialize<QueuedPaymentRequest>(payload!, _jsonSerializerOptions);
                    if (request != null)
                    {
                        requestsWithPayloads.Add((payload, request));
                    }
                }
                catch (JsonException jex)
                {
                    _logger.LogError(jex, "Falha ao desserializar payload: {Payload}", (string)payload);
                }
            }

            if (requestsWithPayloads.Count == 0) return;

            var successfulPayments = new List<(QueuedPaymentRequest Request, int GatewayResult)>();
            var failedPaymentsToRequeue = new List<RedisValue>();

            using var scope = _serviceProvider.CreateScope();
            var gatewayService = scope.ServiceProvider.GetRequiredService<IPaymentGatewayService>();

            var gatewayTasks = new List<Task<(QueuedPaymentRequest Request, int? GatewayResult, bool Success, RedisValue OriginalPayload)>>(requestsWithPayloads.Count);
            foreach (var item in requestsWithPayloads)
            {
                gatewayTasks.Add(ProcessGatewayCallAsync(gatewayService, item.Request, item.OriginalPayload, stoppingToken));
            }

            var allResults = await Task.WhenAll(gatewayTasks);

            foreach (var result in allResults)
            {
                if (result.Success && result.GatewayResult.HasValue)
                {
                    successfulPayments.Add((result.Request, result.GatewayResult.Value));
                }
                else
                {
                    failedPaymentsToRequeue.Add(result.OriginalPayload);
                }
            }

            if (failedPaymentsToRequeue.Count > 0)
            {
                await _redis.ListRightPushAsync(QueueName, failedPaymentsToRequeue.ToArray());
            }

            if (successfulPayments.Count > 0)
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.ChangeTracker.AutoDetectChangesEnabled = false;

                var transactionsToAdd = successfulPayments.Select(sp => new Transaction
                {
                    Id = sp.Request.CorrelationId,
                    Amout = sp.Request.Amount,
                    requestedAt = sp.Request.RequestedAt,
                    Gateway = sp.GatewayResult
                });

                await context.Transactions.AddRangeAsync(transactionsToAdd, stoppingToken);

                try
                {
                    await context.SaveChangesAsync(stoppingToken);
                }
                catch (Exception dbEx)
                {
                    _logger.LogCritical(dbEx, "FALHA CRÍTICA INESPERADA: O banco de dados falhou após o sucesso do gateway. {SuccessCount} pagamentos estão em estado inconsistente.", successfulPayments.Count);
                }
            }
        }

        private static async Task<(QueuedPaymentRequest Request, int? GatewayResult, bool Success, RedisValue OriginalPayload)> ProcessGatewayCallAsync(
            IPaymentGatewayService gatewayService,
            QueuedPaymentRequest request,
            RedisValue originalPayload,
            CancellationToken stoppingToken)
        {
            try
            {
                var gatewayResult = await gatewayService.ProcessAsync(
                    new ExternalGatewayRequest(request.CorrelationId, request.Amount, request.RequestedAt),
                    stoppingToken
                );
                return (request, (int)gatewayResult, true, originalPayload);
            }
            catch (Exception)
            {
                return (request, null, false, originalPayload);
            }
        }
    }
}