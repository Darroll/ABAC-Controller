using System.Diagnostics;

namespace AbacController.Api.Middleware;

/// <summary>
/// Adds correlation ID to every request for structured logging and distributed tracing.
/// Reads X-Correlation-Id header or generates a new one; propagates to response.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing)
            && !string.IsNullOrWhiteSpace(existing.FirstOrDefault())
                ? existing.First()!
                : Activity.Current?.Id ?? Guid.NewGuid().ToString("N");

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        // Push to log scope so all log entries within this request carry the correlation ID
        using (context.RequestServices.GetRequiredService<ILoggerFactory>()
                   .CreateLogger("AbacController")
                   .BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}
