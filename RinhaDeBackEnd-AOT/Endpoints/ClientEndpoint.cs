using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RinhaDeBackEnd_AOT.Domain.Models;
using RinhaDeBackEnd_AOT.Dto;
using RinhaDeBackEnd_AOT.Extensions;
using RinhaDeBackEnd_AOT.Infrastructure.Interfaces;
using RinhaDeBackEnd_AOT.Services;
using StackExchange.Redis;
using System.Data;
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
                // Injeta nosso canal, não mais o IConnectionMultiplexer
                [FromServices] IngestionChannel channel,
                [FromBody] ApiPaymentRequest dto) =>
            {
                var item = new QueuedPaymentRequest(dto.CorrelationId, dto.Amount, DateTime.UtcNow);
                var payload = JsonSerializer.Serialize(item, AppJsonSerializerContext.Default.QueuedPaymentRequest);

                if (channel.Channel.Writer.TryWrite(payload))
                {
                    return Results.Accepted();
                }

                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            }).DisableRequestTimeout();

            endpoints.MapGet("payments-summary", async (
                [FromServices] IDbConnectionFactory dbFactory,
                [FromQuery] DateTime? from = null,
                [FromQuery] DateTime? to = null) =>
            {
                const string sql = @"
                    SELECT
                        ""Gateway"",
                        COUNT(*) AS ""TotalRequests"",
                        SUM(""Amount"") AS ""TotalAmount""
                    FROM ""Transactions""
                    WHERE ""Status"" = 1
                      AND (@from IS NULL OR ""requestedAt"" >= @from)
                      AND (@to IS NULL OR ""requestedAt"" <= @to)
                    GROUP BY ""Gateway""";

                var parameters = new DynamicParameters();

                parameters.Add("from", from, DbType.DateTime);
                parameters.Add("to", to, DbType.DateTime);

                using var connection = await dbFactory.CreateConnectionAsync();

                var stats = await connection.QueryAsync<GatewayStatsResult>(sql, parameters);

                var response = new MetricsResponse(
                    Default: stats.FirstOrDefault(x => x.Gateway == 0)?.ToRecord() ?? new GatewayStats(0, 0),
                    Fallback: stats.FirstOrDefault(x => x.Gateway == 1)?.ToRecord() ?? new GatewayStats(0, 0)
                );

                return Results.Ok(response);
            }).DisableRequestTimeout();


            endpoints.MapPost("/purge-payments", async ([FromServices] IDbConnectionFactory dbFactory) =>
            {
                const string sql = @"TRUNCATE ""Transactions"" RESTART IDENTITY CASCADE;";

                using var connection = await dbFactory.CreateConnectionAsync();

                await connection.ExecuteAsync(sql);

                return Results.Ok("Database purged.");
            });


            return group;
        }
    }
}
