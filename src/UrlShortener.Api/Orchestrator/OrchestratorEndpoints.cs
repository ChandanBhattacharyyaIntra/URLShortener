using UrlShortener.Api.Shortener;

namespace UrlShortener.Api.Orchestrator;

public static class OrchestratorEndpoints
{
    public static void MapOrchestrator(this WebApplication app)
    {
        app.MapGet("/api/orchestrator/graph", () => Results.Json(new
        {
            nodes = SdlcGraph.Nodes.Select(n => new
            {
                n.Id,
                stage = n.Stage.ToString(),
                n.DependsOn,
                n.RequiresApproval,
                n.ParallelGroup,
                n.EntryGates,
                n.ExitGates,
                n.Retry
            }),
            layers = SdlcGraph.Layers()
        }));

        app.MapGet("/api/orchestrator/runs", (OrchestratorEngine engine) => Results.Json(new { items = engine.List() }));

        app.MapGet("/api/orchestrator/runs/{id}", (string id, OrchestratorEngine engine) =>
        {
            var run = engine.Get(id);
            return run is null ? Results.NotFound(new { error = "run not found" }) : Results.Json(run);
        });

        app.MapPost("/api/orchestrator/runs", async (StartRunRequest body, OrchestratorEngine engine) =>
        {
            if (!ScenarioCatalog.All.TryGetValue(body.Scenario ?? "greenfield", out var ctx))
                throw new AppException(400, "unknown scenario");

            var mode = string.Equals(body.ApprovalMode, "manual", StringComparison.OrdinalIgnoreCase)
                ? ApprovalMode.Manual
                : ApprovalMode.Auto;
            var run = engine.Start(ctx, mode);
            if (mode == ApprovalMode.Auto)
            {
                var settled = await engine.WaitUntilSettled(run.Id);
                return Results.Json(settled, statusCode: 201);
            }

            return Results.Json(engine.Get(run.Id), statusCode: 202);
        });

        app.MapPost("/api/orchestrator/runs/{id}/approve", (string id, ApprovalRequest body, OrchestratorEngine engine) =>
        {
            var run = engine.Get(id) ?? throw new AppException(404, "run not found");
            if (run.PendingApproval is null) throw new AppException(409, "no pending approval");
            engine.Approve(id, run.PendingApproval.NodeId, body.Approved ?? true, body.Note ?? "");
            return Results.Json(engine.Get(id));
        });

        app.MapPost("/api/orchestrator/runs/{id}/stop", (string id, OrchestratorEngine engine) => Results.Json(engine.Stop(id)));
        app.MapPost("/api/orchestrator/runs/{id}/replan", (string id, ReplanRequest body, OrchestratorEngine engine) =>
            Results.Json(engine.Replan(id, body.Reason ?? "upstream output changed")));
    }
}

public sealed record StartRunRequest(string? Scenario, string? ApprovalMode);
public sealed record ApprovalRequest(bool? Approved, string? Note);
public sealed record ReplanRequest(string? Reason);
