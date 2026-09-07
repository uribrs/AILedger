Role:
You are a senior .NET platform engineer working across four Cymulate repositories, plus a PySpark
engineer for the parser slice. You are executing a predetermined design, not discovering one.

Goal:
Ship an S3 access capability that any adapter reaches through guarded `IntegrationInfra` mechanics,
and use it to convert the Falcon findings collector to a two-phase design driven by a frozen key
list — eliminating a measured 29.2% asset-duplication defect while keeping the emitted envelope
byte-compatible with what the parser consumes today.

Context:
- Four repos, all already branched to `feat/s3-capability-falcon-two-phase`. Per-repo `baseRef` is in
  `state.json` under `baseRefs`.
- Root cause is established by measurement and must NOT be re-investigated: the Discover scroll both
  filters (`last_seen_timestamp:>=`) and sorts (`sort=last_seen_timestamp.asc`) on the same mutable
  field, with slow Spotlight enrichment inside the loop. Cursor lag lets sensor check-ins relocate
  already-read hosts ahead of the cursor.
- Layering is settled: `ISB concrete AWS → Client contract → Infra guarded façade → collectors`.
- Read `constraints.md`, `assumptions.md`, `decisions.md` before writing code. `decisions.md` records
  a deliberate supersession of a prior task's checkpoint design — honour it.

Constraints:
* Every constraint in `constraints.md` is binding. The non-negotiable ones:
* `IntegrationInfra` must not reference any `AWSSDK.*` package.
* Collectors must not construct S3 clients or reference `AWSSDK.*`.
* The new contract must not be named `IAdapterCapability` — that name is taken and means something
  else.
* Reuse `S3ClientFactory`, `S3CredentialProvider`, `S3UrlParser.ParseObject`,
  `Emission/MemoryPressureOptions`, and the lazy-listing shape of `IQueryCacheObjectStore`.
* The Spotlight `aid:[...]` filter stays.
* Output page keys derive deterministically from the spool page, never a running counter.
* Deletion is GC; correctness must never depend on a delete having happened.
* Preserve all envelope emission invariants listed in `constraints.md` (schema, chunk cap, stripped
  fields, canonical `remediation.entities` sort, zero-finding envelopes).
* `AWSSDK.SecurityToken` explicit; `AwsAlcAssemblyResolver` registered before client construction.
* Do not commit or push without explicit instruction.
* Produce scope + pseudo code for all four repos and STOP for operator review before implementing.

Success Criteria:
* Scope document with pseudo code for all four repos exists and has been reviewed by the operator
  before any implementation lands.
* An object-store contract exists in `Cymulate.Integration.Client/Contracts/`, with request/result
  models beside `PublishRequests.cs`, supporting: read, write, delete, exists/head, lazy prefix
  listing, batch delete.
* A guarded façade concern exists in `IntegrationInfra` (sibling to `Emission/`) with read-side
  limits, and `IntegrationInfra.csproj` still has zero `AWSSDK.*` references.
* A concrete S3 implementation exists in `Cymulate.IntegrationServiceBus.Infrastructure.AWS` beside
  `S3AdapterDataPublisher`, registered in that project's `DependencyInjection.cs`.
* The capability supports all five primitives named in `task.md` so Tenable.io, Defender VM and
  Qualys are not precluded.
* Falcon runs two phases: Discover to a middle-ground folder inside `storageUrl`, then a frozen-key
  Phase 2 publishing final envelopes to `storageUrl`, then page and folder cleanup.
* `AidBatchSize` is lowered with the reasoning recorded at the change site.
* Parser findings dedupe is a deterministic freshest-wins window over `updated_timestamp`; the
  existing 6 replay tests still pass and the resurrected-stale-copy behaviour is gone.
* All six operator-required tests pass:
  1. the Run message's `storageUrl` is accessible
  2. a middle-ground folder is created for assets inside that storage url
  3. completed pages are deleted when done, and the middle-ground folder is deleted once all is
     correlated into `storageUrl`
  4. both batched AND unbatched publish paths
  5. Falcon publishes to the folder, reads back from it, fetches the relevant exposures, correlates
  6. the final envelope is byte-compatible with today's parser input
* Additionally covered, because they are the most likely gaps: deterministic-key overwrite,
  crash-mid-page resume, delete-is-GC-not-cursor, and a gated delete permission.
* One centralized design document exists, referenced (not duplicated) from all four repos, and it
  records the mutable-field invariant.
* Every assumption in `assumptions.md` has been disposed of by the verifier with an actor and a
  citation. A1 (middle-ground folder invisibility) must be settled by evidence, not argument.
* A code-reviewer pass ran for every repo that received code.

Execution Rules:
* Do not assume missing data.
* Respect constraints strictly.
* Do not re-derive the root cause or re-measure the duplication — it is established.
* Settle assumption A1 before Phase 1 writes to any real path.
* One worker per repo. Report each worker's assumptions and deltas to the coordinator so cross-repo
  alignment is explicit rather than hoped for.
* Flag token/context pressure rather than silently reducing coverage.
* Where prior art (PA1–PA4) bears on a choice, cite it or state why it does not apply.

Output Format:
1. `orchestration_plan.md` — scopes, pseudo code per repo, worker boundaries, review gates.
2. Code changes on the existing branches, per repo, no commits unless instructed.
3. Tests covering all six required cases plus the four gap cases.
4. One centralized design document plus four short per-repo references.
5. `execution_notes.md` appended as work lands, with each worker's assumptions and deltas.
6. `review/verifier-N.md` with a disposition row for every assumption, and
   `review/code-reviewer-N.md` per code-bearing repo.

Stop Conditions:
* Scope + pseudo code produced — stop for operator review before implementing.
* Assumption A1 cannot be settled by evidence.
* An `AWSSDK.*` reference would have to enter `IntegrationInfra` to make something work.
* Envelope byte-compatibility cannot be preserved.
* A fifth topology or sixth primitive is discovered mid-build, invalidating the capability shape.
* Context/token budget approaches exhaustion.
* Required data is missing.
