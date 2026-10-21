namespace UrlShortener.Api.Orchestrator;

public enum StageName { Requirements, Architecture, Implementation, TestPlanning, Testing, Documentation, Release }
public enum NodeStatus { Pending, Ready, Running, AwaitingApproval, Succeeded, Failed, Skipped, RolledBack, Stopped, Blocked }
public enum ScenarioKind { Greenfield, Brownfield, Ambiguous }
public enum ApprovalMode { Auto, Manual }

public sealed record Gate(string Id, string Kind, string Description, string Check);
public sealed record RetryPolicy(int MaxAttempts, int BackoffMs);

public sealed class GraphNodeDef
{
    public required string Id { get; init; }
    public required StageName Stage { get; init; }
    public required IReadOnlyList<string> DependsOn { get; init; }
    public bool RequiresApproval { get; init; }
    public string? ApprovalReason { get; init; }
    public required RetryPolicy Retry { get; init; }
    public bool RollbackOnFailure { get; init; }
    public string? ParallelGroup { get; init; }
    public required IReadOnlyList<Gate> EntryGates { get; init; }
    public required IReadOnlyList<Gate> ExitGates { get; init; }
}

public sealed record Decision(string Id, DateTimeOffset At, string NodeId, string Actor, string Summary, string Rationale);
public sealed record Artifact(string Id, string NodeId, string Kind, string Title, object Body, DateTimeOffset CreatedAt);
public sealed record PolicyViolation(string Policy, string Severity, string Message);

public sealed class StageResult
{
    public bool Ok { get; init; }
    public required string Summary { get; init; }
    public List<Artifact> Artifacts { get; init; } = [];
    public List<Decision> Decisions { get; init; } = [];
    public List<string> Assumptions { get; init; } = [];
    public List<string> Risks { get; init; } = [];
    public List<string>? ImpactedModules { get; init; }
    public List<string>? Ambiguities { get; init; }
    public bool Retryable { get; init; } = true;
}

public sealed class NodeRuntime
{
    public required GraphNodeDef Def { get; init; }
    public NodeStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? Error { get; set; }
    public StageResult? Result { get; set; }
    public Dictionary<string, object>? Snapshot { get; set; }
}

public sealed record ReliabilityMetrics(
    double SuccessRate,
    int RetryCount,
    int RollbackCount,
    int? MttrMs,
    int? EndToEndLatencyMs,
    int NodesSucceeded,
    int NodesFailed);

public sealed record AuditEvent(DateTimeOffset At, string Type, string? NodeId, Dictionary<string, object> Detail);

public sealed class PendingApproval
{
    public required string NodeId { get; init; }
    public required string Reason { get; init; }
    public required string Risk { get; init; }
}

public sealed class OrchestratorRun
{
    public required string Id { get; init; }
    public required ScenarioKind Scenario { get; init; }
    public required string Title { get; init; }
    public required string Requirement { get; init; }
    public string Status { get; set; } = "running";
    public required ApprovalMode ApprovalMode { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; set; }
    public required Dictionary<string, NodeRuntime> Nodes { get; init; }
    public List<Decision> Lineage { get; } = [];
    public List<Artifact> Artifacts { get; } = [];
    public List<AuditEvent> Events { get; } = [];
    public ReliabilityMetrics Metrics { get; set; } = new(0, 0, 0, null, null, 0, 0);
    public PendingApproval? PendingApproval { get; set; }
    public int ReplanCount { get; set; }
    public bool StopRequested { get; set; }
}

public sealed class RequirementContext
{
    public required ScenarioKind Scenario { get; init; }
    public required string Title { get; init; }
    public required string Requirement { get; init; }
    public required IReadOnlyList<string> KnownModules { get; init; }
    public required IReadOnlyList<string> Constraints { get; init; }
}
