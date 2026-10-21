# Architecture

## Two planes in one ASP.NET Core host

```
┌─────────────────────────────────────────────────────────┐
│                      Dashboard (wwwroot)                │
│              human oversight / approvals / demo         │
└───────────────┬─────────────────────────┬───────────────┘
                │                         │
        Data plane                  Control plane
        URL shortener               SDLC orchestrator
                │                         │
     UrlShortenerService          OrchestratorEngine
     JsonUrlStore                 DAG + gates + lineage
```

Keeping both in **one process** is a prototype constraint (easy `dotnet run`). Modules stay separated so a later split into two services is mechanical.

## URL shortener

- **Endpoints** — Minimal APIs in `ShortenerEndpoints`.
- **Domain** — `UrlShortenerService` owns allocate/redirect/expire/deactivate/analytics.
- **Persistence** — `JsonUrlStore` with atomic temp-file replace (no SQL native deps).
- **Safety** — scheme allow-list, reserved codes, collision retry cap (5), token-bucket rate limit, security headers.

Redirect is the hot path: load record → fail closed if disabled/expired → increment clicks → append `ClickEvent` → 302.

## Orchestration model (the differentiator)

The graph is **explicit**, not a linear prompt chain:

1. `requirements` (entry: nonempty requirement)
2. `architecture` (**human approval**, change control)
3. **parallel** `implementation` ∥ `test_planning` (group `build`)
4. **sync join** → `testing` (needs both) and `documentation` (needs implementation) — group `verify`
5. `release` (**human approval**, quality gate)

Each node has **entry/exit gates**, bounded **retries + backoff**, optional **rollback** of stage snapshots, and an **audit event stream**.

**Controlled autonomy:** agents run stages; humans own architecture and release. `approvalMode=auto` is only for unattended tests/demos and is itself recorded in lineage.

**Observability:** every run stores decisions (lineage), artifacts, gate events, and reliability metrics (success rate, retries, rollbacks, MTTR, e2e latency).

**Replan / safe-stop:** `POST .../replan` invalidates unfinished downstream work; `POST .../stop` sets a cooperative stop flag and fails a pending approval so the engine does not hang.

Stage “agents” are **deterministic workers** with a single `Run(node, context, prior)` interface. An LLM can be swapped behind that interface later; the governance graph does not depend on a vendor API key.

## Key decisions

| Decision | Why |
| --- | --- |
| ASP.NET Core 8 Minimal APIs | Matches a typical Schwab web stack and Visual Studio 2022 without a .NET 9 SDK |
| JSON file store | Zero-infra interview laptop; trade-off vs HA SQL |
| Deterministic agents | Runnable without secrets; governance is the product |
| Human gates on architecture + release | Assignment principle: humans own oversight and final quality |
| In-process rate limit | Good enough for a node; not cluster-safe |
