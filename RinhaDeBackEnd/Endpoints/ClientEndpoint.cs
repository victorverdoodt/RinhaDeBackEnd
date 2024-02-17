﻿using Microsoft.AspNetCore.Mvc;
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
                using (var transaction = await context.Database.BeginTransactionAsync())
                {
                    try
                    {
                        if (id < 1 || id > 5)
                            return Results.NotFound();

                        var validationResults = new List<ValidationResult>();
                        var validationContext = new ValidationContext(dto);

                        if (!Validator.TryValidateObject(dto, validationContext, validationResults, true))
                        {
                            return Results.UnprocessableEntity(validationResults.Select(vr => vr.ErrorMessage));
                        }

                        var customer = await context.Customers
                             .FromSqlInterpolated($"SELECT *, xmin FROM public.\"Customers\" WHERE \"Id\" = {id} FOR UPDATE LIMIT 1")
                             .SingleAsync();

                        if (customer == null) return Results.NotFound();

                        if (dto.Tipo == 'd' && (customer.Balance - dto.Valor < -customer.Limit))
                        {
                            return Results.UnprocessableEntity();
                        }

                        var balance = customer.Balance;
                        var limit = customer.Limit;

                        var newTransaction = new Transaction
                        {
                            Value = dto.Tipo == 'c' ? dto.Valor : -dto.Valor,
                            Type = dto.Tipo,
                            Description = dto.Descricao
                        };

                        var transactions = customer.LastTransactions == null ? new List<Transaction>() : JsonSerializer.Deserialize<List<Transaction>>(customer.LastTransactions) ?? new List<Transaction>();
                        transactions.Add(newTransaction);

                        var orderedTransactions = transactions.OrderByDescending(x => x.TransactionDate).Take(10).ToList();

                        var updatedTransactionsJson = JsonSerializer.Serialize(orderedTransactions);

                        customer.LastTransactions = updatedTransactionsJson;
                        customer.Balance += newTransaction.Value;
                        context.Customers.Update(customer);
                        await context.SaveChangesAsync();
                        await transaction.CommitAsync();

                        return Results.Ok(new { Limite = limit, Saldo = balance });
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        await transaction.RollbackAsync();
                        return Results.UnprocessableEntity();
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Console.WriteLine(ex.Message);
                        return Results.UnprocessableEntity(ex.Message);
                    }
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