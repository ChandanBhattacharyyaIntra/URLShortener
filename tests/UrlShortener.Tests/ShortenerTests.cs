using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UrlShortener.Tests;

public class ShortenerTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public ShortenerTests(ApiFactory factory)
    {
        _client = factory.CreateClient(new() { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task Health_IsOk()
    {
        var res = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Create_Redirect_And_Analytics()
    {
        var created = await _client.PostAsJsonAsync("/api/urls", new { url = "https://example.com/campaign" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var code = body.RootElement.GetProperty("code").GetString();
        Assert.False(string.IsNullOrWhiteSpace(code));

        var redirect = await _client.GetAsync("/" + code);
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("https://example.com/campaign", redirect.Headers.Location?.ToString());

        var analytics = await _client.GetAsync($"/api/urls/{code}/analytics");
        analytics.EnsureSuccessStatusCode();
        using var a = JsonDocument.Parse(await analytics.Content.ReadAsStringAsync());
        Assert.Equal(1, a.RootElement.GetProperty("clicks").GetInt32());
    }

    [Fact]
    public async Task Rejects_Unsafe_Scheme()
    {
        var res = await _client.PostAsJsonAsync("/api/urls", new { url = "javascript:alert(1)" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task CustomAlias_Conflict_And_Deactivate()
    {
        var first = await _client.PostAsJsonAsync("/api/urls", new { url = "https://example.com/a", customAlias = "promo-one" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var dup = await _client.PostAsJsonAsync("/api/urls", new { url = "https://example.com/b", customAlias = "promo-one" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        var off = await _client.PostAsync("/api/urls/promo-one/deactivate", null);
        off.EnsureSuccessStatusCode();
        var gone = await _client.GetAsync("/promo-one");
        Assert.Equal(HttpStatusCode.Gone, gone.StatusCode);
    }
}
