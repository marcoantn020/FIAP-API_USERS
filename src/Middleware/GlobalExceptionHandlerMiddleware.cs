using System.Net;
using System.Text.Json;
using users_api.Common;
using users_api.Exceptions;

namespace users_api.Middleware;

public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlerMiddleware> logger)
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
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, response) = exception switch
        {
            ValidationException validationEx => (
                HttpStatusCode.BadRequest,
                ApiResponse<object>.ErrorResponse(
                    validationEx.Message,
                    validationEx.Errors
                )
            ),
            UnauthorizedException unauthorizedEx => (
                HttpStatusCode.Unauthorized,
                ApiResponse<object>.ErrorResponse(unauthorizedEx.Message)
            ),
            BusinessException businessEx => (
                HttpStatusCode.BadRequest,
                ApiResponse<object>.ErrorResponse(businessEx.Message)
            ),
            UnauthorizedAccessException => (
                HttpStatusCode.Unauthorized,
                ApiResponse<object>.ErrorResponse("Invalid credentials.")
            ),
            InvalidOperationException invalidOpEx => (
                HttpStatusCode.BadRequest,
                ApiResponse<object>.ErrorResponse(invalidOpEx.Message)
            ),
            _ => (
                HttpStatusCode.InternalServerError,
                ApiResponse<object>.ErrorResponse("An unexpected error occurred.")
            )
        };

        _logger.LogError(exception,
            "Exception occurred: {ExceptionType} - {Message}",
            exception.GetType().Name,
            exception.Message);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(jsonResponse);
    }
}
