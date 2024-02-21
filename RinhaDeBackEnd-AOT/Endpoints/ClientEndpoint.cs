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

                using var transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    var customer = await AppDbContext.GetCustomer(context, id);

                    if (customer == null) return Results.NotFound();

                    if (dto.Tipo == 'd' && (customer.Balance - (int)dto.Valor < -customer.Limit))
                    {
                        return Results.UnprocessableEntity();
                    }

                    var balance = customer.Balance;
                    var limit = customer.Limit;
                    var value = dto.Tipo == 'c' ? dto.Valor : dto.Valor * -1;

                    var updated = await AppDbContext.TryUpdateBalance(context, id, value);

                    if (!updated)
                        return Results.UnprocessableEntity();

                    var newTransaction = new Transaction
                    {
                        Value = value,
                        Type = dto.Tipo,
                        Description = dto.Descricao,
                        CustomerId = id,
                    };


                    await context.Transactions.AddAsync(newTransaction);
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Results.Ok(new RespondeDto { Limite = limit, Saldo = balance });
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
            }).DisableRequestTimeout();

            endpoints.MapGet("clientes/{id}/extrato", async (int id, AppDbContext context) =>
            {
                if (id < 1 || id > 5)
                    return Results.NotFound();

                var statement = await AppDbContext.GetStatement(context, id);

                if (statement == null) return Results.NotFound();

                return Results.Ok(statement);
            }).DisableRequestTimeout();

            return group;
        }
    }
}
