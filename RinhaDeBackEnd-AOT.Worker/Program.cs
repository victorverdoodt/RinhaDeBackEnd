using Polly;
using Polly.Fallback;
using RinhaDeBackEnd_AOT.Domain.Interfaces;
using RinhaDeBackEnd_AOT.Domain.Models;
using RinhaDeBackEnd_AOT.Infrastructure.Factories;
using RinhaDeBackEnd_AOT.Infrastructure.Interfaces;
using RinhaDeBackEnd_AOT.Worker.Services;
using StackExchange.Redis;
using System.Net.Http;
using System.Net.Http.Json;

namespace RinhaDeBackEnd_AOT.Worker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Logging.SetMinimumLevel(LogLevel.Error);
            builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
               ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

            builder.Services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();

            builder.Services.AddResiliencePipeline<string, int>("gateway-pipeline", (pipelineBuilder, context) =>
            {
                var config = context.ServiceProvider.GetRequiredService<IConfiguration>();
                var httpClientFactory = context.ServiceProvider.GetRequiredService<IHttpClientFactory>();

                var fallbackUrl = config["Gateways:Fallback"] + "/payments";
                var requestKey = new ResiliencePropertyKey<ExternalGatewayRequest>("gateway_request");

                pipelineBuilder.AddTimeout(TimeSpan.FromSeconds(3));
                pipelineBuilder.AddFallback(new FallbackStrategyOptions<int>
                {
                    ShouldHandle = new PredicateBuilder<int>().Handle<Exception>(),
                    FallbackAction = async args =>
                    {

                        try
                        {
                            if (!args.Context.Properties.TryGetValue(requestKey, out var request) || request is null)
                            {
                                throw new InvalidOperationException("Request object não encontrado no contexto da pipeline.");
                            }

                            var fallbackTimeout = TimeSpan.FromSeconds(2);
                            using var cts = new CancellationTokenSource(fallbackTimeout);

                            using var fallbackClient = httpClientFactory.CreateClient("GatewayClient");
                            using var jsonContent = JsonContent.Create(request);

                            var response = await fallbackClient.PostAsync(fallbackUrl, jsonContent, cts.Token);

                            response.EnsureSuccessStatusCode();
                            return Outcome.FromResult(1);
                        }
                        catch (Exception fallbackException)
                        {
                            throw new Exception("Todos os gateways falharam.", fallbackException);
                        }
                    }
                });
            });


            builder.Services.AddHttpClient<IPaymentGatewayService, PaymentGatewayService>("GatewayClient")
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    EnableMultipleHttp2Connections = true,
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
                });

           
            builder.Services.AddHostedService<BatchPaymentQueueWorker>();

            var host = builder.Build();
            host.Run();
        }
    }
}