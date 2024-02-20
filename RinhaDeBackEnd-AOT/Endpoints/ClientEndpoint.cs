using Microsoft.EntityFrameworkCore;
using RinhaDeBackEnd_AOT.Dto;
using RinhaDeBackEnd_AOT.Infra.Contexts;
using RinhaDeBackEnd_AOT.Infra.Entities;
using System.ComponentModel.DataAnnotations;

namespace RinhaDeBackEnd_AOT.Endpoints
{
    public static class ClientEndpoint
    {
        public static RouteGroupBuilder MapClientEndpoint(this IEndpointRouteBuilder endpoints)
        {
            var group = endpoints.MapGroup("/clientes");

            endpoints.MapPost("clientes/{id}/transacoes", async (int id, AppDbContext context, TransactionDto dto) =>
            {
                if (id < 1 || id > 5)
                    return Results.NotFound();

                var validationContext = new ValidationContext(dto, null, null);

                if (!Validator.TryValidateObject(dto, validationContext, null, true))
                {
                    return Results.UnprocessableEntity();
                }

                using (var transaction = await context.Database.BeginTransactionAsync())
                {
                    try
                    {
                        var customer = await context.Customers
                             .FromSql($"SELECT * FROM public.\"Customers\" WHERE \"Id\" = {id} FOR UPDATE LIMIT 1")
                             .SingleAsync();

                        if (customer == null) return Results.NotFound();

                        if (dto.Tipo == 'd' && (customer.Balance - (int)dto.Valor < -customer.Limit))
                        {
                            return Results.UnprocessableEntity();
                        }

                        var balance = customer.Balance;
                        var limit = customer.Limit;
                        var value = dto.Tipo == 'c' ? dto.Valor : dto.Valor * -1;

                        var newTransaction = new Transaction
                        {
                            Value = value,
                            Type = dto.Tipo,
                            Description = dto.Descricao,
                            CustomerId = customer.Id,
                        };

                        customer.Balance += value;
                        context.Customers.Update(customer);
                        await context.Transactions.AddAsync(newTransaction);
                        await context.SaveChangesAsync();
                        await transaction.CommitAsync();

                        return Results.Ok(new { Limite = limit, Saldo = balance });
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        await transaction.RollbackAsync();
                        return Results.UnprocessableEntity();
                    }
                    catch (Exception)
                    {
                        await transaction.RollbackAsync();
                        return Results.UnprocessableEntity();
                    }
                }
            });

            endpoints.MapGet("clientes/{id}/extrato", async (int id, AppDbContext context) =>
            {
                if (id < 1 || id > 5)
                    return Results.NotFound();

                var customer = await context.Customers
                    .AsNoTracking()
                    .Where(c => c.Id == id)
                    .Include(x=> x.LastTransactions)
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
                    })
                    .SingleOrDefaultAsync();

                if (customer == null) return Results.NotFound();

                return Results.Ok(customer);
            });

            return group;
        }
    }
}
