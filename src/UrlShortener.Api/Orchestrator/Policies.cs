using System.Text.Json;
using System.Text.RegularExpressions;

namespace UrlShortener.Api.Orchestrator;

public static partial class Policies
{
    public static IReadOnlyList<PolicyViolation> Evaluate(StageResult result)
    {
        var violations = new List<PolicyViolation>();
        var blob = JsonSerializer.Serialize(result);
        if (SecretPattern().IsMatch(blob))
        {
            violations.Add(new PolicyViolation(
                "security.no-secrets-in-artifacts",
                "critical",
                "Artifact or decision text appears to contain secrets or credentials"));
        }

        foreach (var risk in result.Risks)
        {
            if (DangerousChange().IsMatch(risk))
            {
                violations.Add(new PolicyViolation(
                    "change-control.dangerous-action",
                    "high",
                    $"High-impact action requires extra approval: {risk}"));
            }
        }

        return violations;
    }

    [GeneratedRegex(@"(password|secret|api[_-]?key|ssn|bearer\s+[a-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"drop table|force push|disable auth", RegexOptions.IgnoreCase)]
    private static partial Regex DangerousChange();
}

public static class Metrics
{
    public static ReliabilityMetrics Compute(IDictionary<string, NodeRuntime> nodes, DateTimeOffset startedAt, DateTimeOffset? finishedAt)
    {
        var list = nodes.Values.ToList();
        var terminal = list.Count(n => n.Status is NodeStatus.Succeeded or NodeStatus.Failed or NodeStatus.RolledBack or NodeStatus.Skipped or NodeStatus.Stopped);
        var succeeded = list.Count(n => n.Status == NodeStatus.Succeeded);
        var failed = list.Count(n => n.Status is NodeStatus.Failed or NodeStatus.RolledBack);
        var retries = list.Sum(n => Math.Max(0, n.Attempts - 1));
        var rollbacks = list.Count(n => n.Status == NodeStatus.RolledBack);
        var recovery = list
            .Where(n => n.Attempts > 1 && n.StartedAt is not null && n.FinishedAt is not null)
            .Select(n => (int)(n.FinishedAt!.Value - n.StartedAt!.Value).TotalMilliseconds)
            .ToList();

        return new ReliabilityMetrics(
            terminal == 0 ? 0 : (double)succeeded / terminal,
            retries,
            rollbacks,
            recovery.Count == 0 ? null : (int)recovery.Average(),
            finishedAt is null ? null : (int)(finishedAt.Value - startedAt).TotalMilliseconds,
            succeeded,
            failed);
    }
}
