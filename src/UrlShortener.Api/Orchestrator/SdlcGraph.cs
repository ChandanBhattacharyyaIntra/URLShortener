namespace UrlShortener.Api.Orchestrator;

public static class SdlcGraph
{
    public static IReadOnlyList<GraphNodeDef> Nodes { get; } =
    [
        Node("requirements", StageName.Requirements, [], false, null, 2, 25, false, null,
            [G("req-in", "entry", "Requirement text is present", "requirement.nonempty")],
            [G("req-out", "exit", "Normalized intent and ambiguities recorded", "spec.normalized")]),
        Node("architecture", StageName.Architecture, ["requirements"], true,
            "Architecture and schema decisions are high-impact change-control items", 2, 25, true, null,
            [G("arch-in", "entry", "Requirements artifacts exist", "artifacts.requirements")],
            [G("arch-out", "exit", "ADRs and impacted modules published", "adr.complete")]),
        Node("implementation", StageName.Implementation, ["architecture"], false, null, 3, 40, true, "build",
            [G("impl-in", "entry", "Approved architecture in context", "architecture.approved")],
            [G("impl-out", "exit", "Implementation evidence against spec", "impl.evidence")]),
        Node("test_planning", StageName.TestPlanning, ["architecture"], false, null, 2, 25, false, "build",
            [G("tp-in", "entry", "Architecture available for test design", "architecture.approved")],
            [G("tp-out", "exit", "Test plan covers happy path and failure cases", "testplan.coverage")]),
        Node("testing", StageName.Testing, ["implementation", "test_planning"], false, null, 2, 40, true, "verify",
            [G("test-in", "entry", "Implementation and test plan both complete (sync join)", "join.build")],
            [G("test-out", "exit", "Validation evidence recorded", "tests.evidence")]),
        Node("documentation", StageName.Documentation, ["implementation"], false, null, 2, 25, false, "verify",
            [G("doc-in", "entry", "Implementation artifacts available", "impl.evidence")],
            [G("doc-out", "exit", "Operator and API docs produced", "docs.complete")]),
        Node("release", StageName.Release, ["testing", "documentation"], true,
            "Release readiness is a human-owned quality gate", 1, 0, true, null,
            [G("rel-in", "entry", "Testing and documentation synchronized", "join.verify")],
            [G("rel-out", "exit", "Release checklist complete and signed", "release.signed")])
    ];

    public static IReadOnlyList<GraphNodeDef> Ready(HashSet<string> done, IEnumerable<GraphNodeDef> remaining) =>
        remaining.Where(n => n.DependsOn.All(done.Contains)).ToList();

    public static List<List<string>> Layers()
    {
        var remaining = Nodes.ToList();
        var done = new HashSet<string>();
        var layers = new List<List<string>>();
        while (remaining.Count > 0)
        {
            var ready = Ready(done, remaining);
            if (ready.Count == 0) throw new InvalidOperationException("cycle in orchestration graph");
            layers.Add(ready.Select(n => n.Id).ToList());
            foreach (var n in ready)
            {
                done.Add(n.Id);
                remaining.Remove(n);
            }
        }
        return layers;
    }

    private static GraphNodeDef Node(
        string id, StageName stage, string[] deps, bool approval, string? reason,
        int retries, int backoff, bool rollback, string? group,
        Gate[] entry, Gate[] exit) => new()
    {
        Id = id,
        Stage = stage,
        DependsOn = deps,
        RequiresApproval = approval,
        ApprovalReason = reason,
        Retry = new RetryPolicy(retries, backoff),
        RollbackOnFailure = rollback,
        ParallelGroup = group,
        EntryGates = entry,
        ExitGates = exit
    };

    private static Gate G(string id, string kind, string description, string check) =>
        new(id, kind, description, check);
}
