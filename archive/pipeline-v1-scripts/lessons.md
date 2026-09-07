# Lessons Ledger

Append-only, cross-repo. Written by `workflow-coordinator` step 5 from the verifier's assumption
disposition. Read by `prompt-contract-designer` prior-art recall.

Rules:

- One row per refuted, never-tested, or drifted item. Never for confirmations.
- **Rows are written by `mint-lesson.py`, not by hand.** The coordinator's step 5 pipes JSON to it
  and the script assigns the id and formats the row. Hand-typing is how the format drifted for three
  versions and how the id became unreproducible.
- **One line per row, hard cap.** This file is an index, not a record. Detail lives in `lesson.md`
  inside the task directory the row points at — belief, counter-evidence, citation, verify command.
- Append only. Never rewrite or delete an existing row, including one a later task overturned.
  Add the correction as a new row instead; the history of a belief is itself evidence.
- A row is stale operational evidence. Recall seeds it as an OPEN assumption, never as a fact.

Row format — pipe-delimited, id first, with two optional trailing fields:

```text
L-<8hex> | <date> | <repo> | <tag>,<tag> | <CLASS> | <belief> | →<timestamp>_<task-slug>
L-<8hex> | ... | →<pointer> | supersedes:L-<8hex>
L-<8hex> | ... | →<pointer> | retracts:L-<8hex>
```

`CLASS` is REFUTED, UNTESTED, or DRIFTED.

The id is `L-` followed by the first 8 hex of `sha1(belief)`. It is **assigned, not derived from the
row's contents**, which buys three things: it cannot collide the way `<date>-<first-tag>` did once a
single task refuted two beliefs about one vendor on one day; re-tagging a row never invalidates a
`supersedes:` aimed at it; and re-running step 5 mints the same id for the same belief, so a retry
cannot duplicate a row.

- `supersedes:` — new evidence changed the conclusion. The old row stays; recall skips it.
- `retracts:` — the old row was invalid when minted. The old row stays; recall skips it.

Recall greps this file by tag, drops superseded and retracted rows, keeps the newest 10 matches, and
opens at most 3 of the archives they point at. It never reads the whole file.

The multi-line entries below predate the one-line format. They stay as written — greppable, and
still valid evidence. Do not migrate them; new rows use the format above. Their `L-` ids were
backfilled in 1.5.0 so they can be superseded at all: before that, no row in this file carried an id
field, and the parser only ever read `supersedes:` off the one-line shape.

---

<!-- Backfill: seeded 2026-08-03 from the evidence-citing REJECTED entries already in
     ~/codex-state/tasks/. Actor recorded as the original task where identifiable. These predate the
     disposition table, so several cite the task's own notes rather than a diff hunk. -->

## L-1e94a68a | 2026-04-29 | AgentService | tenable | checkpoint-resume | REFUTED
Assumed the AgentService collector had a checkpoint/resume path worth mirroring from C1.
It has neither — it waits for export FINISHED then uploads, so mid-chunk-resume duplication is
structurally impossible there.
source: research/engine-map-and-change-plan.md, W1 (researcher, 2026-06-12)
do not re-assume a resume path in AgentService without pointing at the checkpoint write.
→ tasks/AgentService/2026-06-12_1300_tenable-agentservice-asset-centric-mirror/

## L-16fed2de | 2026-07-26 | cymulate-integration-adapters | falcon | batching | REFUTED
Assumed POST /devices/entities/devices/v2 accepts up to 5,000 IDs per request.
A live run got HTTP 400 with 250 AIDs while a single-AID probe on the same session returned 200;
the 400 body still carried populated `resources`, favouring a rejected ID over a hard cap.
source: correlation e1b72bd4-0321-4a6e-a639-d0351cb7a29b (executor, 2026-07-26)
do not re-assume any batch bound without the vendor `errors` payload from a failing run.
→ tasks/cymulate-integration-adapters/2026-07-26_1655_falcon-prevention-policy-enrichment/

## L-cc37cc31 | 2026-07-29 | yaml-adapter-engine | yaml-engine | definition-sourcing | REFUTED
Assumed YAML definitions reach the engine only through the corpus, making corpus coverage sufficient
proof that composition stays wired.
Definitions also arrive inline in the ISB dispatch payload, or are self-downloaded from S3 by name,
so a vendor can author an unknown key that no corpus test ever sees.
source: YamlOperationRunner.cs:450-453 (executor, 2026-07-29)
do not treat corpus coverage as total coverage of engine inputs.
→ tasks/yaml-adapter-engine/2026-07-29_1102_capability-drift-inventory/

## L-96bb76cc | 2026-08-02 | IntegrationServiceBus | sdk-client | package-drift | REFUTED
Assumed `Cymulate.Integration.Client` is a superset of the SDK and only adds features.
Its extra 33 files are stale copies of files ISB has since deleted, and it is missing the entire
3.2.1→3.3.0 SiemRules delta (`grep -rn SiemRules` over Client returns nothing). Drift runs both ways.
source: Contracts/IAdapterCapability.cs:50, Enums/AdapterCategory.cs:59 (executor, 2026-08-02)
do not assume directional drift between two forked packages; diff both ways.
→ tasks/IntegrationServiceBus/2026-08-02_0936_sdk-to-client-isb-realignment/

## L-c2d905f0 | 2026-08-02 | IntegrationServiceBus | dotnet | type-forwarding | REFUTED
Assumed a zero-code `TypeForwardedTo` shim could bridge old collectors to the renamed Client package.
Type forwarding cannot rename a namespace, so the shim cannot preserve the old namespace surface.
source: constraints.md (executor, 2026-08-02)
do not plan a shim across a namespace rename without checking the forwarding mechanism's limits.
→ tasks/IntegrationServiceBus/2026-08-02_0936_sdk-to-client-isb-realignment/

## L-c9239f9c | 2026-07-02 | cymulate-integration-adapters | falcon | entity-ids | REFUTED
Assumed remediation entity IDs are query-unstable across runs.
Disproved — IDs are identical across runs; only array order differs. (Sorting at emission was still
adopted, to fix parser `entities[0]` nondeterminism, so the fix outlived the reason.)
source: cross-run comparison (executor, 2026-07-02)
do not conflate unstable ordering with unstable identity.
→ tasks/cymulate-integration-adapters/2026-07-02_1705_falcon-correlated-findings-redesign/

## L-b0f288ce | 2026-06-25 | CollectorBase | auth | sigv4 | REFUTED
Assumed AwsSigV4 was available as an auth variant to wire.
It is not among the 10 Shared `AuthSelection` variants and does not exist in Shared at all.
source: Shared AuthSelection enumeration (executor, 2026-06-25)
do not assume an auth scheme exists in Shared without enumerating AuthSelection.
→ tasks/CollectorBase/2026-06-25_1339_auth-breadth/

## L-5e32d960 | 2026-08-18 | cymulate-integration-adapters | falcon | rate-limits | REFUTED
Assumed ~6,000 req/min of Spotlight headroom was ours to spend on concurrency.
It is a token bucket (100 req/s sustained, 6,000 burst) scoped per CUSTOMER ACCOUNT and pooled across every
endpoint and every API client — the customer's budget, shared with their other tooling, occupancy invisible
to us while rate-limit headers go unread.
source: FalconDocs/OfficialDocs/crowdstrike-auth.pdf §1.8 p42-43 (researcher, 2026-08-18)
do not size a concurrency degree as a fraction of a vendor rate number without establishing whose budget it is.
→ tasks/cymulate-integration-adapters/2026-08-18_0829_falcon-spotlight-fetch-concurrency/

## L-98cb5412 | 2026-08-18 | cymulate-integration-adapters | tenable | reference-impl | REFUTED
Assumed TenableIo already had an ordered parallel-collect/serial-publish pipeline to port and measure against.
It has none — its three Parallel.ForEachAsync sites are unordered fan-outs over order-independent sub-items
inside an already-sequential pump. Nothing to port; built from first principles instead.
source: research/internal-recon.md q5; TenableIoVulnPhase.cs:429/215-224 (recon, 2026-08-18)
do not plan around "collector X already does this" without checking the parallelism is the SHAPE you need.
→ tasks/cymulate-integration-adapters/2026-08-18_0829_falcon-spotlight-fetch-concurrency/

## L-882455c1 | 2026-08-18 | cymulate-integration-adapters | falcon | cursor-expiry | UNTESTED
Assumed an expired Spotlight `after` token surfaces as HTTP 404 and triggers the scroller's re-anchor.
spotlight.pdf p19 says that 404 "displays with a 200 OK header and the 404 code under errors in the response
body" — if accurate, FalconHttpFailureClassifier.cs:22 can never fire on this leg and batches truncate
silently. Never probed. Predates the concurrency work; concurrency raises the odds of triggering it.
source: FalconDocs/OfficialDocs/spotlight.pdf p19, verified on the page image (researcher, 2026-08-18)
do not read SpotlightReanchors == 0 as evidence about cursor expiry in either direction.
→ tasks/cymulate-integration-adapters/2026-08-18_0829_falcon-spotlight-fetch-concurrency/

## L-85a32cda | 2026-08-18 | cymulate-integration-adapters | falcon | memory-bound | UNTESTED
Assumed peak memory is degree x one materialised batch at ~35-50 MB each.
Never measured. 35-50 MB is a TARGET in the config comment; the observed figure at AidBatchSize 10 was
90-125 MB, which would put degree 4 at ~360-500 MB and the clamp ceiling of 16 at ~1.4-2 GB — under the 8Gi
limit but able to cross the 70%-of-4Gi HPA trigger. Wall-clock spacing between published objects (A7) was
likewise never addressed, and concurrency compresses it by design.
source: review/verifier-1.md §2; FalconCollectorConfiguration.cs:95-98 (verifier, 2026-08-18)
do not quote the memory formula as a measurement — its per-batch input is a target, not an observation.
→ tasks/cymulate-integration-adapters/2026-08-18_0829_falcon-spotlight-fetch-concurrency/
L-b345971b | 2026-08-19 | cymulate-integration-parsers | cymulate-integration-parsers,tenable,payload-width | REFUTED | The Tenable parser's additional_fields column is effectively the finding's whole plugin object, so plugin size measures the written payload. | →2026-08-19_1626_tenableio-parser-postgres-load
L-1587791b | 2026-08-19 | cymulate-integration-parsers | cymulate-integration-parsers,postgres,performance-insights | REFUTED | Dividing a statement's Performance Insights AAS by its average latency gives the number of concurrent writers (58.59 / 0.11743 = ~499). | →2026-08-19_1626_tenableio-parser-postgres-load
L-90bc1864 | 2026-08-19 | cymulate-integration-parsers | cymulate-integration-parsers,spark,jdbc-write | REFUTED | The number of concurrent Postgres connections from a Spark JDBC write equals the DataFrame's partition count. | →2026-08-19_1626_tenableio-parser-postgres-load
L-57acd253 | 2026-08-19 | cymulate-integration-parsers | integration-service-bus,tenable,checkpoint-resume | REFUTED | The Tenable.io collector has no checkpoint/resume path, so mid-run replay cannot duplicate published findings. | →2026-08-19_1626_tenableio-parser-postgres-load
L-9a2a1c4d | 2026-08-19 | cymulate-integration-parsers | cymulate-integration-parsers,spark,error-masking | REFUTED | A Glue parser failing with 'An error occurred while calling oNNN.jdbc. This connection has been closed.' tells you why the write failed. | →2026-08-19_1626_tenableio-parser-postgres-load
L-0b629a04 | 2026-08-19 | cymulate-integration-parsers | cymulate-integration-parsers,postgres,index-cost | UNTESTED | Per-row index maintenance is a material share of the exposures INSERT CPU. | →2026-08-19_1626_tenableio-parser-postgres-load
L-ab924833 | 2026-08-19 | cymulate-integration-parsers | integration-service-bus,mongo,atlas-iam | UNTESTED | A Mongo _id handed over as a run identifier can be resolved to its run document in stg. | →2026-08-19_1626_tenableio-parser-postgres-load
L-727045fe | 2026-08-19 | cymulate-integration-parsers | cymulate-integration-parsers,glue,deploy-verification | DRIFTED | The deployed parsers.whl can be downloaded and inspected to prove which code path shipped to an environment. | →2026-08-19_1626_tenableio-parser-postgres-load
L-766ef02a | 2026-08-19 | cymulate-integration-parsers | integration-service-bus,tenable,publish-pipeline | UNTESTED | TenableIo's publish pipeline is a sequential pump with unordered parallel fan-outs over order-independent sub-items, and no ordered parallel-collect/serial-publish stage. | →2026-08-19_1626_tenableio-parser-postgres-load
L-f3058a37 | 2026-08-20 | cymulate-integration-adapters | cymulate-exposure-analytics,policy-edge-identity | REFUTED | The CrowdStrike parser's policy edge keys resolve against Exposure Analytics' asset match key, so policy-to-asset edges land. | →2026-08-20_1101_falcon-policy-s3-parser-e2e-validation
L-5de41efb | 2026-08-20 | cymulate-integration-adapters | cymulate-integration-parsers,json-double-encoding | REFUTED | rules[].value reaches Postgres as native JSON, so Exposure Analytics' JSON field extraction reads toggle and slider values. | →2026-08-20_1101_falcon-policy-s3-parser-e2e-validation
L-bd5a48b3 | 2026-08-20 | cymulate-integration-adapters | cymulate-exposure-analytics,deploy-verification | UNTESTED | The Exposure Analytics revision running in production was confirmed to contain the policy ingestion path. | →2026-08-20_1101_falcon-policy-s3-parser-e2e-validation
L-a50e6941 | 2026-08-26 | cymulate-integration-adapters | falcon,streaming-transport | REFUTED | A vendor Retry-After or a 429 status is observable to Falcon's collector code when a Spotlight request fails. | →2026-08-26_1001_falcon-spotlight-transport-retry
L-dae004f6 | 2026-08-26 | cymulate-integration-adapters | falcon,checkpoint-resume | REFUTED | The Falcon findings flow reaches OnTerminalSnapshotWithoutPublishedPage when a Phase 2 deferral snapshots its position without advancing the page. | →2026-08-26_1001_falcon-spotlight-transport-retry
L-9fa66041 | 2026-08-26 | cymulate-integration-adapters | falcon,streaming-transport | DRIFTED | An open circuit breaker should be retried in-flow alongside transport faults, because it will half-open on its own. | →2026-08-26_1001_falcon-spotlight-transport-retry
L-40bca6e4 | 2026-08-26 | cymulate-integration-adapters | falcon,resilience-routing | DRIFTED | Clamping a retry ladder's configuration inputs bounds how long the ladder can stall the pipeline. | →2026-08-26_1001_falcon-spotlight-transport-retry
L-9ad5a354 | 2026-08-26 | cymulate-integration-adapters | falcon,memory-bound | UNTESTED | Falcon's per-batch memory cost, and the degree-1 rollback's memory profile, are known well enough to trade against. | →2026-08-26_1001_falcon-spotlight-transport-retry
L-d8b7fc3a | 2026-08-26 | cymulate-integration-adapters | falcon,streaming-transport | UNTESTED | The production EOF that killed correlation 6a85ca2038f164746a562020 originated on the Spotlight response-body read inside FetchAsync. | →2026-08-26_1001_falcon-spotlight-transport-retry
L-6f1353b8 | 2026-08-27 | IntegrationServiceBus | integration-service-bus,checkpoint-resume | REFUTED | A collector's CanResumeFrom consumes only the adapter_state blob and never ISB's flat checkpoint columns. | →2026-08-27_1153_failed-checkpoint-retention-and-retry
L-9d08e415 | 2026-08-27 | IntegrationServiceBus | integration-service-bus,cross-repo-contract | REFUTED | AdapterDoneMessage lives in the Cymulate.Integration.Client package, so adding a field to it is a coordinated cross-repo package release. | →2026-08-27_1153_failed-checkpoint-retention-and-retry
L-70bf7cf8 | 2026-08-27 | IntegrationServiceBus | integration-service-bus,checkpoint-resume | REFUTED | The collector checkpoint-helper pattern is uniform, so a per-request staleness bound has one shape to change. | →2026-08-27_1153_failed-checkpoint-retention-and-retry
L-16f8c597 | 2026-08-27 | IntegrationServiceBus | integration-service-bus,checkpoint-resume | REFUTED | UnservableTerminalAge is CheckpointTtl * 0.75, so the sweep's give-up bound scales with the TTL config key. | →2026-08-27_1153_failed-checkpoint-retention-and-retry
L-9c94d22d | 2026-08-27 | IntegrationServiceBus | cymulate-integration-adapters,checkpoint-resume | REFUTED | The Tenable.io collector has no checkpoint/resume path. | →2026-08-27_1153_failed-checkpoint-retention-and-retry | supersedes:L-57acd253
L-b1b66b36 | 2026-08-27 | IntegrationServiceBus | integration-service-bus,cross-repo-contract | UNTESTED | Cymulate.Integration.Client carries 33 stale copies of files ISB deleted and is missing the SiemRules delta, i.e. the package has drifted from source in both directions. | →2026-08-27_1153_failed-checkpoint-retention-and-retry
L-f80a1c15 | 2026-08-27 | IntegrationServiceBus | integration-service-bus,checkpoint-resume | DRIFTED | Readability judgment lives entirely in the collector and state lives entirely in ISB, so the three-repo decomposition is clean. | →2026-08-27_1153_failed-checkpoint-retention-and-retry
L-d13589c6 | 2026-08-27 | IntegrationServiceBus | integration-service-bus,checkpoint-resume | REFUTED | Retaining a failed run's checkpoint instead of deleting it is a storage change with no lifecycle consequences. | →2026-08-27_1245_failed-checkpoint-retention-ttl-column
L-716f3c62 | 2026-08-27 | IntegrationServiceBus | integration-service-bus,checkpoint-resume | REFUTED | A recovery sweep predicate and a TTL cleanup predicate over the same column should agree; their disagreement is a smell. | →2026-08-27_1245_failed-checkpoint-retention-ttl-column
L-44a4039e | 2026-08-27 | IntegrationServiceBus | integration-service-bus,persisted-state | REFUTED | Extending how long an existing row is retained is a lifecycle change with no security dimension. | →2026-08-27_1245_failed-checkpoint-retention-ttl-column
L-6bfcda78 | 2026-08-27 | IntegrationServiceBus | integration-service-bus,checkpoint-resume | REFUTED | A retention hold on a dead run should survive whatever later writes touch the row. | →2026-08-27_1245_failed-checkpoint-retention-ttl-column
L-5d94327f | 2026-08-27 | IntegrationServiceBus | integration-service-bus,checkpoint-resume | REFUTED | The retention branch in CompleteExecutionAsync is reached by every collector-reported failure. | →2026-08-27_1245_failed-checkpoint-retention-ttl-column
L-910c8f52 | 2026-08-27 | IntegrationServiceBus | integration-service-bus,persisted-state | UNTESTED | The retain_until_utc migration and every raw SQL statement added for it are correct. | →2026-08-27_1245_failed-checkpoint-retention-ttl-column
L-0a485b73 | 2026-08-27 | IntegrationServiceBus | integration-service-bus,persisted-state | DRIFTED | A second configuration key for the retention window should be wired into the cleanup job the way the existing TTL key is. | →2026-08-27_1245_failed-checkpoint-retention-ttl-column
L-9db99c35 | 2026-08-27 | IntegrationServiceBus | integration-service-bus,checkpoint-resume | REFUTED | Excluding a status from a recovery sweep's SELECT is enough to stop that sweep dispatching such a row. | →2026-08-27_1454_failed-checkpoint-terminal-status
L-5838490b | 2026-08-27 | IntegrationServiceBus | integration-service-bus,persisted-state | REFUTED | One cleanup predicate can serve two retention horizons if it branches on a status column. | →2026-08-27_1454_failed-checkpoint-terminal-status
L-bb0d01f6 | 2026-08-27 | IntegrationServiceBus | integration-service-bus,checkpoint-resume | REFUTED | A checkpoint row marked terminal is inert, so its updated_at_utc is a stable failure timestamp. | →2026-08-27_1454_failed-checkpoint-terminal-status
L-76bacf0b | 2026-08-27 | IntegrationServiceBus | integration-service-bus,persisted-state | REFUTED | Stripping secrets from a retained row gives an unconditional secret-free guarantee. | →2026-08-27_1454_failed-checkpoint-terminal-status
L-71758bac | 2026-08-27 | IntegrationServiceBus | integration-service-bus,checkpoint-resume | REFUTED | An in-memory repository used as the test double behaves equivalently to the Postgres one for lifecycle questions. | →2026-08-27_1454_failed-checkpoint-terminal-status
L-a561ce63 | 2026-08-27 | IntegrationServiceBus | integration-service-bus,persisted-state | UNTESTED | The Postgres raw SQL added for terminal-status retention is correct. | →2026-08-27_1454_failed-checkpoint-terminal-status
L-dc61d2c4 | 2026-08-27 | IntegrationServiceBus | integration-service-bus,checkpoint-resume | DRIFTED | The feature is three edits: mark the status, exclude it from the sweep, delete it on a longer cutoff. | →2026-08-27_1454_failed-checkpoint-terminal-status
L-42cc8a7b | 2026-08-27 | cymulate-integration-adapters | falcon,checkpoint-resume | REFUTED | The Falcon findings flow shares the assets flow's checkpoint-writer paths. | →2026-08-27_0817_falcon-policy-plane-restructure
L-1b6ee226 | 2026-08-27 | cymulate-integration-adapters | falcon,ingestion-guard | REFUTED | Classifying DataPipelineException as non-retryable is safe because the type means an over-ceiling refusal. | →2026-08-27_0817_falcon-policy-plane-restructure
L-49fe697c | 2026-08-27 | cymulate-integration-adapters | falcon,batching | UNTESTED | POST /devices/entities/devices/v2 accepts about 1000 ids per request. | →2026-08-27_0817_falcon-policy-plane-restructure
L-33ce251b | 2026-08-27 | cymulate-integration-adapters | falcon,vendor-error-shape | UNTESTED | A Falcon 404 always arrives as an HTTP 404 status line. | →2026-08-27_0817_falcon-policy-plane-restructure
L-cc7eaeb4 | 2026-08-27 | cymulate-integration-adapters | cymulate-exposure-analytics,policy-edge-identity | UNTESTED | Policy edge keys resolve against Exposure Analytics' asset match key, so policy-to-asset edges land. | →2026-08-27_0817_falcon-policy-plane-restructure
L-85e358c4 | 2026-08-27 | cymulate-integration-adapters | cymulate-integration-parsers,json-double-encoding | UNTESTED | rules[].value reaches Postgres as native JSON, so Exposure Analytics can read toggle and slider values. | →2026-08-27_0817_falcon-policy-plane-restructure
L-3671c4e8 | 2026-08-27 | cymulate-integration-adapters | falcon,memory-bound | UNTESTED | Falcon's per-batch and per-stage memory cost is known well enough to trade against. | →2026-08-27_0817_falcon-policy-plane-restructure
L-1a37307e | 2026-08-27 | cymulate-integration-adapters | falcon,emission-determinism | UNTESTED | Emission order does not affect identity for the staged policy plane artifacts. | →2026-08-27_0817_falcon-policy-plane-restructure
L-00f62476 | 2026-08-27 | cymulate-integration-adapters | falcon,vendor-field-presence | UNTESTED | device_policies.prevention.settings_hash exists and is populated on Falcon device entities. | →2026-08-27_0817_falcon-policy-plane-restructure
L-e872a8ca | 2026-08-27 | cymulate-integration-adapters | falcon,policy-sweep | UNTESTED | GET /policy/combined/prevention/v1 returns precedence and a default-policy indicator per policy. | →2026-08-27_0817_falcon-policy-plane-restructure
L-3aff9764 | 2026-08-27 | cymulate-integration-adapters | falcon,tenant-scale | UNTESTED | About 12 distinct prevention policies is representative for a large Falcon tenant. | →2026-08-27_0817_falcon-policy-plane-restructure
L-a8ddf024 | 2026-08-30 | IntegrationServiceBus | integration-service-bus,persisted-state | REFUTED | Raw SQL that reads correctly to reviewers parses correctly in Postgres. | →2026-08-30_1130_force-resume-on-operator-dispatch
L-0f0a6065 | 2026-08-30 | IntegrationServiceBus | integration-service-bus,tooling | REFUTED | A missing /var/run/docker.sock means no container runtime is available on the machine. | →2026-08-30_1130_force-resume-on-operator-dispatch
L-e9b2e21e | 2026-08-30 | IntegrationServiceBus | integration-service-bus,checkpoint-resume | REFUTED | A plain retry on the same correlation id is harmless when a retained checkpoint exists. | →2026-08-30_1130_force-resume-on-operator-dispatch
L-645966cc | 2026-08-30 | IntegrationServiceBus | integration-service-bus,cross-repo-contract | REFUTED | Adding an optional typed field to an inbound wire message is additive and cannot break existing producers. | →2026-08-30_1130_force-resume-on-operator-dispatch
L-7448468a | 2026-08-30 | IntegrationServiceBus | integration-service-bus,checkpoint-resume | UNTESTED | No collector re-checks staleness inside ResumeAsync or the load path it uses, so bypassing CanResumeFrom is sufficient to force a resume. | →2026-08-30_1130_force-resume-on-operator-dispatch
L-29828585 | 2026-08-30 | cymulate-integration-adapters | falcon,entity-ids | REFUTED | AidExtractor.ExtractAid can derive a unique key for a Discover asset that states no sensor AID. | →2026-08-30_1247_falcon-unmanaged-assets-never-lost
L-a6917e53 | 2026-08-30 | cymulate-integration-adapters | falcon,asset-coverage | REFUTED | Some unmanaged Discover assets already reach output under a derived AID, orphaned downstream. | →2026-08-30_1247_falcon-unmanaged-assets-never-lost
L-ee097772 | 2026-08-30 | cymulate-integration-adapters | falcon,vendor-field-presence | REFUTED | A Discover combined-id record key is about 65 characters long. | →2026-08-30_1247_falcon-unmanaged-assets-never-lost
L-fd80d22b | 2026-08-30 | cymulate-integration-adapters | falcon,asset-coverage | REFUTED | Falcon FQL last_seen_timestamp:>= returns every host, so a date-gated Discover scroll loses nothing. | →2026-08-30_1247_falcon-unmanaged-assets-never-lost
L-2186c9a2 | 2026-08-30 | cymulate-integration-adapters | cymulate-integration-parsers,spark | UNTESTED | An 89-character combined id is safe in parser output columns that have only ever carried 32-character AIDs. | →2026-08-30_1247_falcon-unmanaged-assets-never-lost
L-35b88c57 | 2026-08-30 | cymulate-integration-adapters | falcon,checkpoint-resume | UNTESTED | The Falcon checkpoint's boundary-key list has a ceiling that fails visibly if it grows. | →2026-08-30_1247_falcon-unmanaged-assets-never-lost
L-76ed5c8b | 2026-08-30 | cymulate-integration-adapters | falcon,vendor-error-shape | UNTESTED | The Discover cursor leg observes a Falcon 404 as an HTTP 404 status line, so its re-anchor arm fires on an expired cursor. | →2026-08-30_1247_falcon-unmanaged-assets-never-lost
L-cc8f607b | 2026-09-03 | adapter-data-normalizer | falcon,asset-coverage | REFUTED | Falcon findings never arrive without a matching asset in the same collection batch. | →2026-09-03_1031_thin-ingest-normalizer-prototype
L-ca4fa812 | 2026-09-03 | adapter-data-normalizer | cymulate-exposure-analytics,entity-ids | DRIFTED | A derived row id scoped per (instance_id, vendor parent key) will exercise Exposure Analytics' generation model. | →2026-09-03_1031_thin-ingest-normalizer-prototype
L-b98fca67 | 2026-09-03 | adapter-data-normalizer | cymulate-exposure-analytics,exposure-identity | DRIFTED | Keying an exposure row on (host, CVE) is sufficient because that is what the target table can hold. | →2026-09-03_1031_thin-ingest-normalizer-prototype
L-1e7de7bb | 2026-09-03 | adapter-data-normalizer | cymulate-integration-parsers,second-vendor-parity | DRIFTED | The S3 Tenable samples are enough to sanity-check that a second vendor shape fits the same normalizer with no code change. | →2026-09-03_1031_thin-ingest-normalizer-prototype
L-45043e2e | 2026-09-03 | adapter-data-normalizer | cymulate-exposure-analytics,policy-edge-identity | UNTESTED | Policy edge keys resolve against Exposure Analytics' asset match key. | →2026-09-03_1031_thin-ingest-normalizer-prototype
L-6f6ccb67 | 2026-09-03 | adapter-data-normalizer | cymulate-integration-parsers,json-double-encoding | UNTESTED | A vendor nested value reaches Postgres as native JSON rather than a double-encoded string in every column that carries one. | →2026-09-03_1031_thin-ingest-normalizer-prototype
L-aa0427b1 | 2026-09-03 | adapter-data-normalizer | cymulate-exposure-analytics,schema-currency | UNTESTED | The committed pg_catalog schema dumps are current enough to generate a target contract against. | →2026-09-03_1031_thin-ingest-normalizer-prototype
L-f88b5876 | 2026-09-03 | adapter-data-normalizer | falcon,tenant-scale | UNTESTED | A ~300 host and ~100k finding lab collection is short enough to re-run repeatedly during development. | →2026-09-03_1031_thin-ingest-normalizer-prototype
L-05f182bf | 2026-09-03 | adapter-data-normalizer | integration-service-bus,persisted-state | UNTESTED | Raw SQL that reads correctly to a reviewer parses correctly in Postgres. | →2026-09-03_1031_thin-ingest-normalizer-prototype
L-88abb13f | 2026-09-03 | adapter-data-normalizer | cymulate-integration-parsers,spark | UNTESTED | A widened identity value reaching parser output columns lands correctly in the downstream match key that consumes it. | →2026-09-03_1031_thin-ingest-normalizer-prototype
L-ebe1d09c | 2026-09-06 | AILedger | ailedger-kernel,persisted-state | REFUTED | Adding a new state aggregate to an event-sourced kernel is a storage change with no lifecycle consequences for replay, projections or context assembly. | →2026-09-06_0511_close-conceptual-gaps-against-dossier
L-cd74b5ae | 2026-09-06 | AILedger | ailedger-kernel,lifecycle-semantics | REFUTED | A work item in a terminal status is inert, so nothing later mutates it. | →2026-09-06_0511_close-conceptual-gaps-against-dossier
L-55212e51 | 2026-09-06 | AILedger | ailedger-kernel,lifecycle-semantics | REFUTED | One command and one predicate can serve two different work-item outcomes by branching on a status column. | →2026-09-06_0511_close-conceptual-gaps-against-dossier
L-9c279838 | 2026-09-06 | AILedger | ailedger-kernel,persisted-state | REFUTED | Adding a field to an existing record in this kernel is contained to the command handler, the reducer and the state model. | →2026-09-06_0615_close-claim-supersession-and-challenge-consequence | supersedes:L-ebe1d09c
L-f71030fb | 2026-09-06 | AILedger | ailedger-kernel,lifecycle-semantics | REFUTED | A record in a terminal status is inert, so nothing later mutates it. | →2026-09-06_0615_close-claim-supersession-and-challenge-consequence | supersedes:L-cd74b5ae
L-2e173204 | 2026-09-06 | AILedger | ailedger-kernel,lifecycle-semantics | REFUTED | A supported challenge's consequence can be expressed entirely through existing events, with no new event type. | →2026-09-06_0615_close-claim-supersession-and-challenge-consequence
L-d48fc80f | 2026-09-06 | AdapterDataNormalizer | cymulate-integration-adapters,collector-contract-drift | REFUTED | A working prototype's vendor-to-target field mapping reflects how the current collector emits that vendor's data. | →2026-09-03_1726_mapping-stage-target-rows
L-c22b391c | 2026-09-06 | AdapterDataNormalizer | cybi-db-models,schema-snapshot-currency | REFUTED | A committed pg_catalog schema snapshot can settle whether a column exists on that cluster. | →2026-09-03_1726_mapping-stage-target-rows
L-1e5f81a2 | 2026-09-06 | AdapterDataNormalizer | cymulate-exposure-analytics,enum-normalization-locus | REFUTED | A parser writing a value that is not a member of the target Postgres enum means those rows are being dropped in production. | →2026-09-03_1726_mapping-stage-target-rows
L-42014f05 | 2026-09-06 | AdapterDataNormalizer | cymulate-integration-parsers,default-value-divergence | REFUTED | Aligning a new normalizer to the production parser column by column captures every material divergence from it. | →2026-09-03_1726_mapping-stage-target-rows
L-b12bf36f | 2026-09-06 | AdapterDataNormalizer | adapter-data-normalizer,content-hash-array-order | REFUTED | Vendor array ordering does not reach a content hash composed from mapped target columns. | →2026-09-03_1726_mapping-stage-target-rows
L-fed731cb | 2026-09-06 | AdapterDataNormalizer | adapter-data-normalizer,timestamp-zone-dependence | DRIFTED | Deriving a row id from mapped content is enough to make the id reproducible for the same input bytes. | →2026-09-03_1726_mapping-stage-target-rows
L-7d67fe96 | 2026-09-06 | AdapterDataNormalizer | adapter-data-normalizer,manifest-observed-limits | UNTESTED | A field absent from an observed lane manifest means the vendor does not supply that field. | →2026-09-03_1726_mapping-stage-target-rows
L-ae844bb4 | 2026-09-06 | AdapterDataNormalizer | cymulate-exposure-analytics,entity-ids | UNTESTED | An instance-scoped derived row id will exercise Exposure Analytics' latest=false generation model. | →2026-09-03_1726_mapping-stage-target-rows
L-8780d7a5 | 2026-09-06 | AdapterDataNormalizer | cymulate-integration-parsers,json-double-encoding | UNTESTED | A vendor nested value reaches Postgres as native JSON rather than a double-encoded string. | →2026-09-03_1726_mapping-stage-target-rows
L-192cd3c9 | 2026-09-06 | AdapterDataNormalizer | cymulate-exposure-analytics,downstream-untested | UNTESTED | A normalizer that emits target-shaped rows has thereby shown those rows survive the columns, match keys and joins waiting downstream. | →2026-09-03_1726_mapping-stage-target-rows
