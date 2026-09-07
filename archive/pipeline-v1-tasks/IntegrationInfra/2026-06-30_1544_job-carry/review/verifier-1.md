# Verifier-1 Verdict — Job carry

Independent verification against the 7 Success Criteria in prompt_contract.md. Spot-checked all 11
relocated files byte-by-byte against source; re-ran build + full test suite myself.

## Verdict table

| # | Criterion | Verdict |
|---|-----------|---------|
| 1 | 10 files relocated, behavior verbatim; AdapterRunMetadata in Envelopes.Common | PASS |
| 2 | D8 renames consistent (8); JsonPropertyName values unchanged; wire-safe; CollectorHandshake value intact | PASS |
| 3 | Rewires done; no residual Shared.*; outbound not carried | PASS |
| 4 | Build clean; Kernel+Emission unmodified; 109 tests pass | PASS |
| 5 | README extended; XML docs on EVERY public member | PARTIAL |
| 6 | Tests genuine (hydrator characterization + parser valid/invalid) | PASS |
| 7 | Source repo not mutated | PASS |

**Overall: PASS (with one PARTIAL on the XML-doc clause of criterion 5).**

## Evidence

**C1 — PASS.** All 11 files present under Job/ (flat ns `Cymulate.IntegrationInfra.Job`) + `AdapterRunMetadata`
in `Envelopes/Common/`. Byte-by-byte diff of each file vs source: only namespaces, type-name renames (D8),
and reference rewires differ — logic identical.
- Hydrator (`RunPayloadCredentialHydrator.cs`): logic byte-identical including exception-swallowing
  `catch { }` block (lines 79-82), untyped `Dictionary<string,string>` output, `_encryptedCredentials`
  branch, MetadataKeysToSkip skip. Only change: `CollectorRunEnvelopeParser`→`AdapterRunEnvelopeParser` call.
  NOT reshaped — no parse→typed-job change.
- Both parsers + AdapterTriggerParsing + AdapterTimeDefaults + AdapterTopics + 4 Run models: verbatim.

**C2 — PASS.** 8 D8 renames present and consistent across types/files/refs (AdapterRunAction,
AdapterRunActionParser, AdapterRunEnvelope, AdapterRunEnvelopeParser, AdapterRunStartEnvelope,
AdapterRunStartPayload, AdapterRunMetadata, AdapterTriggerParsing). All `[JsonPropertyName(...)]` values
identical to source (verified field-by-field on every model). No `$type`/`JsonDerivedType`/`JsonPolymorphic`
anywhere in Job (grep clean). `AdapterTopics.Indicator.CollectorHandshake` value `"collector.handshake"`
unchanged — correctly NOT renamed (routing family value). `AdapterTopics.Collector.*` nested class +
"CollectAssets"/"CollectFindings" values unchanged.

**C3 — PASS.** `...Models.Common`→`Envelopes.Common` rewire present (AdapterRunEnvelope/Payload/Metadata).
`AdapterTimeDefaults` uses `Kernel.Time.DateTimeUtc` + `Emission.AdapterGlobalDefaults.DefaultLookbackDays`.
`AdapterTriggerParsing` reads `Emission.AdapterGlobalDefaults` (AssetsFileName/FindingsFileName) — the flagged
Job→Emission coupling, spanning both files as execution_notes records. ZERO residual
`Cymulate.Integration.Adapters.Shared.*` refs in src/ (grep clean). No outbound/Reporting items
(CollectorEnvelopeBuilder/CollectorStatus/converter/EventMetadata/Done/Progress) in Job (grep clean).

**C4 — PASS.** `dotnet build IntegrationInfra.slnx`: Build succeeded, 0 Errors (12 NU1507 warnings —
pre-existing package-source config, not introduced by this carry). `git diff HEAD` on
src/IntegrationInfra/Kernel AND src/IntegrationInfra/Emission: empty (untouched). `DefaultLookbackDays = 365`
still in Emission/AdapterGlobalDefaults.cs — NOT moved. `dotnet test IntegrationInfra.slnx`:
33 Kernel + 20 FaultGovernance + 10 Conversation + 15 Emission + 31 Job = **109 passed, 0 failed**. Matches
contract expectation exactly.

**C5 — PARTIAL.** README.md extended correctly and completely: carried set, D8 renames (incl. the
CollectorHandshake-value-left-alone note), Envelopes.Common addition, flagged Job→Emission lookback coupling,
deferred hydrator parse→typed-job seam — all present. HOWEVER the "XML docs on every relocated/new public
member" clause is NOT met: several public members carry no XML doc (AdapterTriggerParsing's 5 public methods;
AdapterRunActionParser.TryParse; AdapterRunEnvelopeParser class + ParseOrThrow; the record types
AdapterRunEnvelope/AdapterRunMetadata + many properties). This is a *direct consequence of the verbatim
mandate* — the source lacked these docs and the executor preserved source exactly rather than authoring new
prose. The two contract requirements (verbatim vs. full XML-doc coverage) are in tension; the executor chose
verbatim. Not a behavior defect; a documentation-completeness gap. Hence PARTIAL, not FAIL.

**C6 — PASS.** Tests are genuine, not vacuous. Hydrator characterization pins all three required behaviors:
plain-JSON expand into config (accessKey/secretKey), encrypted-blob → `_encryptedCredentials` (and asserts
accessKey absent), bad-payload no-throw + config untouched. Plus endpoint-from-extension-data and
"encrypted" metadata-key skip. Parser tests cover valid + invalid/empty/whitespace/malformed/missing-metadata.
TriggerParsing + TimeDefaults covered with real assertions.

**C7 — PASS.** Source repo `/Users/user/Dev/Uri/localprojects/IntegrationsInfra` is not even a git repo and
source file mtimes are pre-task (May 14 / Apr 26). Unmutated.

## Must-fix items
None blocking. One optional follow-up:
- **(minor, C5)** If full XML-doc coverage is a hard gate, author docs on the undocumented public members
  noted above. This deviates from the verbatim mandate, so it is a deliberate-choice call for the operator,
  not a defect to silently fix.

## Confirmations requested by the verifier brief
- Hydrator NOT reshaped: confirmed (verbatim, exception-swallowing intact).
- DefaultLookbackDays NOT moved out of Emission: confirmed.
- Kernel + Emission git diff empty: confirmed.
- Wire-safety (no type-name-on-wire): confirmed.
