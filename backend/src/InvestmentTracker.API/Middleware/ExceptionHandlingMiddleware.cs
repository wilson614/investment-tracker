using System.Net;
using System.Text.Json;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Infrastructure.MarketData;

namespace InvestmentTracker.API.Middleware;

/// <summary>
/// Global exception handling middleware for consistent error responses.
/// </summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message, shouldLog) = exception switch
        {
            // Domain exceptions - 業務邏輯錯誤，不需要記錄為 Error
            EntityNotFoundException => (HttpStatusCode.NotFound, exception.Message, false),
            AccessDeniedException => (HttpStatusCode.Forbidden, exception.Message, false),
            BusinessRuleException => (HttpStatusCode.BadRequest, exception.Message, false),

            // Infrastructure exceptions
            StooqDailyHitsLimitExceededException =>
                (HttpStatusCode.TooManyRequests, "Stooq daily hits limit exceeded", true),

            // Standard exceptions - 保持向後相容
            UnauthorizedAccessException => (HttpStatusCode.Forbidden, "Access denied", false),
            KeyNotFoundException => (HttpStatusCode.NotFound, exception.Message, false),
            ArgumentException => (HttpStatusCode.BadRequest, exception.Message, false),
            InvalidOperationException => (HttpStatusCode.BadRequest, exception.Message, false),

            // Unexpected exceptions
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred", true)
        };

        if (shouldLog)
        {
            logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
        }
        else
        {
            logger.LogWarning("Handled exception: {ExceptionType} - {Message}",
                exception.GetType().Name, exception.Message);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = new
        {
            error = message,
            statusCode = (int)statusCode,
            timestamp = DateTime.UtcNow
        };

        var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(jsonResponse);
    }
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static void UseExceptionHandling(this IApplicationBuilder builder)
    {
        builder.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
