using Microsoft.EntityFrameworkCore;
using RinhaDeBackEnd_AOT.Dto;
using RinhaDeBackEnd_AOT.Endpoints;
using RinhaDeBackEnd_AOT.Infra.Contexts;
using RinhaDeBackEnd_AOT.Infra.Entities;
using System.Text.Json.Serialization;

namespace RinhaDeBackEnd_AOT
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateSlimBuilder(args);

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
            });

            var ConnectionString = Environment.GetEnvironmentVariable("DB_HOSTNAME") ?? builder.Configuration.GetConnectionString("DefaultConnection");

            var configuration = builder.Configuration;
            builder.Services.AddDbContextPool<AppDbContext>(options =>
                options.UseNpgsql(ConnectionString)
                .EnableThreadSafetyChecks(false)
                .UseModel(AppDbContextModel.Instance)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            );

            var app = builder.Build();

            app.MapClientEndpoint();

            app.Run();
        }
    }

    [JsonSerializable(typeof(Customer[]))]
    [JsonSerializable(typeof(Transaction[]))]
    [JsonSerializable(typeof(TransactionDto))]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}