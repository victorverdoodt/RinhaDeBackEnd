using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RinhaDeBackEnd.Domain.Middleware;
using RinhaDeBackEnd.Endpoints;
using RinhaDeBackEnd.Infra.Contexts;
using RinhaDeBackEnd.Infra.Seeder;

namespace RinhaDeBackEnd
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var configuration = builder.Configuration;
            var ConnectionString = Environment.GetEnvironmentVariable("DB_HOSTNAME") ?? builder.Configuration.GetConnectionString("DefaultConnection");
            var RedisConnectionString = Environment.GetEnvironmentVariable("REDIS_HOSTNAME") ?? builder.Configuration.GetConnectionString("RedisConnection");
            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(ConnectionString));

            builder.Services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = RedisConnectionString;
                options.InstanceName = "SampleInstance_";
            });

            builder.Services.AddHostedService<DataBaseInitializer>();

            var app = builder.Build();
            //app.UseMiddleware<LimitRequestsMiddleware>();
            app.UseMiddleware<JsonExceptionMiddleware>();
            app.MapClientEndpoint();

            app.Run();
        }
    }
}
