# Agentic URL Shortener (ASP.NET Core)

Interview prototype for **Agentic-Proficient Software Engineer**: a working URL shortener plus an orchestration layer that runs the SDLC (requirements → architecture → implementation → tests → docs → release) with a **dependency graph, gates, human approvals, retries, rollback, and audit lineage**.

## Setup

Prerequisites: [.NET 8 SDK](https://dotnet.microsoft.com/download) (Visual Studio 2022 17.8+).

```bash
dotnet test
dotnet run --project src/UrlShortener.Api
```

Open [http://localhost:5169](http://localhost:5169).

## Shortener APIs

| Method | Path | Purpose |
| --- | --- | --- |
| `POST` | `/api/urls` | Create `{ url, customAlias?, expiresInSeconds? }` |
| `GET` | `/{code}` | 302 redirect; records a click |
| `GET` | `/api/urls` | List |
| `GET` | `/api/urls/{code}` | Metadata |
| `GET` | `/api/urls/{code}/analytics` | Clicks by day + referrers |
| `POST` | `/api/urls/{code}/deactivate` | Fail-closed disable (410) |
| `GET` | `/health`, `/metrics` | Liveness and counters |

Unsafe schemes (`javascript:`, `data:`, …) are rejected. Writes are rate-limited.

## Orchestrator APIs

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/api/orchestrator/graph` | DAG + topological layers |
| `POST` | `/api/orchestrator/runs` | `{ scenario, approvalMode }` — `greenfield` \| `brownfield` \| `ambiguous`; `auto` \| `manual` |
| `GET` | `/api/orchestrator/runs/{id}` | Status, lineage, artifacts, metrics |
| `POST` | `/api/orchestrator/runs/{id}/approve` | Human gate |
| `POST` | `/api/orchestrator/runs/{id}/stop` | Safe-stop |
| `POST` | `/api/orchestrator/runs/{id}/replan` | Re-plan after upstream change |

## Three scenarios

1. **Greenfield** — new shortener MVP, full SDLC trace.
2. **Brownfield** — impact map for deactivate + keep analytics; architecture/release still gated.
3. **Ambiguous** — “like bit.ly” is normalized into an MVP with **explicit assumptions** (no SSO, JSON store, rate limits).

## Docs

- [Architecture, API usage, and problem mapping (HTML)](docs/PROJECT-GUIDE.html) — open in a browser
- [Architecture](docs/ARCHITECTURE.md)
- [Engineering summary](docs/ENGINEERING_SUMMARY.md)
