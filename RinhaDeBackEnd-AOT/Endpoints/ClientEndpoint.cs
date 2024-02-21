using Microsoft.EntityFrameworkCore;
using RinhaDeBackEnd_AOT.Dto;
using RinhaDeBackEnd_AOT.Infra.Contexts;
using RinhaDeBackEnd_AOT.Infra.Entities;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

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
                    var customer = await context.Customers
                      .FromSqlInterpolated($"SELECT * FROM public.\"Customers\" WHERE \"Id\" = {id} FOR UPDATE")
                      .Select(x => new CustomerInfoDto { Id = x.Id, Balance = x.Balance, Limit = x.Limit, LastStatement = x.LastStatement })
                      .SingleOrDefaultAsync();

                    if (customer == null) return Results.NotFound();

                    if (dto.Tipo == 'd' && (customer.Balance - (int)dto.Valor < -customer.Limit))
                    {
                        return Results.UnprocessableEntity();
                    }

                    var balance = customer.Balance;
                    var limit = customer.Limit;
                    var value = dto.Tipo == 'c' ? dto.Valor : dto.Valor * -1;


                    var statement = customer.LastStatement == null ? new StatementDto() : JsonSerializer.Deserialize<StatementDto>(customer.LastStatement, AppJsonSerializerContext.Default.StatementDto) ?? new StatementDto();
                    
                    var newTransaction = new TransactionDto { 
                        Descricao = dto.Descricao, 
                        Tipo = dto.Tipo, 
                        Valor = value, 
                        Realizada_em = DateTime.Now  
                    };

                    statement.UltimasTransacoes.Add(newTransaction);

                    var orderedTransactions = statement.UltimasTransacoes.OrderByDescending(x => x.Realizada_em).Take(10).ToList();

                    var newStatemnt = new StatementDto
                    {
                        Saldo = new BalanceDto
                        {
                            Data_extrato = DateTime.Now,
                            Limite = limit,
                            Total = balance + value
                        },
                        UltimasTransacoes = orderedTransactions
                    };

                    var updatedTransactionsJson = JsonSerializer.Serialize(newStatemnt, AppJsonSerializerContext.Default.StatementDto);

                    var updated = await AppDbContext.TryUpdateBalance(context, id, value, updatedTransactionsJson);

                    if (!updated)
                        return Results.UnprocessableEntity();

                    await transaction.CommitAsync();

                    return Results.Ok(new RespondeDto { Limite = limit, Saldo = balance });
                }
                catch (DbUpdateConcurrencyException)
                {
                    Console.WriteLine("Rollback");
                    await transaction.RollbackAsync();
                    return Results.UnprocessableEntity();
                }
            }).DisableRequestTimeout();

            endpoints.MapGet("clientes/{id}/extrato", async (int id, AppDbContext context) =>
            {
                if (id < 1 || id > 5)
                    return Results.NotFound();

                var customer = await AppDbContext.GetCustomer(context, id);

                if (customer == null) return Results.NotFound();

                if (customer.LastStatement is null)
                {
                    return Results.Ok(new StatementDto { 
                        Saldo = new BalanceDto { 
                            Data_extrato = DateTime.Now, 
                            Limite = customer.Limit, 
                            Total = customer.Balance}, 
                        UltimasTransacoes = new List<TransactionDto>() 
                    });
                }

                return Results.Content(customer.LastStatement.ToLower(), "application/json");
            }).DisableRequestTimeout();

            return group;
        }
    }
}
