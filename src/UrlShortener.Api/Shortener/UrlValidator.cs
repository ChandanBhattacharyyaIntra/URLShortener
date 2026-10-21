namespace UrlShortener.Api.Shortener;

public static class UrlValidator
{
    private static readonly string[] BlockedSchemes = ["javascript:", "data:", "file:", "vbscript:"];
    private const int MaxLength = 2048;

    public static string AssertSafe(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new AppException(400, "url is required");

        var url = raw.Trim();
        if (url.Length > MaxLength)
            throw new AppException(400, "url exceeds maximum length");

        var lower = url.ToLowerInvariant();
        if (BlockedSchemes.Any(s => lower.StartsWith(s, StringComparison.Ordinal)))
            throw new AppException(400, "url scheme is not allowed");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            throw new AppException(400, "url is not a valid absolute URL");

        if (parsed.Scheme is not "http" and not "https")
            throw new AppException(400, "only http and https URLs are allowed");

        return parsed.ToString();
    }

    public static int? ParseExpires(int? value)
    {
        if (value is null) return null;
        if (value <= 0 || value > 60 * 60 * 24 * 365 * 5)
            throw new AppException(400, "expiresInSeconds must be a positive integer (max 5 years)");
        return value;
    }
}
