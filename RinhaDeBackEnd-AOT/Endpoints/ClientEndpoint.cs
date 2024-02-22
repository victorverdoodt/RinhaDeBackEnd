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
                if (id < 1 || id > 5 || !Validator.TryValidateObject(dto, new ValidationContext(dto), null, true))
                    return Results.UnprocessableEntity();

                using var transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    var customer = await context.Customers
                      .FromSqlInterpolated($"SELECT * FROM public.\"Customers\" WHERE \"Id\" = {id} FOR UPDATE")
                      .Select(x => new CustomerInfoDto { Id = x.Id, Balance = x.Balance, Limit = x.Limit, LastStatement = x.LastStatement })
                      .SingleOrDefaultAsync();

                    if (customer == null) return Results.NotFound();

                    if (dto.Tipo == 'd' && customer.Balance - (int)dto.Valor < -customer.Limit)
                        return Results.UnprocessableEntity();

                    var balance = customer.Balance;
                    var limit = customer.Limit;
                    var value = dto.Tipo == 'c' ? dto.Valor : dto.Valor * -1;

                    var statement = DeserializeStatement(customer.LastStatement);
                    statement.Saldo.Total = balance+value;
                    statement.Saldo.Limite = limit;
                    dto.Valor = value;
                    statement.Ultimas_transacoes.Add(dto);
                    statement.Ultimas_transacoes = [.. statement.Ultimas_transacoes.OrderByDescending(x => x.Realizada_em)];
                    if (statement.Ultimas_transacoes.Count > 10)
                        statement.Ultimas_transacoes.RemoveAt(10);


                    var updated = await AppDbContext.TryUpdateBalance(context, id, value, JsonSerializer.Serialize(statement, AppJsonSerializerContext.Default.StatementDto));

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

                StatementDto? statement = customer.LastStatement != null
                 ? JsonSerializer.Deserialize<StatementDto>(customer.LastStatement, AppJsonSerializerContext.Default.StatementDto)
                 : new StatementDto
                 {
                     Saldo = new BalanceDto
                     {
                         Limite = customer.Limit,
                         Total = customer.Balance
                     }
                 };

                return Results.Ok(statement);
            }).DisableRequestTimeout();

            return group;
        }

        // **Helper method for concise statement deserialization:**
        static StatementDto DeserializeStatement(string? json) =>
            string.IsNullOrEmpty(json) ? new StatementDto() : JsonSerializer.Deserialize<StatementDto>(json, AppJsonSerializerContext.Default.StatementDto);
    }
}
