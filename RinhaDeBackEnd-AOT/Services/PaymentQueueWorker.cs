using RinhaDeBackEnd_AOT.Dto;
using RinhaDeBackEnd_AOT.Infra.Contexts;
using RinhaDeBackEnd_AOT.Infra.Entities;
using RinhaDeBackEnd_AOT.Infra.Interfaces;
using StackExchange.Redis;
using System.Text.Json;

namespace RinhaDeBackEnd_AOT.Services
{
    public class PaymentQueueWorker : BackgroundService
    {
        private readonly IServiceProvider _provider;
        private readonly IDatabase _redis;
        private readonly JsonSerializerOptions _jsonSerializerOptions;

        public PaymentQueueWorker(IServiceProvider provider, IConnectionMultiplexer redis)
        {
            _provider = provider;
            _redis = redis.GetDatabase();

            _jsonSerializerOptions = new JsonSerializerOptions
            {
                TypeInfoResolver = AppJsonSerializerContext.Default
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var payload = await _redis.ListLeftPopAsync("payments:queue");
                if (payload.IsNullOrEmpty)
                {
                    await Task.Delay(10, stoppingToken);
                    continue;
                }

                try
                {
                    var request = JsonSerializer.Deserialize<QueuedPaymentRequest>(payload!, _jsonSerializerOptions);

                    using var scope = _provider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var gatewayService = scope.ServiceProvider.GetRequiredService<IPaymentGatewayService>();

                    var gateway = await gatewayService.ProcessAsync(new ExternalGatewayRequest(
                        request.CorrelationId,
                        request.Amount,
                        request.RequestedAt
                    ), stoppingToken);

                    await context.Transactions.AddAsync(new Transaction
                    {
                        Id = request.CorrelationId,
                        Amout = request.Amount,
                        requestedAt = request.RequestedAt,
                        Gateway = gateway
                    });

                    await context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    // log ou reenqueue, se necessário
                }
            }
        }
    }
}
