using Microsoft.EntityFrameworkCore;
using RinhaDeBackEnd.Domain.Entities;
using RinhaDeBackEnd.Infra.Contexts;
using System.Threading;

namespace RinhaDeBackEnd.Infra.Seeder
{
    public class DataBaseInitializer : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        public DataBaseInitializer(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }
        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await InitializeDatabaseAsync(dbContext, cancellationToken);
        }

        private async Task InitializeDatabaseAsync(AppDbContext dbContext, CancellationToken cancellationToken)
        {
            try
            {
                var strategy = dbContext.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(() => dbContext.Database.MigrateAsync(cancellationToken));

                await SeedAsync(dbContext, cancellationToken);
            }
            catch
            {

            }
        }

        private async Task SeedAsync(AppDbContext dbContext, CancellationToken cancellationToken)
        {
            if (!await dbContext.Customers.AnyAsync())
            {
                await dbContext.Customers.AddRangeAsync(new List<Customer>() {
                    new() { Id = 1, Name = "o barato sai caro", Limit = 1000 * 100 },
                    new() { Id = 2, Name = "zan corp ltda", Limit = 800 * 100 },
                    new() { Id = 3, Name = "les cruders", Limit = 10000 * 100 },
                    new() { Id = 4, Name = "padaria joia de cocaia", Limit = 100000 * 100 },
                    new() { Id = 5, Name = "kid mais", Limit = 5000 * 100 }
                }, cancellationToken);
                await dbContext.SaveChangesAsync();
            }
        }
    }
}
