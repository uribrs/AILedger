# Decisions

- Job keeps the single trigger parser (`AdapterTriggerParsing`); the Conducting copy dies. — Job's charter owns "which stream, since when".
- Rename `AdapterBusLegacyFlowExecutor` → `AdapterBusClassifiedFlowExecutor`, `CollectorResumeLegacyExecutor` → `CollectorResumeClassifiedExecutor`. — behavior = classify-exception-and-publish path (vs strategy-decided path); executor may refine wording if it collides with local idiom, but no "Legacy" and no "Default"-style lineage names.
- Carry homes: `ITopicHandler` → `Contracts/` (pure contract, sibling of IAdapter); `BaseFlowHandler` → `Conducting/Indicators/` (control-flow template, sibling of Conducting/Collectors); `IocTypeDetector` + `PrivateIpDetector` → `Kernel/Indicators/` (dependency-light leaf utilities); `IocUploadRequest` + `IoaUploadRequest` + `IoaRuleRequestConverter` → `Envelopes/Indicators/` (wire shapes + their converter, precedent: Envelopes/Common holds AdapterStatusJsonConverter).
- `IoaRuleRequestConverter` is IN scope (7th file). — hard compile dependency of IoaUploadRequest; it is functionality, not catalog.
- `IndicatorNames` stays OUT. — identity/catalog is a consumer concern (same ruling as CollectorNames/CollectorZipNames).
- Type names keep their Shared spelling (no Collector→Adapter rename applies; none contain "Collector").
- Minimal new tests for carried pure logic only: `IocTypeDetector`, `PrivateIpDetector`, and an `IoaRuleRequestConverter` read-mapping round-trip, placed in the matching concern test projects. `BaseFlowHandler`/`ITopicHandler` get no new tests this task (template needs SDK scaffolding; defer to first real indicator consumer).
- DEFERRED: relocating `AdapterGlobalDefaults` file-name/lookback constants out of Emission. — ripples through Emission publisher + multiple consumers; not cheap, not needed for the dedup. The Job parser's `using ...Emission` remains an accepted, documented seam.
- DEFERRED: `Conducting/Indicators` growth beyond BaseFlowHandler (topic-handler factory pattern etc.) until an indicator actually consumes the package.
- Version bump: suggest `1.0.0-preview.4` in the final report; do not set it.
- DELIBERATE DIVERGENCE from verbatim carry (post-code-review repair): `IoaRuleRequestConverter.Write` rewritten property-by-property. — Shared's version self-recurses via the `[JsonConverter]` attribute → uncatchable StackOverflow on any serialize; review-surfaced crash bugs get fixed directly (standing operator rule). Shared still carries the bug — flagged as adapters-repo follow-up.
- ACCEPTED (not repaired, keeps divergence minimal): dead `? "1" : "1"` ruleType branch (no-op, pre-existing, flagged vestigial in the audit); `BaseFlowHandler` await without ConfigureAwait(false) (no sync-context in hosts; verbatim from Shared).
