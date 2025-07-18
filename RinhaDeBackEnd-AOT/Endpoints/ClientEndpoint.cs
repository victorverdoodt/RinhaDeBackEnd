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
                [FromServices] IConnectionMultiplexer redis,
                [FromQuery] DateTime? from = null,
                [FromQuery] DateTime? to = null) =>
            {
                var db = redis.GetDatabase();

                // 1. BUSCA TUDO PARA A MEMÓRIA
                var allPaymentHashes = await db.HashGetAllAsync("payments");

                if (allPaymentHashes.Length == 0)
                {
                    return Results.Ok(new MetricsResponse(new GatewayStats(0, 0.0m), new GatewayStats(0, 0.0m)));
                }

                var summary = new Dictionary<string, GatewayStats>
                {
                    ["default"] = new GatewayStats(0, 0.0m),
                    ["fallback"] = new GatewayStats(0, 0.0m),
                };

                // 2. FILTRA E AGREGA NA MEMÓRIA DA APLICAÇÃO
                foreach (var hashEntry in allPaymentHashes)
                {
                    try
                    {
                        var entry = JsonSerializer.Deserialize<RedisPaymentEntry>(hashEntry.Value!, AppJsonSerializerContext.Default.RedisPaymentEntry);
                        if (entry == null) continue;

                        // Filtro de data
                        if (from.HasValue && entry.RequestedAt < from.Value) continue;
                        if (to.HasValue && entry.RequestedAt > to.Value) continue;

                        // Agregação
                        var currentStats = summary[entry.Processor];
                        summary[entry.Processor] = currentStats with // Usa a sintaxe `with` para criar um novo record imutável
                        {
                            TotalRequests = currentStats.TotalRequests + 1,
                            TotalAmount = currentStats.TotalAmount + entry.Amount
                        };
                    }
                    catch (JsonException)
                    {
                        // Ignora entradas com JSON malformado
                        continue;
                    }
                }

                var response = new MetricsResponse(summary["default"], summary["fallback"]);
                return Results.Ok(response);
            }).DisableRequestTimeout();


            endpoints.MapPost("/purge-payments", async ([FromServices] IConnectionMultiplexer redis) =>
            {
                var db = redis.GetDatabase();

                // Deleta o Hash de pagamentos e a fila
                await db.KeyDeleteAsync(new RedisKey[] { "payments", "payments:queue" });

                return Results.Ok("Payments hash and queue purged.");
            });


            return group;
        }
    }
}
