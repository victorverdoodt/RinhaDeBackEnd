namespace RinhaDeBackEnd_AOT.Middlewares
{
    public class BadHttpRequestExceptionMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await next(context);
            }
            catch (BadHttpRequestException)
            {
                context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            }
        }
    }
}
