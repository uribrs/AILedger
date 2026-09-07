# Verifier Report — validate-and-dryrun (scoped to connection-validation; dry-run deferred)

Date: 2026-06-25
Scope note honored: VALIDATE half judged against the contract; dry-run deferral judged for soundness/honesty, not as an incomplete failure.

## Build / Test (re-run)
- `dotnet build CollectorBase.slnx` → **0 Error(s)**.
- `dotnet test CollectorBase.slnx` → **Passed! Failed: 0, Passed: 61, Skipped: 0, Total: 61**.
- Matches execution_notes (58 prior + 3 new). No regressions observed in the suite (passthrough/poll-and-drain/conformance/pagination tests all included in the 61 green).

## Per-criterion verdicts

### 1. Stub gone — `ValidateConfigurationAsync` no longer unconditional Success — PASS
- `CollectorExecutorAdapter.cs:108-140` is a real probe. It can return Failure on three independent paths: NO_DEFINITION (`:111`), INVALID_PROFILE (`:122`), and the probe result mapping (`:139`, `Failure(...)` when `result.Status != Success`).
- The old `:100` "Success unconditionally" body is gone. Confirmed it is NOT tautological — see #3.

### 2. Fail-closed — PASS
- No yaml / blank yaml → `Failure(..., "NO_DEFINITION")` before any HTTP (`:110-112`). Proven by `Validate_NoYaml_FailsClosed` (`CollectorExecutorTests.cs:1725-1736`) asserting `IsValid == false` and `ErrorCode == "NO_DEFINITION"`.
- Invalid yaml → `Failure(..., "INVALID_PROFILE")` (`:120-123`) via `ProfileLoader.Load` up front. (No dedicated test for the INVALID_PROFILE branch, but it is straightforward and the catch is scoped to `InvalidOperationException`/`YamlException`. Minor — see findings.)

### 3. Probe actually authenticates + hits the vendor (not a Prepare shortcut) — PASS
- Trace `CollectorExecutorRunner.ProbeAsync` (`:60-96`): `Prepare` (`:62`) only builds inputs/profile/session-spec — it does NOT call the vendor. The probe then **builds a real session** via `DefaultSessionProvider.Create(sessionSpec, ...)` (`:71-72`) and **issues a real request** `http.SendForStringAsync(...)` (`:84`) against the first step. A 2xx ⇒ Success (`:85`); `AdapterHttpRequestFailedException`/circuit ⇒ classified Failure with 401/403 → `UNAUTHORIZED`, else `PROBE_FAILED` (`:89-94`).
- Round-trip is real, not tautological: `FalconMock` checks the `X-Api-Key` header against `secret-123` (`CollectorExecutorTests.cs:128-131`) → 200 for the right key, 401 otherwise. `Validate_GoodCreds` (good key) asserts `IsValid`; `Validate_BadCreds` (wrong key) asserts `IsValid == false` AND `ErrorCode == "UNAUTHORIZED"` — i.e., the test drives the real Shared session + transport classifier to a genuine 401. (`:1692-1722`).

### 4. No publish / no checkpoint on the probe — PASS
- `ProbeAsync` (`:60-96`) never constructs a `RunContext`, never touches `progress`, never calls any egress/publish or checkpoint code. It returns immediately after the single request. By construction it is non-collecting. Contrast with `RunAsync` (`:125+`) which builds `RunContext`/progress and runs the full loop.

### 5. Synthesized PlatformEvent carries creds + base_url; missing base_url fails gracefully — PASS
- `CollectorExecutorAdapter.cs:127-134` builds the event with `Credentials = new Dictionary(_lastConfiguration)` (creds-by-convention) and `Payload = { yaml, config = _lastConfiguration }`. The input builder reads `config.base_url` (`CollectorExecutorRunInputBuilder.cs:24,33`) and merges config→credentials (`:26-28`), so `api_key`/`base_url` reach the session factory.
- Missing base_url path is graceful: `CollectorExecutorRunPreparer.cs:64-65` returns `ValidationFailure("config.base_url is required.", "INVALID_PAYLOAD")` — a Failure, not a throw. `ProbeAsync:62-63` returns that failure, which maps to `ConfigurationValidationResult.Failure`. No unhandled exception path.

### 6. Legacy fidelity (auth + one capped request + 2xx, no publish) — PASS with a flagged divergence
- Intent matches the legacy `FalconCollector.TryCheckConnectionAsync` primitive (auth + one `limit:1` request + 2xx, no publish). The probe caps `page_size:1` via `BuildContext(..., pageSize:1)` (`:78`).
- Divergence (honestly flagged in execution_notes:52): the probe issues the **first step's** request, which for export-job vendors (e.g. Tenable) is a **POST that creates a throwaway export job**, not a read-only GET. This is a side-effecting probe for those vendors. Judged **acceptable for a connection test** (it proves auth + endpoint reachability and is bounded), and it is disclosed rather than hidden. Not a blocker. See findings for the residual risk.

### 7. Dry-run deferral — PASS (sound + honestly recorded)
- Stated reason (execution_notes:32-40): the Bus cannot signal a dry-run to a bus-entrypoint collector — `PlatformEvent` has no dry-run field; `isDryRun` exists only as a parameter on the capability methods (`IAssetsCollectorAdapter.CollectAssetsAsync(..., bool isDryRun, ...)`), which this adapter routes around (those methods throw `NotSupportedException`, `CollectorExecutorAdapter.cs:169-173`). The bus delegates carry only `{Topic,VendorName}` (`:198`), so there is no channel to carry dryRun. Technically sound and consistent with the code.
- Honesty: `state.json` marks DRYRUN-WIRE and DRYRUN-CAP `deferred` (not complete) with the gap noted (`state.json:29-37`). Nothing falsely claimed done. The `ProbeAsync` primitive is genuinely in place and reusable when the Bus can signal dry-run.
- Note: the bus-entrypoint `ValidateConfigurationAsync` delegate (`:203`) remains a Success stub. This is intentional and defensible (it is the collection-path placeholder, not the setup-time validate entry; the platform's validate calls the adapter method). It diverges from constraints.md line 4 ("wire validation on BOTH ... the bus-entrypoint delegate :165"), but execution_notes:28-30 explains why probing on every collection dispatch is wrong. Acceptable given the mid-flight scope-down; flagged below.

### 8. No regression — PASS
- Full suite green (61/61). Passthrough/poll-and-drain/conformance/pagination tests are part of the suite and pass. The validate changes are additive (new method body + new `ProbeAsync`); `RunAsync`, resume, and the bus wiring are untouched.

## Findings by severity

### Must-fix
- None. The validate deliverable meets every in-scope success criterion; the deferral is sound and honest.

### Should-consider (non-blocking)
1. **Side-effecting probe for export-job vendors** (Med-Low). `ProbeAsync` probes `p.Steps[0]` regardless of method; for POST job-create first steps this creates a throwaway vendor-side job on every connection test. Disclosed in notes. If/when a Tenable-style profile is validated in production, consider probing the first GET/read step or a declared lightweight `probe`/`health` endpoint instead of blindly the first step. No code change required now.
2. **Bus-delegate validate left as Success stub** (Low). Diverges from the letter of constraints.md (wire both). The rationale (don't probe on every collection) is sound, but the constraint was not formally amended in constraints.md/decisions.md — only in execution_notes. Recommend a one-line note in decisions.md so the divergence is recorded at contract level, not just in execution prose.
3. **INVALID_PROFILE branch untested** (Low). The fail-closed-on-invalid-yaml path (`:120-122`) has no dedicated test. Cheap to add; the NO_DEFINITION and bad-cred paths are covered.

## Verdict
- **Validate deliverable: ACCEPT.** The stub is removed; `ValidateConfigurationAsync` performs a real authenticate-and-probe round-trip, fails closed on no/invalid yaml, does not publish/checkpoint, handles missing base_url gracefully, and is proven by non-tautological in-process-harness tests (good→200 valid, bad→401 UNAUTHORIZED). Build clean, 61/61 green, no regression.
- **Dry-run deferral: ACCEPT.** The Bus-dispatch gap is technically correct, the legacy semantics are pinned, the reusable `ProbeAsync` primitive is in place, and state.json honestly records the steps as deferred rather than complete.
