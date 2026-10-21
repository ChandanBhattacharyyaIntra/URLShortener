using System.Collections.Concurrent;
using UrlShortener.Api.Shortener;

namespace UrlShortener.Api.Orchestrator;

public sealed class OrchestratorEngine
{
    private readonly ConcurrentDictionary<string, OrchestratorRun> _runs = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _approvals = new();

    public OrchestratorRun? Get(string id) => _runs.TryGetValue(id, out var run) ? run : null;

    public IReadOnlyList<OrchestratorRun> List() =>
        _runs.Values.OrderByDescending(r => r.StartedAt).ToList();

    public OrchestratorRun Start(RequirementContext ctx, ApprovalMode approvalMode = ApprovalMode.Auto)
    {
        var run = new OrchestratorRun
        {
            Id = Guid.NewGuid().ToString("n"),
            Scenario = ctx.Scenario,
            Title = ctx.Title,
            Requirement = ctx.Requirement,
            ApprovalMode = approvalMode,
            StartedAt = DateTimeOffset.UtcNow,
            Nodes = SdlcGraph.Nodes.ToDictionary(n => n.Id, n => new NodeRuntime { Def = n, Status = NodeStatus.Pending })
        };
        run.Events.Add(Evt("run.started", new() { ["scenario"] = ctx.Scenario.ToString(), ["approvalMode"] = approvalMode.ToString() }));
        _runs[run.Id] = run;
        _ = Task.Run(() => ExecuteAsync(run, ctx));
        return run;
    }

    public OrchestratorRun Approve(string runId, string nodeId, bool approved, string note)
    {
        var run = Require(runId);
        if (!_approvals.TryRemove($"{runId}:{nodeId}", out var tcs))
            throw new AppException(409, "no pending approval for node");
        run.Events.Add(Evt("approval.decision", new() { ["approved"] = approved, ["note"] = note }, nodeId));
        tcs.TrySetResult(approved);
        return run;
    }

    public OrchestratorRun Stop(string runId)
    {
        var run = Require(runId);
        run.StopRequested = true;
        run.Events.Add(Evt("run.stop-requested", []));
        if (run.PendingApproval is { } pending && _approvals.TryRemove($"{runId}:{pending.NodeId}", out var tcs))
            tcs.TrySetResult(false);
        return run;
    }

    public OrchestratorRun Replan(string runId, string reason)
    {
        var run = Require(runId);
        run.ReplanCount++;
        run.Events.Add(Evt("run.replan", new() { ["reason"] = reason }));
        foreach (var node in run.Nodes.Values)
        {
            if (node.Status is NodeStatus.Pending or NodeStatus.Ready or NodeStatus.AwaitingApproval)
            {
                node.Status = NodeStatus.Pending;
                node.Result = null;
            }
        }
        run.Lineage.Add(new Decision(Guid.NewGuid().ToString("n"), DateTimeOffset.UtcNow, "requirements", "human",
            "Upstream change triggered replan", reason));
        return run;
    }

    public async Task<OrchestratorRun> WaitUntilSettled(string runId, TimeSpan? timeout = null)
    {
        var limit = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTimeOffset.UtcNow < limit)
        {
            var run = Require(runId);
            if (run.Status is "succeeded" or "failed" or "stopped")
                return run;
            await Task.Delay(20);
        }
        throw new TimeoutException("timeout waiting for run");
    }

    private OrchestratorRun Require(string id) =>
        Get(id) ?? throw new AppException(404, "run not found");

    private static AuditEvent Evt(string type, Dictionary<string, object> detail, string? nodeId = null) =>
        new(DateTimeOffset.UtcNow, type, nodeId, detail);

    private async Task ExecuteAsync(OrchestratorRun run, RequirementContext ctx)
    {
        try
        {
            var remaining = SdlcGraph.Nodes.Select(n => n.Id).ToHashSet();
            var succeeded = new HashSet<string>();

            while (remaining.Count > 0)
            {
                if (run.StopRequested)
                {
                    foreach (var id in remaining) run.Nodes[id].Status = NodeStatus.Stopped;
                    Finish(run, "stopped");
                    run.Events.Add(Evt("run.stopped", new() { ["safeStop"] = true }));
                    return;
                }

                var defs = SdlcGraph.Nodes.Where(n => remaining.Contains(n.Id)).ToList();
                var ready = SdlcGraph.Ready(succeeded, defs);
                if (ready.Count == 0)
                {
                    Finish(run, "failed");
                    run.Events.Add(Evt("run.blocked", new() { ["remaining"] = remaining.ToArray() }));
                    return;
                }

                foreach (var def in ready) run.Nodes[def.Id].Status = NodeStatus.Ready;
                run.Events.Add(Evt("wave.parallel", new()
                {
                    ["nodes"] = ready.Select(d => d.Id).ToArray(),
                    ["groups"] = ready.Select(d => d.ParallelGroup ?? d.Id).Distinct().ToArray()
                }));

                await Task.WhenAll(ready.Select(def => RunNodeAsync(run, ctx, def.Id)));

                foreach (var def in ready)
                {
                    remaining.Remove(def.Id);
                    var node = run.Nodes[def.Id];
                    if (node.Status is NodeStatus.Succeeded or NodeStatus.Skipped)
                    {
                        succeeded.Add(def.Id);
                        continue;
                    }

                    foreach (var blocked in remaining.Where(id => run.Nodes[id].Def.DependsOn.Contains(def.Id)))
                        run.Nodes[blocked].Status = NodeStatus.Blocked;
                    Finish(run, "failed");
                    return;
                }
            }

            Finish(run, "succeeded");
            run.Events.Add(Evt("run.succeeded", new() { ["replanCount"] = run.ReplanCount }));
        }
        catch (Exception ex)
        {
            run.Events.Add(Evt("run.crashed", new() { ["error"] = ex.Message }));
            Finish(run, "failed");
        }
    }

    private async Task RunNodeAsync(OrchestratorRun run, RequirementContext ctx, string nodeId)
    {
        var node = run.Nodes[nodeId];
        var def = node.Def;
        node.StartedAt = DateTimeOffset.UtcNow;

        foreach (var gate in def.EntryGates)
            run.Events.Add(Evt("gate.entry", new() { ["gate"] = gate.Id, ["check"] = gate.Check }, nodeId));

        if (def.RequiresApproval)
        {
            var allowed = await AwaitApprovalAsync(run, nodeId, def.ApprovalReason ?? "approval required");
            if (!allowed)
            {
                node.Status = run.StopRequested ? NodeStatus.Stopped : NodeStatus.Failed;
                node.Error = "approval denied or safe-stop";
                node.FinishedAt = DateTimeOffset.UtcNow;
                run.Events.Add(Evt("node.rejected", [], nodeId));
                Refresh(run);
                return;
            }
        }

        string lastError = "";
        for (var attempt = 1; attempt <= def.Retry.MaxAttempts; attempt++)
        {
            node.Attempts = attempt;
            node.Status = NodeStatus.Running;
            run.Events.Add(Evt("node.attempt", new() { ["attempt"] = attempt }, nodeId));
            try
            {
                var prior = run.Nodes.Values.Select(n => n.Result).Where(r => r is not null).Cast<StageResult>().ToList();
                var result = StageAgents.Run(nodeId, ctx, prior);
                var violations = Policies.Evaluate(result);
                if (violations.Any(v => v.Severity == "critical"))
                    throw new InvalidOperationException(string.Join("; ", violations.Select(v => v.Message)));
                if (!result.Ok)
                    throw new InvalidOperationException(result.Summary);

                node.Result = result;
                node.Snapshot = new Dictionary<string, object> { ["artifactIds"] = result.Artifacts.Select(a => a.Id).ToArray() };
                run.Artifacts.AddRange(result.Artifacts);
                run.Lineage.AddRange(result.Decisions);
                foreach (var gate in def.ExitGates)
                    run.Events.Add(Evt("gate.exit", new() { ["gate"] = gate.Id, ["check"] = gate.Check }, nodeId));
                node.Status = NodeStatus.Succeeded;
                node.FinishedAt = DateTimeOffset.UtcNow;
                run.Events.Add(Evt("node.succeeded", new() { ["attempt"] = attempt }, nodeId));
                Refresh(run);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                node.Error = lastError;
                run.Events.Add(Evt("node.failed", new() { ["attempt"] = attempt, ["error"] = lastError }, nodeId));
                if (attempt >= def.Retry.MaxAttempts) break;
                await Task.Delay(def.Retry.BackoffMs);
            }
        }

        if (def.RollbackOnFailure && node.Snapshot is not null)
        {
            node.Status = NodeStatus.RolledBack;
            run.Events.Add(Evt("node.rollback", new() { ["restored"] = true }, nodeId));
        }
        else
        {
            node.Status = NodeStatus.Failed;
        }

        node.FinishedAt = DateTimeOffset.UtcNow;
        node.Error = lastError;
        Refresh(run);
    }

    private async Task<bool> AwaitApprovalAsync(OrchestratorRun run, string nodeId, string reason)
    {
        if (run.ApprovalMode == ApprovalMode.Auto)
        {
            run.Events.Add(Evt("approval.auto", new() { ["reason"] = reason }, nodeId));
            run.Lineage.Add(new Decision(Guid.NewGuid().ToString("n"), DateTimeOffset.UtcNow, nodeId, "human",
                $"Auto-approved {nodeId}", $"{reason} (approvalMode=auto for unattended demo/tests)"));
            return true;
        }

        run.Status = "awaiting_approval";
        run.Nodes[nodeId].Status = NodeStatus.AwaitingApproval;
        run.PendingApproval = new PendingApproval { NodeId = nodeId, Reason = reason, Risk = "high-impact" };
        run.Events.Add(Evt("approval.requested", new() { ["reason"] = reason }, nodeId));
        Refresh(run);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _approvals[$"{run.Id}:{nodeId}"] = tcs;
        var approved = await tcs.Task;
        run.PendingApproval = null;
        if (run.Status == "awaiting_approval") run.Status = "running";
        run.Lineage.Add(new Decision(Guid.NewGuid().ToString("n"), DateTimeOffset.UtcNow, nodeId, "human",
            approved ? $"Approved {nodeId}" : $"Denied {nodeId}", reason));
        return approved;
    }

    private static void Finish(OrchestratorRun run, string status)
    {
        run.Status = status;
        run.FinishedAt = DateTimeOffset.UtcNow;
        Refresh(run);
    }

    private static void Refresh(OrchestratorRun run) =>
        run.Metrics = Metrics.Compute(run.Nodes, run.StartedAt, run.FinishedAt);
}
