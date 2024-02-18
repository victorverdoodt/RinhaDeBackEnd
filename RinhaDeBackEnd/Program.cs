using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
            var ConnectionString = Environment.GetEnvironmentVariable("DB_HOSTNAME") ?? builder.Configuration.GetConnectionString("DefaultConnection");

            var configuration = builder.Configuration;
            builder.Services.AddDbContextPool<AppDbContext>(options =>
                options.UseNpgsql(ConnectionString)
                .EnableThreadSafetyChecks(false)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            );

            builder.Services.AddHostedService<DataBaseInitializer>();
            builder.Services.AddRequestTimeouts(options => options.DefaultPolicy = new RequestTimeoutPolicy { Timeout = TimeSpan.FromSeconds(60) });
            var app = builder.Build();
            app.UseMiddleware<JsonExceptionMiddleware>();
            app.MapClientEndpoint();

            app.Run();
        }
    }
}
