using Microsoft.EntityFrameworkCore;
using RinhaDeBackEnd.Domain.Entities;

namespace RinhaDeBackEnd.Infra.Contexts
{
    public class AppDbContext : DbContext
    {
        public DbSet<Customer> Customers { get; set; }
        //public DbSet<Transaction> Transactions { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>()
                .Property<uint>("xmin")
                .IsRowVersion();
        }
    }
}
