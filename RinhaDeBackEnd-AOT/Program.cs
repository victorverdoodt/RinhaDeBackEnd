using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RinhaDeBackEnd_AOT.Domain.Models;
using RinhaDeBackEnd_AOT.Dto;
using RinhaDeBackEnd_AOT.Endpoints;
using RinhaDeBackEnd_AOT.Infrastructure.Factories;
using RinhaDeBackEnd_AOT.Infrastructure.Interfaces;
using RinhaDeBackEnd_AOT.Services;
using StackExchange.Redis;
using System.Net;
using System.Text.Json.Serialization;

namespace RinhaDeBackEnd_AOT
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateSlimBuilder(args);

            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;

            builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
               ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

            builder.Services.AddSingleton<IngestionChannel>();

            // Registra nosso novo worker para rodar em segundo plano
            builder.Services.AddHostedService<RedisBatchPusherWorker>();

            builder.Logging.SetMinimumLevel(LogLevel.Error);

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
            });

            var app = builder.Build();
            app.MapClientEndpoint();

            app.Run();
        }
    }

    [JsonSerializable(typeof(Transaction))]
    [JsonSerializable(typeof(MetricsResponse))]
    [JsonSerializable(typeof(ApiPaymentRequest))]
    [JsonSerializable(typeof(ExternalGatewayRequest))]
    [JsonSerializable(typeof(ProblemDetails))]
    [JsonSerializable(typeof(String))]
    [JsonSerializable(typeof(HealthResponse))]
    [JsonSerializable(typeof(QueuedPaymentRequest))]
    [JsonSerializable(typeof(RedisPaymentEntry))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
