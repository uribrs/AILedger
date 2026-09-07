# Lessons — 2026-08-27_0817_falcon-policy-plane-restructure (2026-08-27)

## L-42cc8a7b — A7
belief:  The Falcon findings flow shares the assets flow's checkpoint-writer paths.
counter: Re-ran the prior lesson's own verify command: all three OnTerminalSnapshotWithoutPublishedPage call sites are under Flows/Assets/ (FalconAssetsScrollRunner.cs:339,:365, FalconAssetsCheckpointWriter.cs:114). The only findings-side hit is a doc comment at FalconFindingsFlow.cs:494, not a call. Re-confirms L-dae004f6 rather than overturning it.
source:  verifier-2.md §2 A7; grep run 2026-08-27 (verifier) (verifier, 2026-08-27)
verify:  grep -rn 'OnTerminalSnapshotWithoutPublishedPage' src/Cymulate.Integration.Adapters/Collectors/FalconCollector/ | grep -v Flows/Assets/ | grep -v '///'
do not:  Do not assume the two Falcon flows share checkpoint-writer paths; cite the call site, not a doc-comment mention.

## L-1b6ee226 — A15
belief:  Classifying DataPipelineException as non-retryable is safe because the type means an over-ceiling refusal.
counter: GuardedObjectStore raises it from five sites; the read-slot acquisition timeout (GuardedObjectStore.cs:374-376, marker Ingestion__MaxConcurrentReads=) is transient, so a flat non-retryable arm makes read contention permanently fatal. The type is sealed, adds no members over InvalidOperationException and carries no inner exception, so cause is legible only in the message.
source:  verifier-2.md §2 A15; IntegrationInfra GuardedObjectStore.cs:260,283,374-376 (verifier) (verifier, 2026-08-27)
verify:  grep -n 'DataPipelineException' /Users/user/Dev/IntegrationInfra/src/IntegrationInfra/Ingestion/GuardedObjectStore.cs
do not:  Do not classify an overloaded infrastructure exception by its type alone; enumerate its throw sites first and split by cause.

## L-49fe697c — A1
belief:  POST /devices/entities/devices/v2 accepts about 1000 ids per request.
counter: No live vendor call was made this task; every rejection test uses a fabricated response. The code was deliberately written to encode no batch bound, so the bound is neither relied on nor measured.
source:  verifier-2.md §2 A1 (verifier) (verifier, 2026-08-27)
verify:  grep -rn 'MaxIsolationsFunded\|no batch-size bound' src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/Policies/FalconDevicePolicyClient.cs
do not:  Do not re-assume any device-entities batch bound without the vendor errors payload from a failing live run.

## L-33ce251b — A2
belief:  A Falcon 404 always arrives as an HTTP 404 status line.
counter: No vendor evidence obtained. The defense was coded both ways instead and keys on body errors[] plus requested-minus-returned, never on status alone, so the belief no longer changes an outcome.
source:  verifier-2.md §2 A2; spotlight.pdf p19 via L-882455c1 (verifier) (verifier, 2026-08-27)
verify:  grep -n 'errors' src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/Policies/FalconDevicePolicyClient.cs
do not:  Do not key a vendor-rejection defense on HTTP status alone; Falcon is documented to deliver a 404 under a 200 header on at least one endpoint.

## L-cc7eaeb4 — A3
belief:  Policy edge keys resolve against Exposure Analytics' asset match key, so policy-to-asset edges land.
counter: The verify query was not run; no database was touched and no downstream artifact was produced. This task moved the emission site of the envelope, so the risk is live and unmeasured. Previously REFUTED as L-f3058a37.
source:  verifier-2.md §2 A3 (verifier) (verifier, 2026-08-27)
verify:  psql -c "select count(*) from cybi.asset_policy ap join cybi.asset a on a.value = ap.asset_match_key" -- zero reproduces the defect
do not:  Do not treat schema compatibility as identity-domain compatibility; check the join key's value domain after any change to where the envelope is emitted.

## L-85e358c4 — A4
belief:  rules[].value reaches Postgres as native JSON, so Exposure Analytics can read toggle and slider values.
counter: The verify query was not run. Definitions now travel through a new NDJSON staging codec and back through RehydrateDefinitions -- a changed serialization path on the exact field that broke before. Previously REFUTED as L-5de41efb.
source:  verifier-2.md §2 A4 (verifier) (verifier, 2026-08-27)
verify:  psql -c "select distinct jsonb_typeof(value) from cybi.security_policy_rule" -- 'string' reproduces the defect, 'object' is correct
do not:  Do not treat a successful write as proof the payload is consumable; assert jsonb_typeof on nested values after changing a serialization path.

## L-3671c4e8 — A5
belief:  Falcon's per-batch and per-stage memory cost is known well enough to trade against.
counter: No memory measurement exists in this task's artifacts; wall-clock was recorded instead. The task introduced two new allocations the belief covers -- a page-size assignment batch and a run-scoped AID-to-edge dictionary -- and measured neither. Third consecutive UNTESTED on this belief after L-85a32cda and L-9ad5a354.
source:  verifier-2.md §2 A5 (verifier) (verifier, 2026-08-27)
verify:  none -- citation is not mechanically checkable without a live run under memory instrumentation
do not:  Do not cite a config comment's target figure as a measured memory cost.

## L-1a37307e — A8
belief:  Emission order does not affect identity for the staged policy plane artifacts.
counter: No test asserts ordering determinism. Both artifacts are serialized from Dictionary enumerations with no contractual order; the risk is structurally avoided because both are read back id-keyed and neither is published, which is an argument rather than evidence.
source:  verifier-2.md §2 A8 (verifier) (verifier, 2026-08-27)
verify:  grep -n 'Snapshot()' src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/Policies/FalconPreventionPolicyCache.cs
do not:  Do not conflate unstable ordering with unstable identity, and do not rely on Dictionary enumeration order for a persisted artifact.

## L-00f62476 — A9
belief:  device_policies.prevention.settings_hash exists and is populated on Falcon device entities.
counter: No live vendor response was inspected. The landed edge carries the vendor assignment object verbatim, so the field is neither extracted nor asserted -- exactly as unverified as at baseRef. Not vendor-documented; searched official CrowdStrike docs and FalconPy source and found nothing.
source:  verifier-2.md §2 A9 (verifier) (verifier, 2026-08-27)
verify:  grep -n 'settings_hash' /Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/crowdstrike/crowdstrike_policy_projection.py
do not:  Do not treat a downstream parser reading a vendor field as proof the vendor populates it.

## L-e872a8ca — A10
belief:  GET /policy/combined/prevention/v1 returns precedence and a default-policy indicator per policy.
counter: The endpoint is now called but no live response was inspected. Fields flow through verbatim rather than being extracted, deliberately, so nothing asserts they arrive. Endpoint identity and limit/offset bounds are confirmed from falconpy _prevention_policies.py; the per-policy field list is not.
source:  verifier-2.md §2 A10 (verifier) (verifier, 2026-08-27)
verify:  grep -n 'BuildPreventionPolicySweepUrl' src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/Policies/FalconPreventionPolicyClient.cs
do not:  Do not report precedence or is_default as populated until a live sweep response has been inspected; passing a field through verbatim is not evidence it exists.

## L-3aff9764 — A11
belief:  About 12 distinct prevention policies is representative for a large Falcon tenant.
counter: No tenant measurement taken. The figure comes from a POC run against 23 devices, which is not scale evidence for a 97,332-host tenant. It is load-bearing again now that the single-page sweep landed.
source:  verifier-2.md §2 A11; crowdstrikepolicies POC meta prevention_policies_distinct=12 (verifier) (verifier, 2026-08-27)
verify:  grep -n 'SweepMaxPages\|SweepPageSize' src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/Policies/FalconPreventionPolicyClient.cs
do not:  Do not size a single-page sweep from a POC tenant's policy count.
