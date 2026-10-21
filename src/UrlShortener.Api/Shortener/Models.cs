namespace UrlShortener.Api.Shortener;

public sealed record ShortUrl(
    string Code,
    string OriginalUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    int Clicks,
    DateTimeOffset? LastAccessedAt,
    bool CustomAlias,
    bool Active,
    string CreatedBy);

public sealed record ClickEvent(
    string Id,
    string Code,
    DateTimeOffset Timestamp,
    string Referrer,
    string UserAgent);

public sealed record CreateShortUrlRequest(
    string Url,
    string? CustomAlias,
    int? ExpiresInSeconds,
    string? CreatedBy);

public sealed record AnalyticsSummary(
    string Code,
    string OriginalUrl,
    int Clicks,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastAccessedAt,
    bool Active,
    IReadOnlyDictionary<string, int> ClicksByDay,
    IReadOnlyList<ReferrerCount> TopReferrers);

public sealed record ReferrerCount(string Referrer, int Count);

public sealed class AppException : Exception
{
    public int Status { get; }
    public AppException(int status, string message) : base(message) => Status = status;
}

public sealed class StoreSnapshot
{
    public List<ShortUrl> Urls { get; set; } = [];
    public List<ClickEvent> Clicks { get; set; } = [];
}
