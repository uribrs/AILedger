# Task 6 pilot: prepared findings and pending live acceptance

Date: 2026-09-28. Source base: `cd3f2d9` on `codex/structured-findings-contract`.
**Status: prepared-data acceptance passed; bounded live trial and operator delivery decision pending.**
Task 6 is not complete. Tasks 7–8 remain conditional and unimplemented.

## Evidence and reproduction

The [pilot tool](../../tools/FindingsPilot/README.md) contains the commands and exact timing boundaries.
[Retained results](task-6-prepared-results.json) contain every final trial, not selected best cases.
The [RN1 manifest](../../tools/FindingsPilot/Fixtures/RN1/manifest.json) identifies original files by
SHA256, task/run/actor, source lines and transcript/tool identities. Historical files were read
without the CLI state reader and hashed unchanged. Task-1's frozen fixtures are separate and unchanged.

RN1's first prepared script at provider event sequence 336 contained twenty claims and twenty
evidence records. Its filing window starts at `2026-09-24T11:55:19.886724+00:00` and ends at the last
successful tool result, `2026-09-24T11:58:37.78922+00:00`: **197.902496 seconds**, with **three denied
attempts and eleven successful tool calls**, producing forty canonical events (lines 36–75).
The later denied status read is outside that filing window; it is not counted as a filing attempt.

The accepted canonical wording differs from the first prepared script in **14 statements, two
consequences, twenty citations and seventeen summaries**. Both versions and the exact
[field differences](../../tools/FindingsPilot/Fixtures/RN1/text-differences.json) are preserved.
Several citations were shortened/reformatted. These 53 changed fields do not prove 53 lost facts,
that the shell caused every change, or that the claims are currently true. The typed pilot preserves
each version exactly when that version is supplied; it does not silently substitute accepted prose
for the original prepared dataset.

## Measured recording

Twenty-one self-checking trials passed. All final datasets contain exactly twenty open claims and
twenty evidence records, with exact statements, consequences, source types, citations, summaries,
support/refute directions and researcher attribution. No provider ran in these trials; run/token
populations remain absent, not fabricated zeros or copied historical provider costs.

| Prepared RN1 layout | Repetitions | Calls / commits / granular events | Recording wall ms: min / median / max |
|---|---:|---|---|
| Four early batches of five findings + five evidence | 7 | 4 / 4 / 40 | 134.577 / 203.183 / 463.578 |
| One batch of twenty findings + twenty evidence | 7 | 1 / 1 / 40 | 46.515 / 77.353 / 85.069 |

The historical accepted-text dataset also passed a four-batch trial. Every incremental scenario
checks that the first complete batch is readable before submitting the rest. All inputs were already
prepared; this is not a live observation of a researcher choosing good recording boundaries.

**Observed local recording elapsed time is lower than the historical filing window.** This is a
comparison of unlike observation boundaries, not a causal estimate of saved agent time. The local
MCP run excludes model turns, shell-tool scheduling and provider/relay startup; it includes durable
storage and protocol/journal work. Wall timing also includes intervening harness writes/checkpoints.
The historical interval includes model scheduling, retries and changing shell submission shapes.
No controlled shell arm, randomized order or isolated machine load exists. Final full-suite checks
were running concurrently. Do not claim a percentage whole-workflow speedup or better cognition.

The single-batch case is a prepared-upload comparator, not advice to accumulate a whole research run
before recording. Four small batches provide earlier durable output at a modest observed local cost.

## Recovery and guardrails

All six scenarios end with **four canonical transactions and forty granular events**, with exact
payload/reference fidelity and no duplicate or rejected-prefix writes. Recovery milliseconds below
span the controlled failing/conflicting attempt through successful recovery and harness checks;
they are not model recovery times. Counts are harness calls and independently read journal populations.

| Scenario | Harness calls | Observed tool / application attempts | Recovery ms | Coverage gaps |
|---|---:|---|---:|---:|
| lost-response | 5 | 5 / 5 | 42.157 | 0 |
| missing-reference | 5 | 5 / 5 | 46.869 | 0 |
| invalid-local-reference | 5 | 5 / 4 | 20.132 | 0 |
| revoked-capability | 5 | 5 / 5 | 61.233 | 0 |
| changed-key-content | 6 | 6 / 6 | 27.781 | 1 |
| journals-unavailable | 5 | unavailable / unavailable | 48.412 | 6 |

- Lost response: the output stream fails after commitment. A new server/recorder returns the same
  receipt on stable-body/key/binding retry; event bytes remain unchanged. The original failed
  delivery observation remains visible. This is an injected response loss, not a machine crash.
- Missing existing reference: a late evidence item names an absent claim after valid candidate
  findings. Kernel refusal leaves no canonical prefix. Correcting the reference permits commitment.
  The ordinary retrospective includes one original `AddEvidenceCommand` service refusal.
- Invalid local reference: strict request validation rejects before application entry. Five tool
  observations and four application observations are the correct distinct populations.
- Revoked capability: the fixture host removes AddEvidence while retaining AddClaim. The mixed
  request cannot commit its claim prefix. Restoring the original fixture grant permits the same
  batch. This proves authority enforcement, not a recommendation to broaden real actor grants.
- Changed content under a committed key: conflict preserves the old transaction and text; retrying
  the original body recovers it. The existing report retains one explicit gap for the conflicting
  attempt's unmatched fingerprint/transaction observation. It does not claim a new commit.
- Unavailable journals: both collectors are blocked and the first response is lost. Recovery still
  succeeds. Tool/application counts remain unavailable; six coverage gaps retain the missing
  journals and unmatched application observations. The harness's five calls are separately known.

The ordinary retrospective CLI, canonical group reader and findings measurement reader supply the
reports. No measurement reducer, refusal grouping, scoring rubric or production implementation changed.
No journal has a completeness watermark; zero detected gaps is not a complete-census claim.
Local response write/flush is not acknowledgement by an external client.

A historical useful-guardrail sample was also checked read-only: Falcon `refusals.jsonl` line 1
refused resolving C1 using LE8, which supports LC8. It named LE13 as supporting C1. Event line 103
records resolution using LE13 **3.850126 seconds** later, by `falcon-recon-synthesis`.
That preserved directional/reference integrity; it does not prove discovery of a false substantive
claim. This actor detail comes from the records; the prior investigation described it as the operator.

RN1's three historical provider shell denials prevented the submitted shell actions from running;
all forty records were subsequently accepted through literal commands. Whether those shell restrictions
were unnecessary at their broader execution boundary remains uncertain. The typed tool removes shell
construction from this recording operation while retaining its own grant, request and kernel checks.
A refusal count alone is not a guardrail-quality score. Infrastructure alternatives filing and S3's
hook-related wording change remain separate observations in the original investigation, not outcomes
retested or fixed by this pilot.

## Delivery recommendation and next-slice boundaries

**Recommendation:** retain `record_findings` as the preferred supplied claim/evidence recording path,
continue small submissions, and complete the bounded live source-inspection trial before marking task
6 complete. The controlled evidence supports reliable local recording and preserved information;
it does not settle provider ergonomics on the proposed new task or the episodic architecture.

The prepared live plan is `/tmp/ailedger-task6-live-prepared/trial-plan.json`: one Codex episode,
240 seconds maximum, six source snapshots, early recording, an expected missing-reference refusal,
corrected later recording and identical retry. Preparation ran; execution awaits explicit user approval.
It must use the production launch and existing grants. This report must be extended with actual live
results, source-grounding/ordering review, provider usage and limitations, not silently call fixtures live.

Potential future operations remain separately scoped:

| Candidate | Coherent batch | Boundary that must remain explicit |
|---|---|---|
| Alternatives recording | Related rejected approaches with evidence and rationale | Who may record vs who may approve; current role restriction remains |
| Artifact submission | One immutable artifact and its identity/provenance | Actual existence/content identity, ownership and independent assurance |
| Claim disposition | Related explicit conclusions over cited evidence | Resolution capability, direction/freshness and the reasoning decision |
| Readiness inspection | One non-mutating snapshot of admission conditions | Observation is not authority to execute against later changed state |

The interface is sufficient for the present recording pilot. Recommend no additional operation in
this task. The operator still needs to review the pilot and choose the next slice (or none).
No task-7 approval, task-8 evaluation, YAML, stage/role-policy change, workflow retirement or migration
is implied by these results or by installing the verified existing release.
