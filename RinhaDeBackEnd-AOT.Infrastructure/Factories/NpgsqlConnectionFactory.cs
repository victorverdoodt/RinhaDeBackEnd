using Microsoft.Extensions.Configuration;
using Npgsql;
using RinhaDeBackEnd_AOT.Infrastructure.Interfaces;
using System.Data;

namespace RinhaDeBackEnd_AOT.Infrastructure.Factories
{
    public class NpgsqlConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;

        public NpgsqlConnectionFactory(IConfiguration configuration)
        {
            _connectionString = Environment.GetEnvironmentVariable("DB_HOSTNAME") ?? configuration.GetConnectionString("DefaultConnection")!;

        }

        public async Task<IDbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }

}
