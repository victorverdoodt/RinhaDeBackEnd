using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using RinhaDeBackEnd.Domain.Dtos;
using RinhaDeBackEnd.Domain.Entities;
using RinhaDeBackEnd.Infra.Contexts;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace RinhaDeBackEnd.Endpoints
{
    public static class ClientEndpoint
    {
        public static RouteGroupBuilder MapClientEndpoint(this IEndpointRouteBuilder endpoints)
        {
            var group = endpoints.MapGroup("/clientes");

            endpoints.MapPost("clientes/{id}/transacoes", async ([FromRoute] int id, [FromServices] AppDbContext context, [FromBody] TransactionDto dto) =>
            {

                var validationResults = new List<ValidationResult>();
                var validationContext = new ValidationContext(dto);

                if (!Validator.TryValidateObject(dto, validationContext, validationResults, true))
                {
                    return Results.UnprocessableEntity(validationResults.Select(vr => vr.ErrorMessage));
                }

                var baseQuery = context.Customers.AsNoTracking().Where(x => x.Id == id);


                var customer = await baseQuery.Select(a => new { a.Id, a.Balance, a.Limit, a.LastTransactions }).FirstOrDefaultAsync();
                if (customer == null) return Results.NotFound();

                if (dto.Tipo == "d" && customer.Balance - dto.Valor < customer.Limit)
                {
                    return Results.UnprocessableEntity();
                }

                var balance = customer.Balance;
                var limit = customer.Limit;


                var newTransaction = new Transaction
                {
                    Value = dto.Tipo == "c" ? dto.Valor : -dto.Valor,
                    Type = dto.Tipo[0],
                    Description = dto.Descricao
                };

                var transactions = customer.LastTransactions == null ? new List<Transaction>() : JsonSerializer.Deserialize<List<Transaction>>(customer.LastTransactions) ?? new List<Transaction>();
                transactions.Add(newTransaction);
                if (transactions.Count > 10) transactions.RemoveAt(0);
                var updatedTransactionsJson = JsonSerializer.Serialize(transactions);

                try
                {
                    await baseQuery.ExecuteUpdateAsync(setters => setters
                        .SetProperty(b => b.Balance, b => b.Balance + newTransaction.Value)
                        .SetProperty(b => b.LastTransactions, b => updatedTransactionsJson)
                   );

                    return Results.Ok(new { Limite = limit, Saldo = balance });
                }
                catch (DbUpdateConcurrencyException)
                {

                    return Results.Conflict();
                }

            });

            endpoints.MapGet("clientes/{id}/extrato", async ([FromRoute] int id, [FromServices] AppDbContext context) =>
            {
                var customer = await context.Customers
                    .AsNoTracking()
                    .Where(c => c.Id == id)
                    .Select(c => new
                    {
                        c.Id,
                        c.Balance,
                        c.Limit,
                        c.LastTransactions
                    })
                    .FirstOrDefaultAsync();

                if (customer == null) return Results.NotFound();

                // Desserializa a coluna JSON para obter as últimas transações
                var ultimasTransacoes = customer.LastTransactions == null ? new List<Transaction>() : JsonSerializer.Deserialize<List<Transaction>>(customer.LastTransactions) ?? new List<Transaction>();

                var statement = new
                {
                    Saldo = new
                    {
                        Total = customer.Balance,
                        DataExtrato = DateTime.UtcNow,
                        Limite = customer.Limit
                    },
                    UltimasTransacoes = ultimasTransacoes.Select(t => new
                    {
                        Valor = t.Value,
                        Tipo = t.Type,
                        Descricao = t.Description,
                        RealizadaEm = t.TransactionDate
                    })
                };

                return Results.Ok(statement);
            });


            return group;
        }
    }
}