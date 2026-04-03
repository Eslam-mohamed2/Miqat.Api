using Miqat.Application.Common;
using System.Net;
using System.Text.Json;

namespace Miqat.API.Middleware
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
                await HandleExceptionAsync(context, ex);
            }
        }

        private static async Task HandleExceptionAsync(
            HttpContext context, Exception ex)
        {
            context.Response.ContentType = "application/json";

            var statusCode = ex switch
            {
                ApiException apiEx => apiEx.StatusCode,
                UnauthorizedAccessException _ => (int)HttpStatusCode.Unauthorized,
                KeyNotFoundException _ => (int)HttpStatusCode.NotFound,
                ArgumentException _ => (int)HttpStatusCode.BadRequest,
                _ => (int)HttpStatusCode.InternalServerError
            };

            var message = ex switch
            {
                ApiException _ => ex.Message,
                UnauthorizedAccessException _ => ex.Message,
                KeyNotFoundException _ => ex.Message,
                ArgumentException _ => ex.Message,
                _ => "An unexpected error occurred."
            };

            context.Response.StatusCode = statusCode;

            var response = ApiResponse<object>.Fail(message);

            var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(json);
        }
    }
}
