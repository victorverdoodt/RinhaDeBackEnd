using Microsoft.EntityFrameworkCore;
using RinhaDeBackEnd_AOT.Dto;
using RinhaDeBackEnd_AOT.Infra.Entities;

namespace RinhaDeBackEnd_AOT.Infra.Contexts
{
    public class AppDbContext : DbContext
    {
        public DbSet<Customer> Customers => Set<Customer>();

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }


        public static readonly Func<AppDbContext, int, Task<CustomerInfoDto?>> GetCustomer
          = EF.CompileAsyncQuery(
              (AppDbContext context, int id) => context.Customers
                  .AsNoTracking()
                  .Select(x=> new CustomerInfoDto { Id = x.Id, Balance = x.Balance, Limit = x.Limit, LastStatement = x.LastStatement })
                  .SingleOrDefault(x => x.Id == id));


        public static async Task<bool> TryUpdateBalance(AppDbContext context, int id, int value, string statement)
        {
            var result = await context.Customers
                .Where(x => x.Id == id && (x.Balance + value >= (x.Limit * -1) || value > 0))
                .ExecuteUpdateAsync(x =>x
                    .SetProperty(e => e.Balance, e => e.Balance + value)
                    .SetProperty(e=> e.LastStatement, e => statement));
            return result > 0;
        }
    }
}
