# Execution Notes

Action log (append-only; crash-resilience requirement). Each entry: timestamp · action · result.

## Contract phase
- 2026-06-30_1328 · prompt-contract-designer · Full-tier contract created. Source scoped (18 Egress +
  CollectorGlobalDefaults). Kernel rewires (Exceptions, Telemetry) + D8 renames (3 Collector*->Adapter*)
  identified. A1–A4 VALIDATED; A-pkg/A-json/A-test OPEN (build-/scope-driven, non-blocking). ISink reshape
  DEFERRED. Contract VALID.

## Execution phase
<!-- executor appends below -->
- 2026-06-30 · S1-S3 · Copied 19 files verbatim into Emission/ (Multipart/Ndjson/Telemetry subfolders).
  Namespace substitution (Egress.*->Emission.*, Exceptions->Kernel.Exceptions, DataPipeline.Telemetry->
  Kernel.Telemetry, Glossary->Emission). D8 renames: CollectorGlobalDefaults/CollectorOutputDefaults/
  CollectorNdjsonPublisher -> Adapter*; files renamed too. Verified 0 residual Shared refs; 0 Collector
  identifiers in .cs (2 README source-path refs handled in S5). Held values unchanged.
- 2026-06-30 · S4 · Build 0 errors with NO package addition — Microsoft.Extensions.Configuration resolved
  transitively (A-pkg: no add needed). No DataPipeline.Json reference (A-json: N/A). Both OPENs closed clean.
- 2026-06-30 · S5 · Extended Emission/README.md (Carried section: engine, the single-JSON-validator
  invariant, egress invariants, Kernel rewires, D8 renames; Deferred section: ISink/static-entry/assets-
  findings reshape). Verbatim files retain source XML docs (AdapterGlobalDefaults/OutputDefaults/Formatter
  already documented at source); proportional stance per prior carries.
- 2026-06-30 · S6 · Added InternalsVisibleTo("IntegrationInfra.Emission.Tests") to main csproj (test-only,
  no runtime effect) to test the internal authoritative JSON validator. Added tests/IntegrationInfra.Emission.Tests
  (xUnit) + slnx. 15 tests: ResultsRecordFormatter (minify+single-line, trim, empty->empty, invalid JSON
  throws), AdapterGlobalDefaults egress invariants + UTF8-no-BOM, AdapterOutputDefaults.BuildPageTargetPath
  (zero-pad, slash-trim, empty-name + non-positive-page guards). One test initially used Assert.Throws (exact)
  on JsonException but actual is derived JsonReaderException -> switched to ThrowsAny. MultipartPartPlanner/
  AdapterGlobalDefaults are constants (no logic to test beyond value assertions).
- 2026-06-30 · S7 · Full build 0 errors. Tests: 33 Kernel + 20 FaultGovernance + 10 Conversation + 15
  Emission = 78 pass. Kernel untouched (empty git diff); 0 Shared.* refs in src; source unmodified.

## Residual risk / notes
- ISink seam / static-entry kill / assets-findings hardwiring = DEFERRED reshape (operator decision).
- D8 renames flagged as headline naming decision for PR review.
- Test coverage: the authoritative JSON validator + defaults + page-naming covered; the DI/streaming
  publisher engine left to integration-level (no brittle unit tests forced).

## Review phase + H1 repair
- Verifier (review/verifier-1.md): PASS (7/7; SC5 XML-docs PARTIAL, verbatim). First verifier run died on an
  API error mid-response without writing; re-run wrote the verdict. Confirmed verbatim, D8 values unchanged,
  no package invention, Kernel untouched, source unmodified, 78 tests, ISink reshape not done.
- Code-reviewer (review/code-reviewer-1.md): one HIGH (H1: DisposeAsync doesn't abort an initiated multipart
  -> orphaned S3 incomplete-multipart on cancel/throw after a part). M1/M2 = pre-existing source behavior
  (recorded, not changed). M3 = test-coverage gap.
- H1 REPAIR (operator decision D7 — "fix it here only"): added `_multipartFinalized` to both Ndjson session
  files; abort-on-dispose with CancellationToken.None for an un-finalized multipart; finalized set on
  successful complete and after catch-abort (prevents double-abort). Build 0 errors; 78 tests still pass.
  Deliberate divergence from verbatim source (source NOT changed).
- H1 regression test NOT added: exercising the abort path needs a fake IAdapterDataPublisher + ~5 MiB of
  buffered data to force a real multipart part (the <5 MiB skip is a hard S3 constraint). Per A-test (no
  brittle DI/streaming tests), deferred + noted. The fix follows the reviewer's exact prescription.
- Recorded as accepted (pre-existing verbatim, NOT changed): M1 (int cast / no upper bound on MaxBytesPerBatch),
  M2 (sub-5MiB buffering floor = doc surprise).
