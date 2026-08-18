using Microsoft.AspNetCore.Mvc;

namespace SalesData.Api.Middleware;

public sealed class RequestCancellationMiddleware(RequestDelegate next, ILogger<RequestCancellationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request cancelled by client: {Method} {Path}", context.Request.Method, context.Request.Path);
            if (!context.Response.HasStarted) context.Response.StatusCode = 499;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "Operation timed out: {Method} {Path}", context.Request.Method, context.Request.Path);
            if (context.Response.HasStarted) return;

            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status504GatewayTimeout,
                Title = "Request timed out",
                Detail = "The operation did not complete in time. Please try again."
            }, CancellationToken.None);
        }
    }
}
