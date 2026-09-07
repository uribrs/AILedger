# Verifier 1

## Verdict

PASS WITH ONE NON-BEHAVIORAL CLEANUP. The requested TenableIo configuration wiring is correct and all behavioral/build gates pass. One test-helper XML comment still describes the batch folder as unconditional even though the helper now supports flat paths; repair is recommended before final code review.

## Findings

### V1 — Low — Conditional path helper has stale unconditional wording

- Location: `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.TenableIoCollector.Test/TenableIoCorrelatedFlowTests.cs:853-857`
- The helper now defaults `batchScopedStorage` to `false` and omits the batch segment, but its XML text still says each object lands in "the emitter's batch folder for that page."
- Impact: documentation only; runtime behavior and tests are correct.
- Repair: describe the batch folder as conditional when batch-scoped storage is enabled.

No behavioral, build, recovery, storage-layout, or request-coverage defects found.

## Original Request Coverage

The user asked to move TenableIo's batch boolean to configuration like the other collectors. The implementation does exactly that: the flag is owned by `TenableIoCollectorConfiguration`, defaults to `false`, is forwarded by the correlated flow, and reaches the existing emitter through the publisher. The previously existing InsightVM Cloud and Qualys `true` to `false` flips remain unchanged and are not treated as implementation work for this refinement.

## Success Criteria

| Criterion | Result | Evidence |
|---|---|---|
| TenableIo configuration exposes `BatchScopedStorage` default `false` | PASS | `TenableIoCollectorConfiguration.cs:89`; builder assertion at `TenableIoCollectorConfigurationBuilderTests.cs:21` |
| Correlated emitter receives configured value, not a literal | PASS | `_config.BatchScopedStorage` forwarded at `TenableIoCorrelatedFindingsFlow.cs:85-90`; parameter passed to `NdjsonBatchEmitter.Create` at `TenableIoCorrelatedBatchPublisher.cs:41-55`; production scan found no emitter boolean literal |
| Tests prove default and propagation | PASS | Default assertion plus configured-`true` scoped path/storage URL assertions at `TenableIoCorrelatedFlowTests.cs:117-145`; focused run 3/3 |
| Default `false` produces flat paths | PASS | Default-config collector assertions use `tenant/run/findings_*.json` at `TenableIoCollectorTests.cs:341-342,521-525`; default flow/retry assertions at `TenableIoCorrelatedFlowTests.cs:793-824`; full suite passed |
| TenableIo test project passes | PASS | `dotnet test ...TenableIoCollector.Test.csproj --no-restore`: 153 passed, 0 failed, 0 skipped |
| Solution builds | PASS | `dotnet build .../Cymulate.Integration.Adapters.sln --no-restore -m:1`: 0 warnings, 0 errors |
| Independent verifier and code reviewer have no unresolved issue | PARTIAL | Verifier complete with V1 documentation cleanup; code reviewer is a downstream gate and has not run yet |

## Scope and Semantic Guardrails

- Base-relative production changes are limited to the TenableIo configuration-to-flow-to-publisher seam plus conditional wording. No checkpoint, retry, resume, object-store, emitter implementation, or progress-context logic changed.
- Test expectation changes only reflect the already-requested default flip: flat paths by default and scoped paths when explicitly configured `true`.
- The base-relative InsightVM Cloud and Qualys changes are each exactly one pre-existing initializer flip (`true` to `false`) and were not modified as part of this Tenable refinement.
- `git diff --check`: PASS.

## Assumption Dispositions

| ID | Terminal disposition | Evidence | Actor |
|---|---|---|---|
| A1 | VALIDATED | Configuration record at `TenableIoCollectorConfiguration.cs:89`; construction seam at `TenableIoCorrelatedFindingsFlow.cs:85-90`; publisher seam at `TenableIoCorrelatedBatchPublisher.cs:41-55`; focused tests 3/3 | verifier |
| A2 | VALIDATED | Existing builder produces default-false configuration (`TenableIoCollectorConfigurationBuilderTests.cs:10-23`); record `with` override reaches scoped output (`TenableIoCorrelatedFlowTests.cs:117-145`) without mapper changes | verifier |
| A3 | VALIDATED | Default-path collector and flow tests are flat; resume/retry tests remain green; full Tenable suite 153/153 and solution build 0 warnings/errors | verifier |

## Decision Drift

| Decision | Drift | Evidence |
|---|---|---|
| Keep change within TenableIo source/tests | None | Tenable task changes are confined to the planned three production and three test files; the two other collector flips predate and are preserved |
| Mirror configuration-property shape with default `false` | None | `TenableIoCollectorConfiguration.cs:89` |
| Preserve flat behavior by passing configuration | None | Default flat-path assertions and explicit-true scoped assertions both pass |
| Avoid external/vendor dependency | None | In-memory publish capture proves propagation; no vendor connectivity used or required |
| Run independent verification and isolated code review | Pending downstream only | Verifier complete; code reviewer has not yet run |

## Verification Commands

- Focused default/configured-scoped tests: 3 passed.
- Full TenableIo test project: 153 passed.
- Single-node solution build: succeeded, 0 warnings, 0 errors.
- Tenable production literal/configuration scan: configuration default and forwarding found; no emitter literal remains.
- `git diff --check`: passed.

## Residual Risks

- No vendor-backed run was performed. Risk is low because this is local configuration/constructor wiring and the publish capture asserts both target path and emitted storage URL.
- Runtime dictionary/platform metadata cannot toggle this property. That is intentional under the contract: the request was to move ownership into collector configuration like the comparison collectors, not add a new externally bound setting.
