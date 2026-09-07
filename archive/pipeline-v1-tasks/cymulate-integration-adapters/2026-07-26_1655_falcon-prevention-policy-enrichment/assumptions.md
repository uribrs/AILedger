# Assumptions

## Validated

- Discover `aid` is the join key for Hosts `device_id`.
- `/devices/entities/devices/v2` returns assignment metadata under
  `device_policies.prevention`.
- `/policy/entities/prevention/v1` returns the first-level definition including
  nested `prevention_settings`.
- ~~The device endpoint accepts up to 5,000 IDs; current page/batch bounds are
  smaller.~~ **REJECTED / IN DOUBT — 2026-07-26.** A live run
  (correlation `e1b72bd4-0321-4a6e-a639-d0351cb7a29b`) got HTTP 400 from
  `POST /devices/entities/devices/v2` with 250 AIDs, while the single-AID access
  probe on the same session/tenant returned 200. This assumption came from
  research, never from observed traffic, and the "one request per bounded unit"
  design rests on it. Two candidate causes remain (a per-request ID cap below
  250, or one or more IDs the vendor rejects); the response body carried
  populated `resources` alongside the 400, which favours the second. The vendor
  `errors` list was truncated by the 2,000-char error-snippet budget, so the
  budget for policy requests was raised to 16,000 to make the next run
  self-diagnosing. Do not re-mark this VALIDATED without the `errors` payload.
- A 100-ID definition chunk is conservative for the implementation.
- A successful response may contain `resources: null` or omit a requested
  resource.
- Findings volume dominates policy metadata volume and existing findings
  chunking remains necessary.
- The existing findings builder deep-clones the host into each chunk.
- The assets parser currently streams a page, requiring one-page materialization
  before policy enrichment.

## Contract assumptions locked for implementation

- Empty policy shape is `collection_status: "complete"` with
  `prevention: null`.
- Disabled shape is `collection_status: "disabled"` with `prevention: null`.
- Not-found/unavailable retain the raw assignment and use `definition: null`.
- The configuration switch is default-enabled and should be named
  `EnablePreventionPolicyEnrichment` unless an immediately adjacent naming
  convention requires an equivalent spelling.
- The run cache is not persisted and does not require a checkpoint change.

## Operator directives — VALIDATED

- `FalconCollector/Flows/Policies/` is a required, dedicated policy space.
- Enrichment is a single shared component used by both flows, decided in
  `decisions.md` #11; the shared-vs-per-flow question is closed.
- Policies are collected on every assets gathering, i.e. in both
  `CollectAssets` and `CollectFindings`.
- Idiomatic small-method C# with no new framework or generic abstraction.
- Branch `feature/falcon-prevention-policy-enrichment` is prepared and current
  with `dev`; no commit or push in this task.

## Flexible implementation details

- Exact class/file names may follow the closest current Falcon convention; the
  `Flows/Policies/` folder itself is fixed.
- Definition chunk size may remain a constant unless the existing
  configuration pattern gives a clear reason to expose it.

No critical product assumption remains open. If current code or official local
documentation contradicts a locked contract, record the evidence and stop
before silently changing behavior.

