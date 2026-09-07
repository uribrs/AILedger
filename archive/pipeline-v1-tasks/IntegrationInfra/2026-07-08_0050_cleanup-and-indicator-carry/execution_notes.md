# Execution Notes

Branch: `chore/dedup-rename-indicator-carry` (off `main` @ f0d323d).

## S1 — Branch
Created; repo was clean.

## S2 — Fix 1: trigger-parsing dedup
- Re-verified logic identity pre-delete: normalized diff (names + docs + using/namespace stripped) → byte-identical.
- Re-verified usage: zero src call sites; only the Conducting test class + Conducting README referenced it.
- Test-parity diff: Job.Tests lacked `TryParseBool("no"/"y")` rows and a null input for `TryParseUtcDateTime` — ported both into `AdapterTriggerParsingTests.cs`.
- Deleted `src/.../Conducting/Collectors/Triggers/CollectorTriggerParsing.cs` (git rm) and the `CollectorTriggerParsingTests` class from `tests/IntegrationInfra.Conducting.Tests/PureUnitTests.cs` (+ removed now-orphaned usings: Triggers, Emission, Job).
- README: dropped from supporting surface; removed Emission from the Conducting DAG (zero remaining Conducting→Emission code references); rewrote the tracked-follow-up note (Job→Emission constant coupling remains deferred).

## S3 — Fix 2: Legacy renames
- `AdapterBusLegacyFlowExecutor` → `AdapterBusClassifiedFlowExecutor` (file + call site in `AdapterBusFlowExecutor.cs`).
- `CollectorResumeLegacyExecutor` → `CollectorResumeClassifiedExecutor` (file + call site in `CollectorResumeRunner.cs`).
- Bonus identifier found by sweep: local var `legacySnippet` in `AdapterHttpClient.cs` → `streamedSnippet`.
- Remaining "legacy" occurrences are comments only (legacy `{metadata}` wire shape, legacy `IHttpSession` path, legacy/proxied servers) — permitted by contract.

## S4 — Fix 3: indicator-mechanism carry (7 files)
- `Contracts/ITopicHandler.cs` (ns `...Contracts`), `Conducting/Indicators/BaseFlowHandler.cs` (ns `...Conducting.Indicators`, + using Contracts), `Kernel/Indicators/{IocTypeDetector,PrivateIpDetector}.cs` (ns `...Kernel.Indicators`), `Envelopes/Indicators/{IocUploadRequest,IoaUploadRequest,IoaRuleRequestConverter}.cs` (ns `...Envelopes.Indicators`; converter/model usings collapse — same namespace).
- Verbatim bodies; only namespace/using rewires. Post-carry identity check: all 7 IDENTICAL modulo using/namespace lines.
- New concern edge: Conducting → Contracts (BaseFlowHandler implements ITopicHandler). Documented in Conducting README ("Indicator surface" section replaced the stale "Out of scope" note).
- `IndicatorNames` NOT carried (per decision).

## S5 — Tests
- `Kernel.Tests/Indicators/IocTypeDetectorTests.cs`, `Kernel.Tests/Indicators/PrivateIpDetectorTests.cs` (mirrors Kernel.Tests subfolder convention).
- `Job.Tests/IoaRuleRequestConverterTests.cs` (inbound-payload parsing home, beside RunEnvelopeAndHydratorTests): canonical + alias field names, severity map (Low25/Med50/High75/Critical|VeryHigh100/unknown50), actionType map (Block30/else20), commandLine synthesis, fieldValues precedence, defaults.

## S6 — Build + tests
- `dotnet build IntegrationInfra.slnx`: 0 errors; 19 warnings all pre-existing (NU1507 source-mapping ×8, CS1574 stale crefs in untouched files).
- `dotnet test --no-build`: 225/225 passed across all 7 projects (Conversation 11, Kernel 66, Reporting 15, FaultGovernance 20, Job 48, Conducting 16, Emission 49).

## S7 — Gates
- Legacy identifiers in src: none. `CollectorTriggerParsing`: absent from code (one intentional historical mention in Conducting README). `IndicatorNames`: absent. Carried files: 7/7 logic-identical.

## Residual risks / follow-ups
- Job→Emission `AdapterGlobalDefaults` constant coupling still deferred (documented in Conducting README + decisions.md).
- `ForceFlushAlwaysForTesting` env reachability, static-publisher DIP seam, assets/findings hardwiring — pre-existing, untouched.
- Suggested version bump: `1.0.0-preview.4` (not set — operator call).

## Post-review repair (code-reviewer-1 Major)
- `IoaRuleRequestConverter.Write` self-recursion (attribute-registered converter re-entered by `JsonSerializer.Serialize` on the same type → uncatchable StackOverflow, reproduced by reviewer) fixed: property-by-property write, `fieldValues` delegated (no converter on `IoaFieldValue`). Shape matches default serialization (declaration order, nulls emitted).
- Two new tests: `Write_RoundTrips_WithoutRecursing`, `Write_EmitsNulls_ForAbsentOptionalFields`. Job.Tests 50/50 green; build 0 errors.
- Minors accepted, not repaired (divergence minimization): dead ruleType ternary; missing ConfigureAwait(false) in BaseFlowHandler.
- Shared's copy of the converter still has the Write bug — adapters-repo follow-up.

## Addendum — residual dup cleanup (operator-approved, 8295bee)
- `TryExtractMetadataObject` single-homed as internal on `Job/AdapterRunEnvelopeParser`; byte-identical private copy removed from `Conducting/AdapterPlatformEventFactory` (now calls the Job helper; Conducting→Job edge pre-existing in DAG).
- Reviewer minor applied: non-object-root guard so the shared Try* helper returns false instead of throwing for future unguarded callers (both current call sites already guard — no behavior change today).
- code-reviewer-2: no material findings. Build 0 errors; full suite 227/227.
