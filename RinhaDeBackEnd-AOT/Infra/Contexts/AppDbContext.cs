using Microsoft.EntityFrameworkCore;
using RinhaDeBackEnd_AOT.Dto;
using RinhaDeBackEnd_AOT.Infra.Entities;

namespace RinhaDeBackEnd_AOT.Infra.Contexts
{
    public class AppDbContext : DbContext
    {
        public DbSet<Transaction> Transactions => Set<Transaction>();

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GatewayStatsResult>().HasNoKey().ToView(null);
        }
    }
}

