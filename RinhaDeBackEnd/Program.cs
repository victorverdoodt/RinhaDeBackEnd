using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.EntityFrameworkCore;
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
            builder.Services.AddRequestTimeouts(options => options.DefaultPolicy = new RequestTimeoutPolicy { Timeout = TimeSpan.FromSeconds(60) });
            var app = builder.Build();
            app.UseMiddleware<JsonExceptionMiddleware>();
            app.MapClientEndpoint();

            app.Run();
        }
    }
}
