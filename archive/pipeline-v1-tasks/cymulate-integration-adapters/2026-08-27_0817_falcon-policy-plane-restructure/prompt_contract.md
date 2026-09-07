# Prompt Contract — Falcon Policy Plane Restructure

Role:
You are a senior .NET engineer working on Cymulate's FalconCollector inside
`cymulate-integration-adapters`, with the CrowdStrike Falcon collection pipeline
(Discover spool -> policy -> Spotlight) as your subject matter.

Goal:
Make prevention-policy collection a stage over a frozen host inventory instead of
an enrichment inside the Discover spool, so that a policy failure can never
discard a completed Discover scroll, and so that policy definitions travel once
per tenant rather than once per host.

Context:
- `FalconHostSpooler.SpoolAsync` (`Flows/Findings/TwoPhase/FalconHostSpooler.cs:186`)
  runs `EnrichStagedPagesAsync` and only then `WriteManifestAsync`. The manifest
  is the sole completion proof, so an enrichment failure leaves no manifest and
  `ResolveFrozenKeyListAsync` re-spools from page 1 under a new generation.
- `EnrichStagedPagesAsync` rewrites each staged page through
  `ObjectWriteRequest.FromBytes` (`FalconStagingArea.cs:188`). Policy enrichment
  deep-clones the definition per host (`FalconPolicyEnricher.cs:198,224`), which
  grew page 0 from 3.7 MB to 9,594,735 bytes and tripped
  `Ingestion__MaxInMemoryObjectBytes=8388608` in `GuardedObjectStore.cs:281`,
  throwing `DataPipelineException`.
- `DataPipelineException` has no arm in `FalconFlowExceptionClassifier`, so it
  falls to `_ => null` and `UnknownFlowRetryPolicy` retries 30/60/120s. Live
  cost: 9 generations, 31 minutes, 0 records (prod-eu correlation
  `6a8eeaa266c86fb72e4e45a8`).
- `FalconAccessProber.ProbePreventionPolicyAsync` (`Processing/Validation/FalconAccessProber.cs:51`)
  probes with `Aids=1` and has no 404 handling; the 5xx swallow lives in
  `ProbeAsync` (`:178`), which that method never calls. A stale Discover AID
  404'd and failed the run as `INVALID_CONFIGURATION`, contradicting the method's
  own docstring that only 401/403 propagate.
- Downstream (`cymulate-integration-parsers`,
  `libs/packages/parsers/crowdstrike/crowdstrike_policy_projection.py`) already
  emits one row per policy with a `policy_edges` blob, and burns a
  `Window.partitionBy("_external_id")` rank pass deduping our per-host copies.
- Mid-scroll HTTP 500s on the Discover `after=` cursor were observed live on
  2026-08-26/27 at pages ~25 and ~43 — a new condition for that endpoint.

Constraints:
* Read `constraints.md` in this directory and treat every line as binding.
* Do not replace the host-directed assignment call with a policy-members sweep. The members endpoint cannot page past `limit + offset > 10000`.
* Prevention-only. Do not widen to other policy planes.
* Staged host pages are immutable after freeze.
* Policy definitions live under `_staging/policies/pgen_<id>/`, not under the host generation.
* Spotlight gates on traversed, never on succeeded. A policy stage ending empty must still unblock spotlight.
* Every traversal unit writes its object even when empty; every skip/drop/reject is counted into the manifest ledger.
* Defenses key on the response body's `errors[]` and on requested-vs-returned comparison, not on HTTP status alone.
* Never parse vendor prose to identify a rejected AID.
* Preserve the two pre-existing uncommitted branch changes (config default `false`, `CollectorVersion` 6.3.4). They are not part of this work.
* Do not change the `FalconDevicePoliciesEnvelope` wire vocabulary — the parser reads it.

Success Criteria:
* A policy-stage failure leaves a completed Discover scroll intact: the manifest exists, and the next leg resumes without re-spooling. Demonstrated by a test that fails the policy stage after the freeze and asserts no new generation is created.
* No code path rewrites a staged host page after the manifest is written.
* A staged host page's size is independent of the number of distinct policies: the per-host edge carries ids and status only, never a definition body.
* `DataPipelineException` classifies as non-retryable, with a test asserting it does not reach `UnknownFlowRetryPolicy`.
* A 404 or a 400 naming rejected ids yields empty envelopes for exactly those ids, the surviving ids keep their assignments, and the counts appear in the ledger. Covered by tests for: 404 with empty `resources`; 400 with populated `resources` plus `errors`; and a 200 whose body carries a 404 under `errors`.
* `ProbePreventionPolicyAsync` does not fail init on 404; 401/403 still propagate. Test both.
* A mid-scroll HTTP 500 re-anchors from the watermark and the scroll completes, with no generation lost. Test asserts one generation id across the fault.
* A tenant with zero prevention policies still produces a written policy plane object and a ledger entry distinguishing traversed-and-empty from not-traversed.
* Existing Falcon collector unit tests pass, including the two-phase findings tests.
* `assumptions.md` entries are disposed by the verifier with actor and citation.

Execution Rules:
* Do not assume missing data. Where vendor behavior is unverified, code the defensive branch and record the assumption rather than picking the optimistic path.
* Respect constraints strictly.
* Diagnose before changing. If a fix produces more failures than it resolves, revert it and report.
* Do not expand scope. Phase 2 throughput, other policy planes, and the config default are out of scope.
* Append progress to `execution_notes.md`; update `state.json` step statuses as they change.

Output Format:
* Code changes in `src/Cymulate.Integration.Adapters/Collectors/FalconCollector/` plus tests under `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test/`.
* `execution_notes.md` recording what changed, per step, with file:line references.
* A short summary naming any success criterion not met and why.

Stop Conditions:
* When every success criterion is met.
* When a constraint would have to be violated to proceed.
* When required data is missing and no defensive branch can stand in for it.
* When the ~1000-id batch bound is contradicted by a live 400 — degrade the batch and report; do not silently re-tune it into the design.
