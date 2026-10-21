namespace UrlShortener.Api.Orchestrator;

public static class ScenarioCatalog
{
    public static readonly IReadOnlyDictionary<string, RequirementContext> All = new Dictionary<string, RequirementContext>(StringComparer.OrdinalIgnoreCase)
    {
        ["greenfield"] = new()
        {
            Scenario = ScenarioKind.Greenfield,
            Title = "Greenfield — URL shortener MVP",
            Requirement = "Build a URL shortener from scratch with create/redirect APIs, click analytics, link expiration, custom aliases, and reliability controls (validation, rate limits, health).",
            KnownModules = [],
            Constraints = ["No native database drivers required", "Must run with dotnet run", "Production-grade validation"]
        },
        ["brownfield"] = new()
        {
            Scenario = ScenarioKind.Brownfield,
            Title = "Brownfield — deactivate + mutation audit",
            Requirement = "The shortener is already in use. Add the ability to disable an existing code without breaking historical analytics, identify impacted modules, and keep redirects fail-closed (410).",
            KnownModules =
            [
                "Shortener/UrlShortenerService.cs",
                "Shortener/JsonUrlStore.cs",
                "Shortener/ShortenerEndpoints.cs",
                "Shortener/UrlValidator.cs"
            ],
            Constraints = ["Do not change existing short-code length", "Preserve click history", "Change-control approval before architecture"]
        },
        ["ambiguous"] = new()
        {
            Scenario = ScenarioKind.Ambiguous,
            Title = "Ambiguous — 'make it like bit.ly'",
            Requirement = "We need something like bit.ly for campaigns. Make URLs better. Analytics should be good. Ship soon.",
            KnownModules = ["Shortener/"],
            Constraints = ["Two-day calendar window", "Unknown traffic", "Unknown auth requirements"]
        }
    };
}
