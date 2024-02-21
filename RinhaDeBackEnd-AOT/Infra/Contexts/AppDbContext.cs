using Microsoft.EntityFrameworkCore;
using RinhaDeBackEnd_AOT.Dto;
using RinhaDeBackEnd_AOT.Infra.Entities;

namespace RinhaDeBackEnd_AOT.Infra.Contexts
{
    public class AppDbContext : DbContext
    {
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Transaction> Transactions => Set<Transaction>();

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }


        /*public static readonly Func<AppDbContext, int, Task<StatementDto?>> GetStatement
            = EF.CompileAsyncQuery(
                (AppDbContext context, int id) => context.Customers
                    .AsNoTracking()
                    .Where(c => c.Id == id)
                    .Include(x => x.LastTransactions)
                    .Select(c => new StatementDto()
                    {
                        Saldo = new BalanceDto()
                        {
                            Total = c.Balance,
                            Data_extrato = DateTime.UtcNow,
                            Limite = c.Limit
                        },
                        UltimasTransacoes = c.LastTransactions.OrderByDescending(x => x.TransactionDate).Take(10).Select(t => new TransactionDto()
                        {
                            Valor = t.Value,
                            Tipo = t.Type,
                            Descricao = t.Description,
                            Realizada_em = t.TransactionDate
                        })
                    }).SingleOrDefault());*/


        public static readonly Func<AppDbContext, int, Task<CustomerInfoDto?>> GetCustomer
          = EF.CompileAsyncQuery(
              (AppDbContext context, int id) => context.Customers.FromSqlRaw("")
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
