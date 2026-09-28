> **Accepted 2026-09-28:** the operator accepted this delivery with its documented limitations
> and no extension for now. Task 6 is complete. The evaluation and pending-decision checkpoint
> below are preserved; the subsequent acceptance is appended at the end.

# Task 6 live pilot — results and proposed delivery decision

Date: 2026-09-28. Checkpoint: `acc8f26`; tested CLI and source snapshots: `cd3f2d9`.
**Technical evaluation is finished with the limitations below. Operator delivery acceptance remains
pending; task 6 is not yet closed.** The user's “just proceed” approved the proposed single billable
Codex episode, not this later delivery decision or tasks 7–8.

The [retained results](task-6-live-results.json) preserve both launch outcomes, canonical events,
full provider transcript, original completion, retrospective, plan and source hashes. The provider
sidecar's `effectiveArguments` field is omitted from that retained copy because it contains the
transient relay capability; the original sidecar's SHA256 is retained. Other provider fields and all
23 events are preserved. Source citations below refer to the six hashed snapshots, whose paths and
original source commit are in the plan. Original temporary artifacts remain unchanged.

## Launch outcomes and boundaries

The first attempt at `/tmp/ailedger-task6-live-prepared` failed when the outer host sandbox denied
`TcpListener.Bind`. Canonical history contains a failed run, no findings, no manifest-delivery hash
and no usage. There is no provider sidecar. The stack ends at `ProviderFindingsSession.Start` before
`adapter.RunAsync` in `ProviderLauncher.cs:155–174`; no model episode was started. The original
failure and retrospective are retained, including two missing-telemetry gaps and absent attempt
counts. Missing usage is not converted to zero tokens.

A fresh equivalent fixture at `/tmp/ailedger-task6-live-authorized` used the same CLI, goal, six source
hashes and 240-second limit, with host permission for the local relay socket. Its preparation records
`acc8f26` as the checkout checkpoint; the CLI still identifies `cd3f2d9`. Production provider composition,
researcher capabilities (`add-claim`, `add-evidence`, `build-context`), exact findings-tool grant and
provider `workspace-write` sandbox remained unchanged. No broader grant or automatic billable retry
was used. This one Codex episode completed with exit 0 in **65.823420 seconds** at the adapter boundary,
within its limit. Kernel run start to completion was **67.125943 seconds**. These boundaries differ.

## Transcript and canonical review

| Observation | Actual evidence and assessment |
|---|---|
| Early recording | Provider sequences 7–8 read the two layout snapshots; 9–10 submit and receive the first receipt; 12–13 read the remaining four sources. The first canonical commit precedes the later read start by 4.231765 seconds. |
| First finding | `RepositoryLayout.props:2–6` embeds the build checkout path; `RepositoryLayout.cs:9–19` reads the metadata and checks an absolute path, solution file and Git marker. Statement and evidence summary are supported. |
| Second finding | `TestEnvironment.cs:7–12` sets the process environment from `ContextBrief.CognitiveRoot`; `ContextBrief.cs:96–105` derives it from the recorded checkout; `CognitiveArtifactLoader.cs:60–66` gives a configured root precedence over that environment default. Statement and evidence summary are supported. |
| Expected refusal | Sequence 15 returns `kernel_refused`, `not_committed`, at `evidence[0]` for the deliberately missing claim. The refusal journal records `AddEvidenceCommand` at task version 9. The next successful batch advances directly to version 11: no rejected claim prefix. |
| Correction and replay | Sequences 18 and 20 return identical receipts, with `replayed:false` then `true`. Exact request comparison finds only the intended supports-reference change after refusal, and no change at all on replay. |
| Text and attribution | Both statements, source types, citations and summaries match canonical values exactly. Supporting references map to the correct generated claims; refutes remain empty. No consequence was supplied. All four events retain actor `researcher` and run `R1`; claims remain open. |

Transactions are `2398dc87e13847ee9fc23f93632266b4` (early, events 8–9) and
`05c781986df543e983501a738725a2ad` (later, events 10–11). Four observed tool calls and four application
attempts produced two canonical transactions and four granular events. Replay created none.
This was a **deliberate replay after observed success**, not lost-response recovery. The completed
prepared-data trial remains the evidence for injected response loss.

Semantic limits matter. The final answer's statement that an alternate cognitive root needs an
explicit override is an inference under the unchanged process default, not a demonstrated failing
test or proof that the environment cannot deliberately be changed. The snapshots do not establish
which test projects import the props file; the agent explicitly retained that uncertainty. These
findings describe source behavior, not a repository-wide runtime verification or a claim-resolution
verdict. The guardrail prevented a dangling evidence reference, not a false substantive assertion.

There was a bounded scope deviation: sequences 5–8 listed the disposable ledger and read its
`task.md`, `assumptions.md` and `decisions.md`, beyond the plan's source-only wording. They were task
projections, not additional code sources. No source edits, extra findings calls, CLI writes or agent
dispatch appear in the transcript, and all six source hashes are unchanged. Do not label this strict
snapshot-only compliance. It does not invalidate the cited findings or the recorded read order.

Two item-level provider error messages at sequences 1–2 report an unknown `remote_computer_use`
feature requirement. The provider wrapper marks `isError:false`, stderr is empty, and the episode
completes. Both messages are retained, not silently classified as findings refusals. All three shell
commands exit 0; no permission/search denial appears in this retained transcript.

## Actual measurements

| Call | Host transport ms | Application ms | Result |
|---|---:|---:|---|
| Early submission | 72.4554 | 45.7873 | Committed |
| Deliberate missing reference | 45.1419 | 43.2798 | Refused, no commit |
| Corrected submission | 19.5226 | 18.1082 | Committed |
| Identical replay | 48.8011 | 42.8499 | Original receipt |

The four sequential host intervals total **185.9210 ms**; their nested application intervals total
**150.0252 ms**. Never add those totals together or add them to provider elapsed time. First host-call
start to last local response spans **24.978397 seconds**, including intervening source inspection,
model activity and scheduling. Refusal host start to corrected local response spans **6.830257
seconds**; the provider's refusal-completion to corrected-completion interval is **6.780885 seconds**.
Neither is isolated model reasoning time. The original completion reports **27,118 ms to first ledger
write**, measured from canonical run start, not from the first findings call.

The existing retrospective counts usage **once for one successful run**: **29,410 uncached input**,
**281,344 cache-read**, **0 cache-write**, and **2,297 output tokens**. The terminal provider event reports
310,754 total input tokens, consistent with those input buckets. Its 782 reasoning-output tokens
remain in the raw record and are not added to output tokens. No currency cost is inferred.
Requested/served model and measured model-turn count remain absent; counting `turn.completed`
events would change the existing instrument's meaning. The first failed fixture remains a separate
unmeasured run, not a second charged episode or a source of fabricated zeros.

Session `01a0e8f9-b3aa-7d61-8e80-6485f15cea39` agrees between the sidecar and completion and is joined
to attempts. Immutable transport provider-session fields remain absent. Manifest hash
`d3917c6b6250ac2f9b44fef12abc9fc5922394b78cf0730439eadede958f2dbd` and artifact count 8 are preserved.
Truncated lines are 0, timeout termination is false, and the successful report detects no coverage
gaps. These best-effort journals still have no completeness watermark. Local response `written`
is not client acknowledgement; this episode additionally retains provider-side receipt results.
Exact protocol negotiation and isolated model time remain unobserved.

The earlier prepared-data results show lower local recording elapsed time than the historical RN1
filing window, under unlike measurement boundaries. This live episode supplies no matched shell arm,
so it adds no causal speedup estimate. It demonstrates early recording and reference-error correction
in one narrow, explicitly scripted source task; it does not establish unprompted batching habits,
whole-workflow improvement or better cognition.

## Validation and delivery recommendation

The original live harness passed. Additional read-only checks passed for exact payload fidelity,
reference-only correction, identical replay body/receipt, canonical attribution/counts, transcript
ordering, all source/goal/CLI hashes, original usage fields and their measured populations, session
joins, refusal version, and equality of every default retrospective field with the opt-in report.
All 11 frozen fixture hashes were verified before edits and at final validation. Earlier prepared
results, validation history and frozen expected outputs are unchanged.

Only retained evidence and task-6 documentation/status changed in this continuation. No implementation
or test code changed, so the previously passing 1,901 main and 99 Memory tests in both full runners
were not repeated. JSON/provenance integrity, local document links and `git diff --check` were checked
for this change. No reinstall, merge, publication, historical migration or global change occurred.
A first attempt to calculate review intervals hit the local Python version's variable-fraction ISO
parsing limitation; normalizing fractional precision fixed the external review script. It wrote no
result before failure and caused no provider retry or change to original timestamps.

**Recommendation for the operator:** accept the current findings interface as the first delivery,
with these limitations, and choose **no additional operation for now**. The prepared reliability
matrix plus this live source review support the recording path. Keep small submissions and the current
validation/authority boundaries. The source-only deviation and configuration warnings are residual
observations, not reasons to broaden grants or silently declare a cleaner run.

The alternative is to hold delivery acceptance for a separately authorized stricter scope-compliance
trial. That would address instruction-following confidence, not a demonstrated loss or duplication
in this interface. No further billable run is proposed as part of the current authorization.

The operator decision remains **pending**. Accepting this recommendation can close task 6 without
starting a new operation. Alternatives recording, artifact submission, claim disposition and readiness
inspection remain individually scoped future choices described in the prepared report. Tasks 7–8,
episodic architecture changes and migration require their own decisions.


## Operator acceptance — 2026-09-28

After reviewing the recommendation and the explanation of the exercise's purpose, the operator
replied “accepted” and “proceed”. This accepts the current structured findings interface as the
first delivery, including the limitations above, and selects no extension for now. **Task 6 and the
first delivery (tasks 1–6) are complete.**

Acceptance does not change the observations: source-only compliance was imperfect, provider
warnings and the initial pre-provider failure remain recorded, and no causal workflow-speed or
cognition claim is established. The retained JSON is the original evaluation snapshot; its pending
status describes the checkpoint before this decision, not the current delivery status.

This closeout updates documentation and backlog status only. Earlier measurements, transcript,
canonical evidence and validation records remain unchanged. Document links and Git whitespace
were checked; no implementation changed and no test suite or billable episode was rerun.
No new operation, task-7/8 work, installation, merge, publication or migration follows from this
acceptance. Any such work needs a separately defined request.
