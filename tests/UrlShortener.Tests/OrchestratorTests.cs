using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using UrlShortener.Api.Orchestrator;

namespace UrlShortener.Tests;

public class OrchestratorTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public OrchestratorTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("greenfield")]
    [InlineData("brownfield")]
    [InlineData("ambiguous")]
    public async Task Scenario_Completes_With_Lineage_And_Metrics(string scenario)
    {
        var res = await _client.PostAsJsonAsync("/api/orchestrator/runs", new { scenario, approvalMode = "auto" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("succeeded", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.GetProperty("artifacts").GetArrayLength() >= 6);
        Assert.True(doc.RootElement.GetProperty("lineage").GetArrayLength() >= 6);
        Assert.Equal(7, doc.RootElement.GetProperty("metrics").GetProperty("nodesSucceeded").GetInt32());
        Assert.Contains(doc.RootElement.GetProperty("events").EnumerateArray(), e => e.GetProperty("type").GetString() == "wave.parallel");
        Assert.Contains(doc.RootElement.GetProperty("events").EnumerateArray(), e => e.GetProperty("type").GetString() == "gate.entry");
        Assert.Contains(doc.RootElement.GetProperty("events").EnumerateArray(), e => e.GetProperty("type").GetString() == "approval.auto");
    }

    [Fact]
    public async Task Graph_Has_Parallel_Build_And_Human_Gates()
    {
        var res = await _client.GetAsync("/api/orchestrator/graph");
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var layers = doc.RootElement.GetProperty("layers");
        Assert.Equal("requirements", layers[0][0].GetString());
        var build = layers[2].EnumerateArray().Select(x => x.GetString()).ToHashSet();
        Assert.Contains("implementation", build);
        Assert.Contains("test_planning", build);

        var nodes = doc.RootElement.GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("id").GetString()!);
        Assert.True(nodes["architecture"].GetProperty("requiresApproval").GetBoolean());
        Assert.True(nodes["release"].GetProperty("requiresApproval").GetBoolean());
    }

    [Fact]
    public void Ambiguous_Scenario_Records_Assumptions()
    {
        var ctx = ScenarioCatalog.All["ambiguous"];
        var result = StageAgents.Run("requirements", ctx, []);
        Assert.True(result.Ok);
        Assert.NotEmpty(result.Ambiguities!);
        Assert.Contains(result.Assumptions, a => a.Contains("unauthenticated", StringComparison.OrdinalIgnoreCase));
    }
}
