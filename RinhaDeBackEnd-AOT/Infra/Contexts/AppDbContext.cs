using Microsoft.EntityFrameworkCore;
using RinhaDeBackEnd_AOT.Infra.Entities;

namespace RinhaDeBackEnd_AOT.Infra.Contexts
{
    public class AppDbContext : DbContext
    {
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Transaction> Transactions { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    }
}
