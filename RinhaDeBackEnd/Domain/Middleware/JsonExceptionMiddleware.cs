using System.Text.Json;

namespace RinhaDeBackEnd.Domain.Middleware
{
    public class JsonExceptionMiddleware
    {
        private readonly RequestDelegate _next;

        public JsonExceptionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (BadHttpRequestException ex) when (ex.InnerException is JsonException)
            {
                context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                await context.Response.WriteAsync("Ocorreu um erro na deserialização do JSON.");
            }
        }
    }

}
