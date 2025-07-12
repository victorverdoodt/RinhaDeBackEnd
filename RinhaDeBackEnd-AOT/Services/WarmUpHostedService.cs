using Microsoft.EntityFrameworkCore;
using Npgsql;
using RinhaDeBackEnd_AOT.Dto;
using RinhaDeBackEnd_AOT.Infra.Contexts;
using System.Data;
using System.Diagnostics;

namespace RinhaDeBackEnd_AOT.Services
{
    public class WarmUpHostedService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<WarmUpHostedService> _logger;

        public WarmUpHostedService(IServiceProvider serviceProvider, ILogger<WarmUpHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var sw = Stopwatch.StartNew();

                // Forçar abertura de conexão (ajuda a reduzir TLS e pool warm-up)
                await context.Database.OpenConnectionAsync(cancellationToken);
                await context.Database.CloseConnectionAsync();

                // Forçar compilação da query
                var fromParam = new NpgsqlParameter("from", DBNull.Value) { DbType = DbType.DateTime };
                var toParam = new NpgsqlParameter("to", DBNull.Value) { DbType = DbType.DateTime };

                await context.Set<GatewayStatsResult>()
                    .FromSqlRaw(@"
                        SELECT
                            ""Gateway"",
                            COUNT(*) AS ""TotalRequests"",
                            SUM(""Amout"") AS ""TotalAmount""
                        FROM ""Transactions""
                        WHERE (@from IS NULL OR ""requestedAt"" >= @from)
                          AND (@to IS NULL OR ""requestedAt"" <= @to)
                        GROUP BY ""Gateway""
                    ", fromParam, toParam)
                        .AsNoTracking()
                        .ToListAsync(cancellationToken);

                await context.Transactions.AddAsync(
                    new Infra.Entities.Transaction { 
                        Amout = 20, Gateway = 1, 
                        Id = Guid.NewGuid(), 
                        requestedAt = DateTime.UtcNow }
                    );

                await context.SaveChangesAsync();

                await context.Database.ExecuteSqlRawAsync(@"TRUNCATE ""Transactions"" RESTART IDENTITY CASCADE;");

                sw.Stop();
                _logger.LogInformation("AppDbContext and query warmed up in {Time}ms", sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to warm up AppDbContext");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
