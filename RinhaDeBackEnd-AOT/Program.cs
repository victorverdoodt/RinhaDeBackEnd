using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RinhaDeBackEnd_AOT.Dto;
using RinhaDeBackEnd_AOT.Endpoints;
using RinhaDeBackEnd_AOT.Infra.Contexts;
using RinhaDeBackEnd_AOT.Infra.Entities;
using RinhaDeBackEnd_AOT.Infra.Interfaces;
using RinhaDeBackEnd_AOT.Middlewares;
using RinhaDeBackEnd_AOT.Services;
using RinhaDeBackEndAOT.Models;
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

            ServicePointManager.Expect100Continue = false; // less headers
            ServicePointManager.UseNagleAlgorithm = false; // less latency

            builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
               ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

            builder.Logging.SetMinimumLevel(LogLevel.Error);

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
            });

            builder.Services.Configure<RouteHandlerOptions>(o => { o.ThrowOnBadRequest = true; });

            builder.Services.AddHttpClient<IPaymentGatewayService, PaymentGatewayService>();

            var ConnectionString = Environment.GetEnvironmentVariable("DB_HOSTNAME") ?? builder.Configuration.GetConnectionString("DefaultConnection");

            var configuration = builder.Configuration;
            builder.Services.AddDbContextPool<AppDbContext>(options =>
                options.UseNpgsql(ConnectionString)
                .EnableThreadSafetyChecks(false)
                .UseModel(AppDbContextModel.Instance)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            );

            builder.Services.AddHostedService<WarmUpHostedService>();


            builder.Services.AddHostedService<BatchPaymentQueueWorker>();
            

            var app = builder.Build();
            app.UseMiddleware<BadHttpRequestExceptionMiddleware>();
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
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
