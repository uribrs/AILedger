Role:
You are a senior distributed-systems engineer performing read-only reconnaissance across four
repositories that together implement Cymulate's collector checkpoint/resume machinery.

Goal:
Produce a cited, evidence-graded map of what exists today, sufficient for a later
implementation decision about retaining failed collectors' checkpoints for one week and
servicing an operator-initiated retry that resumes from them. Recon only — no code changes.

Context:
- ISB (`/Users/user/Dev/IntegrationServiceBus`) persists `adapter_checkpoints` in Postgres,
  runs `CheckpointRecoveryJob` / `CheckpointRecoveryHandler` (claim-driven recovery sweep) and
  `CheckpointCleanupJob` (TTL deletion), and enters resume via
  `ProcessEventCommandHandler.ExecuteWithResumeAsync`.
- `cymulate-integration-adapters` holds ~15 collectors, each with a `Recovery/` folder
  containing a checkpoint helper, a state type, and serializer/deserializer.
- `IntegrationInfra` holds `RecoveryParsingHelper` (shared staleness threshold) and ships the
  `Cymulate.Integration.Client` contracts package that ISB and the collectors both bind to.
- The backend/platform repo that triggers collections must be identified from evidence.
- The real resume state is the `adapter_state` JSON blob, adapter-owned and adapter-read.
  ISB's flat columns (`current_page`, `processed_items`, `sequence_id`) are ISB bookkeeping.

Constraints:
* Recon only. No product code changes, no branches, no commits, in any repo.
* No questions to the user. Record ambiguity as an OPEN assumption.
* ISB is a service hub, not a decision hub. Do not design ISB-side retryability judgment,
  failure classification, or advisability logic.
* Retryability judgment belongs to the collector; the retry decision belongs to the backend.
* Every claim carries a `repo/path:line` citation, or is labelled speculation explicitly.
* Read-only against `cymulate-integration-adapters`, `IntegrationInfra`, and any backend repo.
* Re-derive each OPEN assumption from source; do not carry prior-conversation claims forward.
* State cross-repo release cost wherever a change would require a package release.
* Report the trigger owner from evidence; if none is present on this machine, say so.

Success Criteria:
* Every assumption A1–A15 in `assumptions.md` is disposed with an actor and a citation.
* The ISB checkpoint lifecycle is mapped end to end: write, retention, recovery selection,
  cleanup selection, resume entry — each with a citation.
* The collector resume-state landscape is enumerated across all collectors, stating how many
  follow the shared helper pattern and naming every deviation.
* Every `IsCheckpointStale` call site is enumerated, with whether it overrides the threshold.
* The staleness parameterization surface is described concretely: what would have to change,
  in which repo, and what package release that implies.
* The collection trigger owner is named from evidence, along with the inbound message/API a
  retry request would reuse or extend.
* `AdapterDoneMessage`'s definition site and consumer set are identified, with the cost of
  adding a field.
* The minimal ISB-side change is stated under the service-hub constraint, separating what ISB
  must hold from what it must merely service.
* Per-repo obligations are split, with sequencing dependencies made explicit.

Execution Rules:
* Do not assume missing data
* Respect constraints strictly
* Prefer reading source over inferring from naming
* Grade every finding against the evidence hierarchy; label speculation as speculation
* When a prior-conversation claim is contradicted by source, report the contradiction

Output Format:
A recon report with these sections:
1. ISB checkpoint lifecycle (write → retain → recover → clean up → resume), cited
2. Collector resume-state landscape (uniformity, deviations, decline taxonomy), cited
3. Shared staleness threshold: definition, every call site, parameterization surface, cited
4. Trigger ownership and the retry request path, cited
5. Wire contract surface and cross-repo release cost, cited
6. Minimal ISB change under the service-hub constraint
7. Per-repo obligation split with sequencing dependencies
8. Assumption disposition table (A1–A15 plus prior-art entries), each with actor and citation
9. Open questions and contradictions found

Stop Conditions:
* When the goal is achieved
* When required data is missing
* When a repo required for a section is absent from this machine — report the gap, continue
  with the remaining sections
