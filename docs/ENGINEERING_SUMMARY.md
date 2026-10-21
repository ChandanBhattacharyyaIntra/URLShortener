# Engineering summary

## Plan and rationale

The PDF asks for two things at once: a **real URL shortener** and an **agentic SDLC orchestrator** that is more than a task list. The shortener proves engineering output (APIs, analytics, reliability). The orchestrator proves decomposition, non-linear execution, governance, and validation.

Work sequence:

1. Normalize the assignment into a bounded MVP (no SSO, no multi-region).
2. Implement the data plane with fail-closed validation.
3. Implement the control plane as a DAG engine with approvals and metrics.
4. Encode three scenarios as first-class requirement contexts.
5. Prove both planes with `dotnet test`.

## Artifacts

- Runnable API + dashboard (`src/UrlShortener.Api`)
- xUnit coverage for shortener + all three orchestrator scenarios
- This summary, architecture note, and README setup
- Per-run artifacts emitted by stage agents (spec, ADR, evidence, test plan, checklist)

## Risks, trade-offs, validation

| Risk | Mitigation |
| --- | --- |
| File store is not HA / multi-writer safe | Documented limit; atomic replace; tests use isolated temp files |
| Rate limiter is in-memory | Fine for one node; would move to Redis/gateway in production |
| Deterministic agents do not call an LLM | Governance is testable; LLM is a plug-in, not the control plane |
| Dashboard has no dedicated UI tests | API tests cover behavior; UI is a thin client |
| Redirect + analytics race | Acceptable for prototype; would use a single transactional write |

Validation: unsafe URL rejection, alias conflict, deactivate → 410, click aggregation, DAG parallel waves, gate events, auto-approval lineage, success metrics.

## Assumptions

- Single-node local demo is the “release.”
- Unauthenticated MVP with rate limits.
- Analytics retained in the JSON store with no TTL.
- Auto-approve is allowed only when explicitly requested (tests/demo).

## Limitations

- No SSO, audit log shipping, or blue/green deploy.
- No clustered rate limits or SQL backing store.
- Replan does not automatically restart a finished run (it records intent and invalidates unfinished nodes).
- Ambiguous scenario **does not invent hidden requirements**; it records assumptions and proceeds only inside that envelope.
