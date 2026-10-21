using System.Collections.Concurrent;

namespace UrlShortener.Api.Shortener;

public sealed class RateLimitMiddleware(RequestDelegate next)
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";

        var write = HttpMethods.IsPost(context.Request.Method)
                    || HttpMethods.IsPut(context.Request.Method)
                    || HttpMethods.IsPatch(context.Request.Method)
                    || HttpMethods.IsDelete(context.Request.Method);
        var capacity = write ? 30d : 120d;
        var refill = write ? 5d : 20d;
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "local";

        if (!Allow(key, capacity, refill))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new { error = "rate limit exceeded" });
            return;
        }

        await next(context);
    }

    private bool Allow(string key, double capacity, double refillPerSecond)
    {
        var now = DateTimeOffset.UtcNow;
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket(capacity, now));
        lock (bucket)
        {
            var elapsed = (now - bucket.UpdatedAt).TotalSeconds;
            bucket.Tokens = Math.Min(capacity, bucket.Tokens + elapsed * refillPerSecond);
            bucket.UpdatedAt = now;
            if (bucket.Tokens < 1) return false;
            bucket.Tokens -= 1;
            return true;
        }
    }

    private sealed class Bucket(double tokens, DateTimeOffset updatedAt)
    {
        public double Tokens = tokens;
        public DateTimeOffset UpdatedAt = updatedAt;
    }
}
