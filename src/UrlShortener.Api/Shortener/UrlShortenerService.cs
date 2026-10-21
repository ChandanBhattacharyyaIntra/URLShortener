namespace UrlShortener.Api.Shortener;

public sealed class UrlShortenerService(JsonUrlStore store)
{
    private const int MaxCollisionRetries = 5;

    public ShortUrl Create(CreateShortUrlRequest input)
    {
        var original = UrlValidator.AssertSafe(input.Url);
        var expiresIn = UrlValidator.ParseExpires(input.ExpiresInSeconds);
        string code;
        var custom = false;

        if (!string.IsNullOrWhiteSpace(input.CustomAlias))
        {
            if (!CodeGenerator.IsValidAlias(input.CustomAlias))
                throw new AppException(400, "customAlias must be 3-32 chars (letters, numbers, _ or -) and not reserved");
            if (store.Get(input.CustomAlias) is not null)
                throw new AppException(409, "customAlias is already taken");
            code = input.CustomAlias;
            custom = true;
        }
        else
        {
            code = AllocateCode();
        }

        var now = DateTimeOffset.UtcNow;
        var record = new ShortUrl(
            code,
            original,
            now,
            expiresIn is null ? null : now.AddSeconds(expiresIn.Value),
            0,
            null,
            custom,
            true,
            input.CreatedBy ?? "api");
        store.Put(record);
        return record;
    }

    public ShortUrl Resolve(string code, string? referrer, string? userAgent)
    {
        var record = Require(code);
        if (!record.Active)
            throw new AppException(410, "link is disabled");
        if (record.ExpiresAt is { } exp && exp <= DateTimeOffset.UtcNow)
            throw new AppException(410, "link has expired");

        var accessed = DateTimeOffset.UtcNow;
        var updated = record with { Clicks = record.Clicks + 1, LastAccessedAt = accessed };
        store.Put(updated);
        store.AddClick(new ClickEvent(
            Guid.NewGuid().ToString("n"),
            code,
            accessed,
            string.IsNullOrWhiteSpace(referrer) ? "direct" : referrer,
            string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent));
        return updated;
    }

    public ShortUrl Get(string code) => Require(code);

    public ShortUrl Deactivate(string code)
    {
        var record = Require(code) with { Active = false };
        store.Put(record);
        return record;
    }

    public IReadOnlyList<ShortUrl> List() => store.List();

    public AnalyticsSummary Analytics(string code)
    {
        var record = Require(code);
        var events = store.ClicksFor(code);
        var byDay = events
            .GroupBy(e => e.Timestamp.UtcDateTime.ToString("yyyy-MM-dd"))
            .ToDictionary(g => g.Key, g => g.Count());
        var referrers = events
            .GroupBy(e => e.Referrer)
            .Select(g => new ReferrerCount(g.Key, g.Count()))
            .OrderByDescending(r => r.Count)
            .Take(10)
            .ToList();

        return new AnalyticsSummary(
            record.Code,
            record.OriginalUrl,
            record.Clicks,
            record.CreatedAt,
            record.ExpiresAt,
            record.LastAccessedAt,
            record.Active,
            byDay,
            referrers);
    }

    private ShortUrl Require(string code)
    {
        if (CodeGenerator.IsReserved(code))
            throw new AppException(404, "not found");
        return store.Get(code) ?? throw new AppException(404, "not found");
    }

    private string AllocateCode()
    {
        for (var i = 0; i < MaxCollisionRetries; i++)
        {
            var code = CodeGenerator.Next();
            if (store.Get(code) is null && !CodeGenerator.IsReserved(code))
                return code;
        }

        throw new AppException(503, "unable to allocate a unique code");
    }
}
