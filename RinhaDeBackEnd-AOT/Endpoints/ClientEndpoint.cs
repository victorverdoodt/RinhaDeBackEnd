using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RinhaDeBackEnd_AOT.Dto;
using RinhaDeBackEnd_AOT.Extensions;
using RinhaDeBackEnd_AOT.Infra.Contexts;
using StackExchange.Redis;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace RinhaDeBackEnd_AOT.Endpoints
{
    public static class ClientEndpoint
    {
        public static RouteGroupBuilder MapClientEndpoint(this IEndpointRouteBuilder endpoints)
        {
            var jsonSerializerOptions = new JsonSerializerOptions
            {
                TypeInfoResolver = AppJsonSerializerContext.Default
            };
            var group = endpoints.MapGroup("");

            endpoints.MapPost("payments", async (
                [FromServices] IConnectionMultiplexer redis,
                [FromBody] ApiPaymentRequest dto) =>
            {
                if (!Validator.TryValidateObject(dto, new ValidationContext(dto), null, true))
                    return Results.UnprocessableEntity();

                var db = redis.GetDatabase();
                var item = new QueuedPaymentRequest(dto.CorrelationId, dto.Amount, DateTime.UtcNow);
                var payload = JsonSerializer.Serialize(item, jsonSerializerOptions);

                await db.ListRightPushAsync("payments:queue", payload, flags: CommandFlags.FireAndForget);

                return Results.Accepted();
            }).DisableRequestTimeout();

            endpoints.MapGet("payments-summary", async ([FromServices] AppDbContext context, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null) =>
            {
                var fromUtc = from?.ToUniversalTime();
                var toUtc = to?.ToUniversalTime();

                var fromParam = new Npgsql.NpgsqlParameter("from", (object?)fromUtc ?? DBNull.Value)
                {
                    DbType = System.Data.DbType.DateTime
                };

                var toParam = new Npgsql.NpgsqlParameter("to", (object?)toUtc ?? DBNull.Value)
                {
                    DbType = System.Data.DbType.DateTime
                };

                var stats = await context
                    .Set<GatewayStatsResult>()
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
                    .ToListAsync();

                var response = new MetricsResponse(
                    Default: stats.FirstOrDefault(x => x.Gateway == 0)?.ToRecord() ?? new GatewayStats(0, 0),
                    Fallback: stats.FirstOrDefault(x => x.Gateway == 1)?.ToRecord() ?? new GatewayStats(0, 0)
                );

                return Results.Ok(response);
            }).DisableRequestTimeout();

            endpoints.MapPost("/purge-payments", async ([FromServices] AppDbContext context) =>
            {
                await context.Database.ExecuteSqlRawAsync(@"TRUNCATE ""Transactions"" RESTART IDENTITY CASCADE;");
                return Results.Ok("Database purged.");
            });

            return group;
        }
    }
}
