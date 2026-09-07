# Lessons — 2026-08-27_1153_failed-checkpoint-retention-and-retry (2026-08-27)

## L-6f1353b8 — A7
belief:  A collector's CanResumeFrom consumes only the adapter_state blob and never ISB's flat checkpoint columns.
counter: Three of 18 collectors read flat columns inside the gate: YamlAdapter.cs:450 measures total run age off CreatedAtUtc (decisive for every YAML vendor), FalconCollector.cs:521 branches on CurrentPage/ProcessedItems/ProcessedFindings, DummyCollector.cs:249 returns true on CurrentPage alone.
source:  cymulate-integration-adapters YamlAdapter.cs:450, FalconCollector.cs:521, DummyCollector.cs:249 (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  grep -rn 'checkpoint.CurrentPage\|checkpoint.CreatedAtUtc\|ProcessedFindings' ~/Dev/cymulate-integration-adapters/src --include=*.cs
do not:  do not treat ISB's flat checkpoint columns as inert bookkeeping the collector ignores; check the adapter's CanResumeFrom entry point, not its Recovery/ policy helper.

## L-9d08e415 — A13
belief:  AdapterDoneMessage lives in the Cymulate.Integration.Client package, so adding a field to it is a coordinated cross-repo package release.
counter: It is ISB source at Domain/.../Messaging/AdapterDoneMessage.cs:10, absent from IntegrationInfra source and from both restored package DLLs. The consumer takes @Payload() data: any with no runtime validation, so ISB can add an optional field and ship independently.
source:  IntegrationServiceBus Domain/Cymulate.IntegrationServiceBus.Domain/Messaging/AdapterDoneMessage.cs:10; cymulate-integrations connector-manager.controller.ts:29 (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  grep -rn 'class AdapterDoneMessage' ~/Dev/IntegrationServiceBus/src ~/Dev/IntegrationInfra/src
do not:  do not assume a Cymulate wire DTO lives in the shared Client package because it looks like contract surface; locate the class before costing a release.

## L-70bf7cf8 — A11
belief:  The collector checkpoint-helper pattern is uniform, so a per-request staleness bound has one shape to change.
counter: Seven deviations. YamlAdapter implements a wholly separate, runtime-overridable 24h mechanism that never calls RecoveryParsingHelper; DummyCollector has no staleness test; TenableSc and InsightVm inline the gate in the collector; TenableIo routes to a second correlated gate.
source:  cymulate-integration-adapters YamlAdapter.cs:442-465, TenableScCollector.cs:312, InsightVmCollector.cs:403, TenableIoCollector.cs:448 (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  grep -rln 'IsCheckpointStale' ~/Dev/cymulate-integration-adapters/src --include=*.cs | wc -l
do not:  do not assume editing the shared recovery helper reaches the whole collector fleet; it misses every YAML vendor entirely.

## L-16f8c597 — A5
belief:  UnservableTerminalAge is CheckpointTtl * 0.75, so the sweep's give-up bound scales with the TTL config key.
counter: It is max(CheckpointTtl*0.75, StaleClaimThreshold*6). At default config the first term wins and 18h is right, but lowering the TTL or raising the stale threshold changes which term dominates.
source:  IntegrationServiceBus CheckpointRecoveryHandler.cs:728-736, :699 (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  sed -n '695,740p' ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Applications/Cymulate.IntegrationServiceBus.Application/Services/CheckpointRecoveryHandler.cs
do not:  do not read a derived time bound as a plain multiple without checking for a floor.

## L-9c94d22d — L-57acd253
belief:  The Tenable.io collector has no checkpoint/resume path.
counter: It declares IResumableAdapter at TenableIoCollector.cs:34 with CanResumeFrom at :448, a Recovery/ helper and resume runner, a correlated-format gate, and two staleness call sites. The fleet denominator is 18 of 18 resume-capable, not a subset.
source:  cymulate-integration-adapters Collectors/TenableIoCollector/TenableIoCollector.cs:34, :448 (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  grep -n 'IResumableAdapter\|CanResumeFrom' ~/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector/TenableIoCollector.cs
do not:  do not re-assume Tenable.io lacks resume; this supersedes the 2026-08-19 finding.

## L-b1b66b36 — L-96bb76cc
belief:  Cymulate.Integration.Client carries 33 stale copies of files ISB deleted and is missing the SiemRules delta, i.e. the package has drifted from source in both directions.
counter: The both-directions check run this task was a type-set diff and found no drift at 1.2.0-preview.0 (one undocumented static class source-to-package, 18 compiler-generated the other way). It neither reproduced nor refuted the ledger's file-level premise.
source:  review/verifier-1.md §2; research/infra-staleness-and-contracts.md §7 (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  none — citation is not mechanically checkable without a file-level package-vs-source extraction
do not:  do not re-assume the 33-file drift figure without a file-level comparison; a type-set diff does not test it.

## L-f80a1c15 — D4
belief:  Readability judgment lives entirely in the collector and state lives entirely in ISB, so the three-repo decomposition is clean.
counter: Leakier than assumed. Three collectors read ISB-owned flat columns to make the readability call, and the only no-contract-change route for a per-request staleness bound has ISB writing that bound into the collector's own state blob.
source:  review/verifier-1.md §4; research/infra-staleness-and-contracts.md §4 (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  grep -n 'MapToAdapterCheckpoint' ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Applications/Cymulate.IntegrationServiceBus.Application/Commands/ProcessEventCommandHandler.cs
do not:  do not present the state/readability/decision split as clean without naming where it leaks.
