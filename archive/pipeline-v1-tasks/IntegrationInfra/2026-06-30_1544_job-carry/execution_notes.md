# Execution Notes

Action log (append-only; crash-resilience requirement). Each entry: timestamp · action · result.

## Contract phase
- 2026-06-30_1544 · prompt-contract-designer · Full-tier contract created. Source scoped (10 Job files + 1
  Common shape). Edges: Common→Envelopes.Common (AdapterRunMetadata), Job→Emission (lookback constant,
  flagged), D8 renames (8 names, wire-safety to verify). Hydrator verbatim (parse→typed-job seam deferred).
  A1–A5 VALIDATED; A-wire/A-test OPEN (build-/scope-driven). Contract VALID.

## Execution phase
<!-- executor appends below -->
- 2026-06-30 · S1 · Wire-safety: no $type/JsonDerivedType/JsonPolymorphic in Run models/parsers → D8 rename
  wire-safe (only JsonPropertyName values on the wire). A-wire VALIDATED.
- 2026-06-30 · S2-S4 · Copied 10 Job files → Job/ + CollectorRunMetadata → Envelopes/Common/. Flattened all
  source namespaces → Cymulate.IntegrationInfra.Job; Common→Envelopes.Common. D8 renames (8 names, longest-
  first to avoid prefix collisions) applied + files renamed. AdapterTopics.CollectorHandshake (a routing
  topic constant; value "collector.handshake") left as-is — family routing value, not a D8 identity rename.
- 2026-06-30 · S5 · Build surfaced 2 missed rewires (good): AdapterTimeDefaults uses DateTimeUtc (Kernel.Time)
  and AdapterTriggerParsing reads AdapterGlobalDefaults (Emission). Added `using Kernel.Time` + `using Emission`.
  Both AdapterTimeDefaults AND AdapterTriggerParsing read the Emission defaults → the flagged Job→Emission
  coupling spans both. Build 0 errors. Kernel + Emission git-diff CLEAN (untouched).
- 2026-06-30 · S6 · Extended Job/README.md (Carried + cross-concern-edges sections; D8 renames; the
  AdapterRunMetadata→Envelopes.Common addition; the flagged Job→Emission lookback coupling; the deferred
  hydrator parse→typed-job seam).
- 2026-06-30 · S7 · Added tests/IntegrationInfra.Job.Tests (xUnit) + slnx. 31 tests: AdapterTriggerParsing
  (flow-name mapping, GetAny/GetAnyNullable, date + bool parsing variants), AdapterTimeDefaults (MinValue→
  now-365d, provided-date, lookback const), AdapterRunEnvelopeParser.TryParseStartEnvelope (valid + invalid/
  missing-metadata), RunPayloadCredentialHydrator CHARACTERIZATION (plain-JSON expand, encrypted-blob→
  _encryptedCredentials, endpoint-from-extension-data, "encrypted" key skipped, bad-payload no-throw).
- 2026-06-30 · S8 · Full build 0 errors. Tests: 33 Kernel + 20 FaultGovernance + 10 Conversation + 15
  Emission + 31 Job = 109 pass. No residual Shared.* refs; Kernel + Emission untouched; source unmodified.

## Residual risk / notes
- FLAGGED Job→Emission coupling (AdapterTimeDefaults + AdapterTriggerParsing read Emission.AdapterGlobalDefaults
  for lookback days + assets/findings file names). DefaultLookbackDays arguably mis-filed in Emission →
  reshape candidate (relocate to a Job/time or shared home). Carried verbatim; constant not moved.
- Hydrator parse→typed-job seam = deferred reshape.
- AdapterRunMetadata parked in Envelopes.Common (interim, like AdapterError) — final home in the Events carve.

## Review phase
- Verifier (review/verifier-1.md): PASS (6/7; C5 XML-docs PARTIAL — verbatim source gaps, no behavior
  defect). 11 files byte-verbatim incl. hydrator; D8 renames clean (JsonPropertyName values unchanged);
  Kernel + Emission untouched; 109 tests pass; source unmutated. No must-fix.
- Code-reviewer (review/code-reviewer-1.md): no blockers/majors. Credential handling sound (no secret
  exposure). Minors M1-M4 + nits = pre-existing verbatim source behavior -> recorded, NOT changed.
- Test follow-up (tracked, non-blocking): ParseOrThrow (needs SDK PlatformEvent) + ValidateOrThrow +
  AdapterRunActionParser untested; reviewer: prioritize before the parse->typed-job reshape.
- No repairs needed — disposition is record-not-change (verbatim source) + tracked follow-ups.
