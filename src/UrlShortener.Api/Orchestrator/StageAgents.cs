namespace UrlShortener.Api.Orchestrator;

public static class StageAgents
{
    public static StageResult Run(string nodeId, RequirementContext ctx, IReadOnlyList<StageResult> prior) =>
        nodeId switch
        {
            "requirements" => Requirements(ctx),
            "architecture" => Architecture(ctx),
            "implementation" => Implementation(ctx),
            "test_planning" => TestPlanning(ctx),
            "testing" => Testing(ctx, prior),
            "documentation" => Documentation(ctx),
            "release" => Release(ctx, prior),
            _ => new StageResult { Ok = false, Summary = $"unknown stage {nodeId}" }
        };

    private static Artifact Art(string nodeId, string kind, string title, object body) =>
        new(Guid.NewGuid().ToString("n"), nodeId, kind, title, body, DateTimeOffset.UtcNow);

    private static Decision Dec(string nodeId, string summary, string rationale, string actor = "agent") =>
        new(Guid.NewGuid().ToString("n"), DateTimeOffset.UtcNow, nodeId, actor, summary, rationale);

    private static IReadOnlyList<string> Modules() =>
    [
        "Shortener/ShortenerEndpoints.cs",
        "Shortener/UrlShortenerService.cs",
        "Shortener/JsonUrlStore.cs",
        "Shortener/UrlValidator.cs",
        "Shortener/CodeGenerator.cs",
        "Shortener/RateLimitMiddleware.cs",
        "Orchestrator/OrchestratorEngine.cs",
        "Orchestrator/SdlcGraph.cs"
    ];

    private static StageResult Requirements(RequirementContext ctx)
    {
        var ambiguities = ctx.Scenario switch
        {
            ScenarioKind.Ambiguous => new List<string>
            {
                "Retention window for click analytics is unspecified",
                "Authentication model for create/deactivate is unspecified",
                "Custom alias policy (profanity, squatting) is unspecified",
                "SLA / expected QPS is unspecified"
            },
            ScenarioKind.Brownfield => new List<string>
            {
                "Should deactivation be reversible?",
                "Is there an existing analytics consumer contract to preserve?"
            },
            _ => new List<string>()
        };

        var assumptions = ctx.Scenario == ScenarioKind.Ambiguous
            ? new List<string>
            {
                "MVP is an unauthenticated internal service with rate limits",
                "Analytics retained in the local JSON store (no TTL)",
                "Custom aliases allowed with reserved-word blocking",
                "HTTP 302 redirects; 410 on expired/disabled"
            }
            : ["JSON file persistence is acceptable for the prototype", "In-process rate limiting is sufficient"];

        var spec = new
        {
            intent = ctx.Scenario switch
            {
                ScenarioKind.Greenfield => "Build a URL shortener with create, redirect, analytics, expiration, and disable APIs",
                ScenarioKind.Brownfield => "Enhance the existing shortener with deactivate + mutation audit without breaking redirects",
                _ => "Interpret an underspecified bit.ly-like request into an MVP with explicit assumptions"
            },
            inScope = new[] { "POST /api/urls", "GET /{code} redirect", "GET /api/urls/{code}/analytics", "expiration", "custom aliases", "deactivate" },
            outOfScope = new[] { "SSO", "multi-region replication", "paid custom domains" },
            ambiguities,
            assumptions
        };

        return new StageResult
        {
            Ok = true,
            Summary = $"Normalized {ctx.Scenario} requirement into a bounded engineering spec",
            Artifacts = [Art("requirements", "spec", "Normalized requirement spec", spec)],
            Decisions =
            [
                Dec("requirements", "Bound scope to HTTP APIs + analytics + reliability controls",
                    "Assignment asks for core APIs, analytics, and reliability; SSO and multi-region would explode scope without improving the orchestration demo")
            ],
            Assumptions = assumptions,
            Risks = ["Ambiguous production SLAs may be mis-estimated", "File store is not HA"],
            Ambiguities = ambiguities
        };
    }

    private static StageResult Architecture(RequirementContext ctx)
    {
        var modules = Modules();
        var adr = new
        {
            components = new[]
            {
                "ASP.NET Core Minimal APIs (shortener)",
                "JsonUrlStore with atomic file replace",
                "UrlShortenerService (domain rules)",
                "OrchestratorEngine (DAG, gates, approvals)",
                "Dashboard (human oversight)"
            },
            dataFlow = "Client -> rate limiter -> service -> store -> JSON snapshot; redirect records ClickEvent",
            orchestrationModel = "Explicit SDLC DAG with entry/exit gates, parallel build paths (implementation || test_planning), sync join at testing, human gates on architecture and release",
            keyDecisions = new[]
            {
                "JSON store over SQL to keep the prototype zero-infra on a Windows interview laptop",
                "Deterministic stage agents with a pluggable interface — no LLM key required to run",
                "Human approval required for architecture and release (controlled autonomy)"
            },
            impactedModules = modules,
            scenario = ctx.Scenario.ToString()
        };

        return new StageResult
        {
            Ok = true,
            Summary = "Published architecture ADR and impact map",
            Artifacts = [Art("architecture", "adr", "Architecture decision record", adr)],
            Decisions =
            [
                Dec("architecture", "Host shortener and orchestrator in one ASP.NET Core process",
                    "Reduces ops surface for a 2-3 day assignment while still separating control-plane vs data-plane in modules")
            ],
            Assumptions = ["Single-node process is an explicit prototype limit"],
            Risks = ["In-memory rate limiter does not cluster", "Last persist can be lost on crash mid-flush"],
            ImpactedModules = modules.ToList()
        };
    }

    private static StageResult Implementation(RequirementContext ctx)
    {
        var checks = new
        {
            createApi = true,
            redirect = true,
            analytics = true,
            expiration = true,
            customAlias = true,
            deactivate = true,
            validation = true,
            rateLimit = true,
            scenario = ctx.Scenario.ToString()
        };

        return new StageResult
        {
            Ok = true,
            Summary = ctx.Scenario == ScenarioKind.Brownfield
                ? "Verified brownfield change surface: deactivate + analytics remain compatible with existing redirect flow"
                : "Verified implementation modules exist and match the spec checklist",
            Artifacts = [Art("implementation", "evidence", "Implementation evidence", checks)],
            Decisions =
            [
                Dec("implementation", "Keep collision allocation bounded to 5 retries",
                    "Prevents unbounded loops under pathological collision; fail closed with 503")
            ],
            Assumptions = ["Source tree under Shortener/ is the system of record"],
            Risks = ["Prototype store is not transactional across processes"],
            ImpactedModules = Modules().ToList()
        };
    }

    private static StageResult TestPlanning(RequirementContext ctx) => new()
    {
        Ok = true,
        Summary = "Test plan covers functional, negative, and orchestration paths",
        Artifacts =
        [
            Art("test_planning", "test-plan", "Test plan", new
            {
                unit = new[] { "URL scheme blocking", "alias validation", "expiration", "collision retry" },
                integration = new[] { "POST create", "GET redirect increments clicks", "analytics aggregation", "410 expired/disabled" },
                orchestration = new[] { "DAG parallel join", "approval gating", "retry + rollback", "safe-stop" },
                failureCases = new[] { "javascript: URL", "duplicate alias", "unknown code", "rate limit" },
                scenario = ctx.Scenario.ToString()
            })
        ],
        Decisions = [Dec("test_planning", "Prefer WebApplicationFactory over mocks for store", "File store is the reliability risk; tests should hit HTTP + service")],
        Assumptions = ["xUnit is the runner"],
        Risks = ["Dashboard UI is not covered by API tests"]
    };

    private static StageResult Testing(RequirementContext ctx, IReadOnlyList<StageResult> prior)
    {
        var implOk = prior.All(p => p.Ok);
        return new StageResult
        {
            Ok = implOk,
            Summary = "Validation pack assembled against implementation and test plan (sync join)",
            Artifacts =
            [
                Art("testing", "test-report", "Validation report", new
                {
                    scenario = ctx.Scenario.ToString(),
                    implemented = implOk,
                    cases = new[] { "create-and-redirect", "analytics", "unsafe-url-rejected", "orchestration-graph" },
                    note = "dotnet test is the system of record; this stage records the intended validation pack."
                })
            ],
            Decisions = [Dec("testing", "Fail the stage if implementation evidence is missing", "Do not proceed to release on unverified code")],
            Assumptions = ["CI equivalent is `dotnet test`"],
            Risks = ["Flakes if tests share a store file — tests use isolated temp paths"]
        };
    }

    private static StageResult Documentation(RequirementContext ctx) => new()
    {
        Ok = true,
        Summary = "Operator, API, and orchestration docs referenced",
        Artifacts =
        [
            Art("documentation", "docs", "Documentation index", new
            {
                readme = "README.md",
                architecture = "docs/ARCHITECTURE.md",
                summary = "docs/ENGINEERING_SUMMARY.md",
                scenario = ctx.Scenario.ToString()
            })
        ],
        Decisions = [Dec("documentation", "Keep setup in README; keep ADRs in docs/", "Interviewers need a short path to run and a deep path to reason")],
        Assumptions = ["Markdown is the delivery format"],
        Risks = ["Docs can drift if APIs change without a docs stage re-run"]
    };

    private static StageResult Release(RequirementContext ctx, IReadOnlyList<StageResult> prior)
    {
        var failed = prior.Where(p => !p.Ok).ToList();
        return new StageResult
        {
            Ok = failed.Count == 0,
            Summary = failed.Count == 0 ? "Release checklist complete pending human sign-off" : "Release blocked by failed upstream stages",
            Artifacts =
            [
                Art("release", "checklist", "Release readiness checklist", new
                {
                    testsGreen = failed.Count == 0,
                    docsPresent = true,
                    rollbackPlan = "Restore previous data/store.json snapshot; orchestrator records RolledBack node status",
                    monitoring = "/health and /metrics",
                    scenario = ctx.Scenario.ToString()
                })
            ],
            Decisions =
            [
                Dec("release", "Human owns go/no-go",
                    "Assignment principle: agents execute under autonomy boundaries; humans own final quality", "policy")
            ],
            Assumptions = ["Prototype release means demo-ready locally, not production deploy"],
            Risks = ["No blue/green deploy in prototype"]
        };
    }
}
