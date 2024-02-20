using Microsoft.EntityFrameworkCore;
using RinhaDeBackEnd_AOT.Infra.Entities;

namespace RinhaDeBackEnd_AOT.Infra.Contexts
{
    public class AppDbContext : DbContext
    {
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Transaction> Transactions => Set<Transaction>();

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }


        public static readonly Func<AppDbContext, int, IAsyncEnumerable<dynamic>> GetExtrato
            = EF.CompileAsyncQuery(
                (AppDbContext context, int id) => context.Customers
                    .AsNoTracking()
                    .Where(c => c.Id == id)
                    .Include(x => x.LastTransactions)
                    .Select(c => new
                    {
                        Saldo = new
                        {
                            Total = c.Balance,
                            Data_extrato = DateTime.UtcNow,
                            Limite = c.Limit
                        },
                        UltimasTransacoes = c.LastTransactions.OrderByDescending(x => x.TransactionDate).Take(10).Select(t => new
                        {
                            Valor = t.Value,
                            Tipo = t.Type,
                            Descricao = t.Description,
                            Realizada_em = t.TransactionDate
                        })
                    }));

        public static readonly Func<AppDbContext, int, Task<Cliente?>> GetCliente
            = EF.CompileAsyncQuery(
                (AppDbContext context, int id) => context.Clientes
                    .AsNoTracking()
                    .SingleOrDefault(x => x.Id == id));
    }
}
