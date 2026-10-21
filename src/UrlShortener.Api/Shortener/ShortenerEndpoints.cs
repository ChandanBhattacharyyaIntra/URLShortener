namespace UrlShortener.Api.Shortener;

public static class ShortenerEndpoints
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    public static void MapShortener(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Json(new
        {
            status = "ok",
            uptimeSeconds = (int)(DateTimeOffset.UtcNow - StartedAt).TotalSeconds
        }));

        app.MapGet("/metrics", (JsonUrlStore store) =>
        {
            var urls = store.List();
            return Results.Json(new
            {
                urls = urls.Count,
                active = urls.Count(u => u.Active),
                clicks = urls.Sum(u => u.Clicks)
            });
        });

        app.MapPost("/api/urls", (CreateShortUrlRequest body, UrlShortenerService service, HttpContext http) =>
        {
            var record = service.Create(body);
            var shortUrl = $"{http.Request.Scheme}://{http.Request.Host}/{record.Code}";
            return Results.Created($"/{record.Code}", new
            {
                record.Code,
                record.OriginalUrl,
                record.CreatedAt,
                record.ExpiresAt,
                record.Clicks,
                record.CustomAlias,
                record.Active,
                shortUrl
            });
        });

        app.MapGet("/api/urls", (UrlShortenerService service) => Results.Json(new { items = service.List() }));
        app.MapGet("/api/urls/{code}", (string code, UrlShortenerService service) => Results.Json(service.Get(code)));
        app.MapGet("/api/urls/{code}/analytics", (string code, UrlShortenerService service) => Results.Json(service.Analytics(code)));
        app.MapPost("/api/urls/{code}/deactivate", (string code, UrlShortenerService service) => Results.Json(service.Deactivate(code)));

        app.MapGet("/{code}", (string code, UrlShortenerService service, HttpContext http) =>
        {
            if (code.Contains('.')) return Results.NotFound();
            var record = service.Resolve(code, http.Request.Headers.Referer.ToString(), http.Request.Headers.UserAgent.ToString());
            return Results.Redirect(record.OriginalUrl, permanent: false);
        });
    }
}
