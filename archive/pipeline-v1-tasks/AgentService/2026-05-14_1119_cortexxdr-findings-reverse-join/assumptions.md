# Assumptions

- **A1 — VALIDATED** — `PaloAltoCortexApiBase.FetchEndpointsAsync` paginates `last_seen DESC` and stops at `last_seen < baseDate`; reuse with the same `iIsDryRun ? UtcNow : iBaseDate` mapping as `CollectAssetsAsync`. Source: `Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/BaseApiClients/PaloAltoNetworks/PaloAltoCortexApiBase.cs:83-190`.

- **A2 — VALIDATED** — `FetchXqlResultsAsync` passes `iQueryByTimeRangeGroup: null` so no `timeframe` clause is sent to XQL; the existing 50k-row run is unbounded and that's the contract. Source: `PaloAltoCortexApiBase.cs:226-247` and `PaloAltoCortexXqlQueryExecutor.cs:69-76`.

- **A3 — VALIDATED** — Reverse-join via `va_cves.affected_hosts[]` matches `endpoint_name` case-insensitively. Validated against the populated Host-Insights tenant in probe archive `CortexXdr_20260514_080340_616Z`; documented in `execution_notes.md` of the probe-side task dir.

- **A4 — ACCEPTED OUT-OF-SCOPE** — `CollectAssetsAsync` continues to derive `finding_ids[]` via the forward join (`va_endpoints.cves[]`). The two ID sets won't fully match the reverse-join finding ids emitted by `CollectFindingsAsync`. Operator explicitly scoped this refactor to the findings flow.

- **A5 — OPEN** — Memory ceiling for `cvesByHost` (~75 MB at the 50k cap). Acceptable per operator sign-off but unverified under production memory pressure. Flag in `execution_notes.md` for the verifier to confirm against any AgentService heap budget.

- **A6 — VALIDATED** — Tenants without the Host Insights add-on (or with permission-split API keys) return zero va_cves rows; the current readiness gate misclassifies this as "VA not ready" and short-circuits. Source: probe `execution_notes.md` lines 21-35.
