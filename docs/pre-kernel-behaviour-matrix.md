# Pre-kernel behaviour matrix

## Decision

The kernel preserves the old workflow's planning brain and makes its central truth, evidence,
provenance, scope, and verification controls substantially stronger. It did not replace the whole
operating system around those skills. The regressions are concentrated in unattended close-out,
fleet hygiene, verifier baselining, lesson completeness, human browsing, and distribution.

The most defensible next action is to treat the `ABSENT` rows below as the migration inventory. This
document does not choose or design remedies.

## Frame and boundary

- Task type: migration or compatibility analysis.
- Documentation status: public official docs available — for this private system, the first-party
  local repositories and the installed CLI help are the authoritative published surfaces.
- A **behaviour** is one independently observable control that changes what an actor is instructed to
  do, what an executable does, what the kernel records, or what it refuses. Repeated explanations,
  examples, schemas, and output wording were folded into the control they support.
- The inventory contains **108 behaviours**. Coverage is by source, not by line count: `RULES.md`, all
  six old skills, all six executables, `lessons.md`, `global/engineering_principles.md`, the old task
  archive, all six rewritten skills, full `ailedger --help`, the current lesson store, task corpus,
  and the 48-row backlog were examined. Product `src` was not read.
- Verdict totals are **37 `BETTER`, 49 `EQUAL`, 7 `WORSE`, 15 `ABSENT`, 0 `DELIBERATE`**. No loss
  qualified as `DELIBERATE`: no accepted decision, rejected alternative, or minted lesson in the
  supplied ledger records a decision to drop it.
- Verdicts use only the contract vocabulary. `EQUAL` includes the same behavioural strength through a
  different mechanism. An instruction retained only as an instruction is never upgraded to `BETTER`.
- Confidence is high except where a row says **undetermined**. Local first-party code and prose agree
  on the old side; kernel claims are grounded in CLI help, rewritten rules/skills, live corpus shape,
  or an explicit backlog record.

## Regressions together

| Lost behaviour | What it protected or cost | Old evidence | Kernel evidence | Verdict |
|---|---|---|---|---|
| Preflight checked the ledger, minter, indexer, validator, hook, and host hook before work | Detected broken installation before recall or close-out silently degraded | `skills/workflow-coordinator/SKILL.md:63-128` | Rewritten coordinator has no preflight; `cognitive/skills/workflow-coordinator/SKILL.md:1-30` begins at kernel routing | `ABSENT` |
| Claude `Stop` hook ran outside the model and could block session termination | Made close-out fire even when the agent forgot; without it, stage arms only act when invoked | `closeout-hook.sh:1-23`; `skills/workflow-coordinator/SKILL.md:29-42` | `ailedger --help:110-115` guards requested transitions only; `Backlog/Priorities.md:20` says 24/43 tasks never transitioned | `ABSENT` |
| Stop gate examined only recently touched tasks | Prevented historic unfinished work from making the gate permanently noisy and disabled | `closeout-hook.sh:12-13,21`; `validate-closeout.py:199-213` | No automatic stop gate or recent-task command exists in `ailedger --help:7-78` | `ABSENT` |
| Fleet audit detected open tasks idle for seven days and escalated them in retrospective mode | Found work “finished in spirit” that could never mint a lesson | `validate-closeout.py:31-34,119-127,178-189` | `retrospective build` is one-task-at-a-time (`ailedger --help:14-18`); no fleet/staleness command is exposed | `ABSENT` |
| Validator scanned local active/done trees or the whole global archive with an optional date bound | Found evidence loss across the corpus rather than only on a task someone remembered to name | `validate-closeout.py:169-213` | `retrospective build` accepts one task id and exposes no archive scan (`ailedger --help:14-18`) | `ABSENT` |
| Contract time captured `git rev-parse HEAD` as `baseRef` | Gave the verifier a stable before/after boundary | `skills/prompt-contract-designer/SKILL.md:97-120` | `Backlog/Priorities.md:18` records that the kernel manifest has no such field and two verifier runs used fallback | `ABSENT` |
| Arbitrary operator-level global state was loaded before local task state | Let durable cross-task principles beyond lessons influence contracts | `skills/prompt-contract-designer/SKILL.md:48-55`; `global/engineering_principles.md:1-8` | Kernel designer consumes only the context manifest and recalled lessons (`cognitive/skills/prompt-contract-designer/SKILL.md:48-67`) | `ABSENT` |
| Light and full task tiers changed the required state-document footprint | Avoided full bookkeeping for smaller non-trivial tasks | `skills/prompt-contract-designer/SKILL.md:79-94` | Both rewritten tiers produce the PromptContract; “full” only optionally adds claims/decisions (`cognitive/skills/prompt-contract-designer/SKILL.md:71-80`) | `ABSENT` |
| Lesson correction distinguished `supersedes` from “invalid when minted” `retracts` | Preserved whether a conclusion changed or the original evidence was never valid | `lessons.md:31-45`; `mint-lesson.py:248-257,294-301` | Kernel exposes only `lesson mark --supersedes` (`ailedger --help:55-63,125-130`) | `ABSENT` |
| Minter warned when verifier lesson-bearing identifiers were not represented in submitted lessons | Caught the only silent undercount left after close-out | `mint-lesson.py:129-174,372-382` | Archive requires an eligible mark but help exposes no completeness comparison (`ailedger --help:110-130`) | `ABSENT` |
| Lesson minting had `--schema` and a write-free `--dry-run` | Let an agent validate complex lesson input and preview all side effects | `mint-lesson.py:15-18,328-404` | `lesson mark` has no schema or dry-run surface (`ailedger --help:55-70`) | `ABSENT` |
| A batch of lesson rows was fully validated before any row was written | Prevented half a close-out when one of several lessons was malformed | `mint-lesson.py:177-263,355-369,406-431` | Each mark is a separate mutation; no batch lesson command exists (`ailedger --help:55-63`) | `ABSENT` |
| A generated README summarized live/stale lessons, repositories, recent lessons, and archive counts | Kept an append-only ledger browsable without reading it whole | `build-readme.py:146-238` | The store is `lessons/lessons.jsonl`; CLI offers targeted `lesson recheck` but no index or aggregate lesson view (`ailedger --help:64-70,166-184`) | `ABSENT` |
| Release bundle creation packaged rules, skills, scripts, changelog, and install guide, warning on version mismatch | Made the workflow distributable as one checked handout | `build-bundle.sh:1-38` | No bundle/publish command exists in `ailedger --help:7-78`; rewritten skills assume a supplied cognitive root | `ABSENT` |
| Runtime installer deployed only owned skills and checked installed copies for drift | Supported Claude/Codex installation without erasing unrelated skills | `install.sh:1-87` | Cognitive files are read in place and hashed only after context build (`ailedger --help:20-25,84-99`); no install/check command exists | `ABSENT` |

## Complete matrix

### Rules and pipeline invariants

| Old behaviour | Old evidence | Kernel behaviour and evidence | Verdict |
|---|---|---|---|
| Challenge only material architecture, risk, security, scaling, assumption, or complexity issues | `RULES.md:9-28` | Same instruction is retained verbatim in `cognitive/RULES.md:9-28`; no kernel refusal judges challenge quality | `EQUAL` |
| Surface the best challenge once, then let the operator decide and do not re-litigate | `RULES.md:30-31` | Same rule remains in `cognitive/RULES.md:30-31`; challenges also have explicit raise/dispose records (`ailedger --help:39-41`) | `BETTER` |
| Prefer the simplest sufficient solution and reject unjustified abstraction | `RULES.md:33-37`; `global/engineering_principles.md:3,6` | Same instruction remains in `cognitive/RULES.md:33-37`; the kernel does not mechanically judge complexity | `EQUAL` |
| Communicate directly and avoid narrative preambles | `RULES.md:39-41` | Same instruction remains in `cognitive/RULES.md:39-41` | `EQUAL` |
| Rank evidence: official/source, implementation, operational evidence, community, labeled speculation | `RULES.md:47-58` | Same hierarchy remains in `cognitive/RULES.md:47-58`; claims and directional evidence are separately recorded (`ailedger --help:30-35`) | `BETTER` |
| Treat assumption status as evidence, require actor+citation, and never count completion as validation | `RULES.md:60-67` | Rewritten designer keeps the lifecycle (`cognitive/skills/prompt-contract-designer/SKILL.md:94-121`); the CLI refuses resolution without linked directional evidence by command contract (`ailedger --help:30-35`) | `BETTER` |
| Plan every non-trivial task before execution and enter through the coordinator | `RULES.md:69-82` | Same pipeline remains (`cognitive/RULES.md:69-82`); PromptContract/OrchestrationPlan are typed, producer-run artifacts (`ailedger --help:26-29`) | `BETTER` |
| Plan readiness requires goal, constraints, success criteria, and operator sign-off | `RULES.md:73-82` | Rewritten designer refuses an invalid contract and files a typed artifact (`cognitive/skills/prompt-contract-designer/SKILL.md:143-182`) | `BETTER` |
| Execute only the signed scope; do not expand or silently work around constraints | `RULES.md:84-96` | Same rule remains; work has named scope and multi-area work requires a recorded alternative (`ailedger --help:42-45,117-120`) | `BETTER` |
| Diagnose before changing; revert a change that creates more errors than it fixes | `RULES.md:88-92` | Same instruction remains in `cognitive/RULES.md:88-92`; no kernel rule can assess this outcome | `EQUAL` |
| Mandatory verifier follows execution | `RULES.md:93-95` | Work completion requires a working-role run plus a verifier run (`ailedger --help:106-108`) | `BETTER` |
| Mandatory isolated reviewer follows verification on code-bearing work | `RULES.md:93-95` | Still specified (`cognitive/RULES.md:93-95`), but completion can close the review route and the Learn arm accepts any completed reviewer run (`Backlog/Priorities.md:22,34`) | `WORSE` |
| Stop on ambiguity, missing criteria, contract violation, speculative impact, or lost goal | `RULES.md:137-148` | Same stop list remains in `cognitive/RULES.md:137-148`; enforcement depends on the specific governed record involved | `EQUAL` |
| Treat engineering time, cognitive load, operational complexity, and iteration as costs | `RULES.md:150-154` | Same rule remains in `cognitive/RULES.md:150-154`; per-run cost is recorded but coordinator cost is still unmeasured (`Backlog/Priorities.md:35,69`) | `EQUAL` |

### Coordination, contracts, and task state

| Old behaviour | Old evidence | Kernel behaviour and evidence | Verdict |
|---|---|---|---|
| Coordinator is a pure router with five bounded responsibilities | `skills/workflow-coordinator/SKILL.md:9-27` | Rewritten coordinator remains a pure router; authority is separated across actor roles and capabilities (`cognitive/skills/workflow-coordinator/SKILL.md:9-28`; `ailedger --help:101-104`) | `BETTER` |
| Non-trivial work flows designer → orchestrator → execution → verifier → reviewer → lesson/archive | `skills/workflow-coordinator/SKILL.md:44-59` | Same order remains in `cognitive/RULES.md:113-131`, now represented by runs, artifacts, marks, and stage transitions | `BETTER` |
| Preflight verifies every external script and host close-out integration | `skills/workflow-coordinator/SKILL.md:63-128` | No equivalent command or rewritten coordinator step (`cognitive/skills/workflow-coordinator/SKILL.md:1-30`; `ailedger --help:7-78`) | `ABSENT` |
| Never mutate the user's Claude settings; print the exact hook snippet | `skills/workflow-coordinator/SKILL.md:97-123` | Rewritten coordinator forbids all host configuration writes (`cognitive/skills/workflow-coordinator/SKILL.md:190-202`); it no longer prints/arms a hook | `EQUAL` |
| Continue the same work in one task directory and never create a duplicate | `skills/workflow-coordinator/SKILL.md:130-140` | Kernel supplies one canonical task path and owns lifecycle; actors may not create/move it (`cognitive/skills/prompt-contract-designer/SKILL.md:187-205`) | `BETTER` |
| Always run contract design for non-trivial work; revisions are incremental | `skills/workflow-coordinator/SKILL.md:142-155`; `skills/prompt-contract-designer/SKILL.md:402-409` | Same instruction remains; artifact revisions use a new id plus `--supersedes` (`cognitive/skills/prompt-contract-designer/SKILL.md:173-195`) | `BETTER` |
| Designer does not execute, research deeply, or write post-contract artifacts | `skills/prompt-contract-designer/SKILL.md:9-15,211-225,307-309` | Same ownership remains and capability-filtered runs make cross-role mutations refusible (`cognitive/skills/prompt-contract-designer/SKILL.md:125-139,192-195`) | `BETTER` |
| Missing correctness-critical information stops contract filing; lesser unknowns become explicit assumptions | `skills/prompt-contract-designer/SKILL.md:229-255` | Same behavior, with lesser unknowns becoming event-backed OPEN claims (`cognitive/skills/prompt-contract-designer/SKILL.md:143-159`) | `BETTER` |
| Clarification asks only directly unblocking questions, at most five, then resumes existing state | `skills/prompt-contract-designer/SKILL.md:413-424` | Same protocol remains (`cognitive/skills/prompt-contract-designer/SKILL.md:273-284`) | `EQUAL` |
| Prompt contract follows a fixed, execution-ready structure and cannot have empty constraints or success criteria | `skills/prompt-contract-designer/SKILL.md:247-257,388-399,428-459` | Same validation/structure remains (`cognitive/skills/prompt-contract-designer/SKILL.md:161-182,248-306`) | `EQUAL` |
| Preserve existing validated constraints and decisions on re-run | `skills/prompt-contract-designer/SKILL.md:48-55,402-409` | Manifest preserves event-backed records; replacement is explicit via supersession commands (`cognitive/skills/prompt-contract-designer/SKILL.md:48-53`; `ailedger --help:32-38,71-72`) | `BETTER` |
| Store only future-relevant constraints, decisions, evidence, lessons, and unresolved risks | `skills/prompt-contract-designer/SKILL.md:353-369` | Structured command types replace free-form mutable state (`ailedger --help:26-72`) | `BETTER` |
| Source conflict priority is current user, local state, global state, defaults | `skills/prompt-contract-designer/SKILL.md:372-385` | Rewritten order is current request, current manifest/artifacts, recalled lessons, defaults (`cognitive/skills/prompt-contract-designer/SKILL.md:232-245`) | `EQUAL` |
| `state.json` is mutable machine truth and every skill updates its owned fields | `skills/prompt-contract-designer/SKILL.md:97-138`; `skills/workflow-coordinator/SKILL.md:320-374` | Append-only event truth plus regenerated projections replaces shared mutation; projections must not be edited (`cognitive/skills/prompt-contract-designer/SKILL.md:83-90`) | `BETTER` |
| State/markdown conflict is a hard stop | `skills/prompt-contract-designer/SKILL.md:135-138`; `skills/workflow-coordinator/SKILL.md:372` | Manifest/current-artifact conflict remains a stop (`cognitive/skills/workflow-coordinator/SKILL.md:175-187`) | `EQUAL` |
| Capture contract-time `baseRef` for verifier diff scope | `skills/prompt-contract-designer/SKILL.md:105-120` | Missing from kernel manifest; verifier fallback has fired (`Backlog/Priorities.md:18`) | `ABSENT` |
| Use light/full tiers to vary required files | `skills/prompt-contract-designer/SKILL.md:79-94` | No meaningful governed tier distinction remains (`cognitive/skills/prompt-contract-designer/SKILL.md:71-80`) | `ABSENT` |
| Read arbitrary global operator state before local state | `skills/prompt-contract-designer/SKILL.md:48-55`; `global/engineering_principles.md:1-8` | Context contains governed records, selected skills/rules, and recalled lessons, not the old global directory (`cognitive/skills/prompt-contract-designer/SKILL.md:48-67`) | `ABSENT` |
| Mirror local task state into a cross-repository archive | `skills/prompt-contract-designer/SKILL.md:280-305`; `skills/workflow-coordinator/SKILL.md:279-318` | Kernel uses one canonical event-log directory and publishes lessons separately through `--lesson-root` (`ailedger --help:200-204`) | `BETTER` |
| Confirm verifier/reviewer outputs before lesson close-out | `skills/workflow-coordinator/SKILL.md:173-183,266-269` | Typed producer-run artifacts and completed role runs are queryable (`ailedger --help:9-13,26-29`), but reviewer content can still be a refusal (`Backlog/Priorities.md:22`) | `WORSE` |

### Recall and research

| Old behaviour | Old evidence | Kernel behaviour and evidence | Verdict |
|---|---|---|---|
| Derive 2–4 task tags, recall matching lessons, and never scan task directories | `skills/prompt-contract-designer/SKILL.md:59-75` | Task-open tags drive cross-repo store recall; untagged tasks receive recent lessons (`ailedger --help:193-204`) | `EQUAL` |
| Drop superseded/retracted heads, take newest ten, follow at most three archive pointers | `skills/prompt-contract-designer/SKILL.md:63-73`; `lessons.md:41-45` | Kernel head-filters/bounds before building context (`cognitive/skills/prompt-contract-designer/SKILL.md:58-67`); content is embedded, so pointers need not be opened. Retraction is the missing exception | `EQUAL` |
| Distinguish a changed conclusion from an invalidly minted lesson | `lessons.md:42-45` | Only supersession is exposed (`ailedger --help:125-130`) | `ABSENT` |
| Recalled lessons enter as OPEN, stale evidence, never fact | `skills/prompt-contract-designer/SKILL.md:67-75`; `lessons.md:20-21` | `claim add --from-lesson` preserves provenance and OPEN status; operator-approved `lesson recheck` can re-establish it (`ailedger --help:30-31,64-70`) | `BETTER` |
| Research only an external behavior or decision-changing question, centrally, before execution | `skills/task-orchestrator/SKILL.md:102-126` | Same routing remains (`cognitive/skills/task-orchestrator/SKILL.md:98-122`) | `EQUAL` |
| Research uses web first, official sources first, community for real-world behavior, and labels uncertainty | `skills/technical-researcher/SKILL.md:43-56,80-134` | Same rules remain (`cognitive/skills/technical-researcher/SKILL.md:44-57,81-135`) | `EQUAL` |
| State one docs-access status and distinguish not-found from inaccessible | `skills/technical-researcher/SKILL.md:53-56,80-90,161-169` | Same behavior remains (`cognitive/skills/technical-researcher/SKILL.md:53-57,81-90,162-170`) | `EQUAL` |
| Produce a recommendation, assumptions, confidence, sequencing, and implement-now/verify-first split | `skills/technical-researcher/SKILL.md:51-56,136-180` | Same output discipline remains (`cognitive/skills/technical-researcher/SKILL.md:52-57,137-180`) | `EQUAL` |
| Persist one topic file and update it in place | `skills/technical-researcher/SKILL.md:15-33` | Same governed path remains (`cognitive/skills/technical-researcher/SKILL.md:15-33`) | `EQUAL` |
| Resolve the triggering assumption where research lands | `skills/technical-researcher/SKILL.md:35-41` | Researcher records claim plus directional evidence but cannot resolve; an operator/lead must finish it (`cognitive/skills/technical-researcher/SKILL.md:35-42`) | `BETTER` |
| Every research finding cites the underlying source, not its own report | `skills/technical-researcher/SKILL.md:35-41,188-190` | Kernel requires separately linked evidence and the rewritten skill repeats the underlying-source rule (`cognitive/skills/technical-researcher/SKILL.md:35-40,189-191`) | `BETTER` |
| Research references supply task emphasis, source labels, and exact output template | `skills/technical-researcher/SKILL.md:78,93,146` | Those three referenced files are absent from both old and rewritten skill directories; the instruction could not actually run in either system | `EQUAL` |

### Planning, decomposition, execution, and verification

| Old behaviour | Old evidence | Kernel behaviour and evidence | Verdict |
|---|---|---|---|
| Treat the finalized contract as the sole problem statement | `skills/task-orchestrator/SKILL.md:19-38` | Current PromptContract artifact and manifest replace mutable files (`cognitive/skills/task-orchestrator/SKILL.md:13-35`) | `BETTER` |
| Run internal recon before path choice because separability depends on it | `skills/task-orchestrator/SKILL.md:128-166` | Same order remains (`cognitive/skills/task-orchestrator/SKILL.md:124-162`) | `EQUAL` |
| Read durable repo rules first and map only the subsystem delta | `skills/task-orchestrator/SKILL.md:137-140` | Same instruction remains (`cognitive/skills/task-orchestrator/SKILL.md:133-136`) | `EQUAL` |
| Recon returns a bounded map: sources, files, exemplars, shared surface, disjoint sets, landmines | `skills/task-orchestrator/SKILL.md:141-164` | Same required map remains (`cognitive/skills/task-orchestrator/SKILL.md:137-160`) | `EQUAL` |
| Classify contract sufficiency and at most three task classes, corrected by recon | `skills/task-orchestrator/SKILL.md:60-70` | Same gate remains (`cognitive/skills/task-orchestrator/SKILL.md:56-66`) | `EQUAL` |
| Trace every changed artifact one hop into its first existing consumer | `skills/task-orchestrator/SKILL.md:70-76` | Same design-invalidating interaction check remains (`cognitive/skills/task-orchestrator/SKILL.md:66-72`) | `EQUAL` |
| Record at most five material attention items with causal path and resolvable test/guard/research/accept handling | `skills/task-orchestrator/SKILL.md:77-95` | Same plan contract remains (`cognitive/skills/task-orchestrator/SKILL.md:73-91`) | `EQUAL` |
| Ask at most three research questions, each able to change a named decision | `skills/task-orchestrator/SKILL.md:86-90,102-126` | Same behavior remains (`cognitive/skills/task-orchestrator/SKILL.md:82-86,98-122`) | `EQUAL` |
| Score six path axes and record them before deciding | `skills/task-orchestrator/SKILL.md:96-100` | Same requirement remains (`cognitive/skills/task-orchestrator/SKILL.md:92-96`) | `EQUAL` |
| Default to decomposition; use direct only for a named overlapping file cluster | `skills/task-orchestrator/SKILL.md:179-200` | Same rule remains (`cognitive/skills/task-orchestrator/SKILL.md:175-196`) | `EQUAL` |
| Decompose across repositories, recon-proven disjoint sets, or separable tests/implementation | `skills/task-orchestrator/SKILL.md:181-189` | Same hard triggers remain (`cognitive/skills/task-orchestrator/SKILL.md:177-185`) | `EQUAL` |
| Freeze shared interfaces/types/contracts before consumers fan out | `skills/task-orchestrator/SKILL.md:202-208` | Same phase-zero rule remains (`cognitive/skills/task-orchestrator/SKILL.md:198-204`) | `EQUAL` |
| Give each worker an exclusive file set and prevent sibling overlap | `skills/task-orchestrator/SKILL.md:206-215,336-353` | Kernel occupies directory areas and refuses unrecorded multi-area consolidation (`ailedger --help:117-123`), though occupancy is task-local and coarse (`Backlog/Priorities.md:23`) | `BETTER` |
| Worker needing a missing shared artifact returns `BLOCKED:` instead of inventing it | `skills/task-orchestrator/SKILL.md:355-365` | Rewritten skill retains the escape hatch and kernel provides `work block/unblock` records (`ailedger --help:47-49`) | `BETTER` |
| Prefer fresh workers; resume exact context only for feedback on their own output | `skills/task-orchestrator/SKILL.md:217-227` | `provider resume` requires the exact session and named run (`ailedger --help:73-82`); policy remains in rewritten orchestrator | `BETTER` |
| Synthesize worker outputs centrally before verification | `skills/task-orchestrator/SKILL.md:325-388` | Same coordinating-run ownership remains (`cognitive/skills/task-orchestrator/SKILL.md:315-376`) | `EQUAL` |
| Reclassify and change path when execution exposes bad decomposition | `skills/task-orchestrator/SKILL.md:227` | Same behavior remains in `cognitive/skills/task-orchestrator/SKILL.md:223` | `EQUAL` |
| Executor reads contract, plan, recon, and research in order and stops on material OPEN assumptions | `skills/contract-driven-execution/SKILL.md:33-73` | Manifest and current typed artifacts replace mutable files; stop rule remains (`cognitive/skills/contract-driven-execution/SKILL.md:30-62`) | `BETTER` |
| Executor records evidence at the moment it is produced | `skills/contract-driven-execution/SKILL.md:79-90` | Claims and evidence become append-only CLI events (`cognitive/skills/contract-driven-execution/SKILL.md:64-74`) | `BETTER` |
| Preserve unrelated user changes | `skills/contract-driven-execution/SKILL.md:88` | Same instruction remains (`cognitive/skills/contract-driven-execution/SKILL.md:72`) | `EQUAL` |
| Executor writes execution notes but never performs its own verifier/reviewer pass | `skills/contract-driven-execution/SKILL.md:90-103,118-127` | Same boundary remains, but Worker/Researcher cannot file their document as an artifact (`Backlog/Priorities.md:21`) | `WORSE` |
| Verifier receives full context and checks the request, criteria, plan, artifacts, edge cases, and research alignment | `skills/task-orchestrator/SKILL.md:390-421` | Same full-context verifier run remains (`cognitive/skills/task-orchestrator/SKILL.md:380-411`) | `EQUAL` |
| Verifier compares what actually landed against `baseRef`, defaulting unsupported assumptions to NEVER-TESTED | `skills/task-orchestrator/SKILL.md:423-449` | Claim/evidence rules enforce the disposition, but missing `baseRef` forces artifact fallback (`Backlog/Priorities.md:18`; `ailedger --help:30-35`) | `WORSE` |
| Every attention item receives handled/accepted-risk/not-applicable/unresolved evidence | `skills/task-orchestrator/SKILL.md:451-484` | VerifierOutput validation reads planned attention identifiers, but does so task-wide rather than per work item (`Backlog/Priorities.md:46`) | `WORSE` |
| Repair verifier issues and create a new numbered review rather than overwrite history | `skills/task-orchestrator/SKILL.md:472-484` | Typed artifact revision requires a new id with `--supersedes`; files still increment (`ailedger --help:26-29`; rewritten `skills/code-reviewer/SKILL.md:41-57`) | `BETTER` |

### Independent code review

| Old behaviour | Old evidence | Kernel behaviour and evidence | Verdict |
|---|---|---|---|
| Reviewer receives only code, risk/type, stack, accepted tradeoffs, and output path | `skills/code-reviewer/SKILL.md:9-29` | Artifact kinds are filtered, but goal, claims, decisions, alternatives, evidence, and constraints still leak; a live reviewer refused (`Backlog/Priorities.md:18`) | `WORSE` |
| Reviewer assesses technical safety/idiom/maintainability, not request satisfaction | `skills/code-reviewer/SKILL.md:51-56` | Same boundary remains (`cognitive/skills/code-reviewer/SKILL.md:61-65`) | `EQUAL` |
| Calibrate review by change type, low/medium/high risk, and proportional depth | `skills/code-reviewer/SKILL.md:57-102` | Same calibration remains (`cognitive/skills/code-reviewer/SKILL.md:67-112`) | `EQUAL` |
| Prioritize hot paths, retries, polling, distributed coordination, serialization, network, scale, and persistence | `skills/code-reviewer/SKILL.md:103-117` | Same lenses remain (`cognitive/skills/code-reviewer/SKILL.md:113-127`) | `EQUAL` |
| Label confirmed issues, likely risks, possible concerns, and preferences | `skills/code-reviewer/SKILL.md:119-131` | Same evidence threshold remains (`cognitive/skills/code-reviewer/SKILL.md:129-141`) | `EQUAL` |
| Review language correctness, async/cancellation, exceptions, nullability, resources, thread safety, and idioms | `skills/code-reviewer/SKILL.md:133-175` | Same general lenses remain (`cognitive/skills/code-reviewer/SKILL.md:143-185`) | `EQUAL` |
| Load stack-specific lens files | `skills/code-reviewer/SKILL.md:148,175` | `references/csharp-lenses.md` is absent from both old and rewritten directories; this behavior was instructed but never supplied | `EQUAL` |
| Review control flow, responsibilities, invariants, edge cases, recoverability, and abstraction value | `skills/code-reviewer/SKILL.md:150-162` | Same logic lens remains (`cognitive/skills/code-reviewer/SKILL.md:160-172`) | `EQUAL` |
| Challenge poor collections, repeated scans, materialization, remote-call batching, unbounded concurrency, and O(n²) work | `skills/code-reviewer/SKILL.md:163-175` | Same algorithm/data-structure lens remains (`cognitive/skills/code-reviewer/SKILL.md:173-185`) | `EQUAL` |
| Review streaming/DOM choice, retry semantics, throttling, memory, and shared state | `skills/code-reviewer/SKILL.md:177-195` | Same runtime lens remains (`cognitive/skills/code-reviewer/SKILL.md:187-205`) | `EQUAL` |
| Recommend the smallest material fix; distinguish debt, tradeoff, negligence, and premature optimization | `skills/code-reviewer/SKILL.md:196-233,267-293` | Same proportionality and tradeoff rules remain (`cognitive/skills/code-reviewer/SKILL.md:206-243,277-303`) | `EQUAL` |
| Severity follows operational impact, not diff size; output uses Blocker/Major/Minor/Nit/Observation with fix shape | `skills/code-reviewer/SKILL.md:235-252,295-310` | Same severity/output contract remains (`cognitive/skills/code-reviewer/SKILL.md:245-262,305-320`) | `EQUAL` |
| Reviewer output is durable and revisioned | `skills/code-reviewer/SKILL.md:31-49` | It must also be filed from its active producer run as typed `CodeReviewOutput` (`cognitive/skills/code-reviewer/SKILL.md:31-57`) | `BETTER` |
| A completed reviewer run proves a review happened | Coordinator inferred this from state/file checks (`skills/workflow-coordinator/SKILL.md:173-183`) | Kernel Learn arm counts a completed reviewer run even when its artifact is only a refusal (`Backlog/Priorities.md:22`) | `WORSE` |

### Lesson close-out and operational scripts

| Old behaviour | Old evidence | Kernel behaviour and evidence | Verdict |
|---|---|---|---|
| Archive only after verifier/reviewer completion and terminal disposition tables | `skills/workflow-coordinator/SKILL.md:173-195,266-269` | Archive has stage prerequisites and requires an eligible lesson mark even through a waiver (`ailedger --help:110-115`) | `BETTER` |
| Automatic stop-time close-out enforcement external to the model | `skills/workflow-coordinator/SKILL.md:29-42,255-264`; `closeout-hook.sh:9-23` | No host stop hook; stage guards fire only when transition is requested (`Backlog/Priorities.md:20`) | `ABSENT` |
| Scope automatic enforcement to recent work | `closeout-hook.sh:12-13`; `validate-closeout.py:199-213` | No equivalent automatic enforcement or recent filter (`ailedger --help:7-78`) | `ABSENT` |
| Warn on stale open tasks and error on them in archive-retrospective mode | `validate-closeout.py:119-127,178-189` | Per-task retrospective exists; no fleet audit/staleness command (`ailedger --help:14-18`) | `ABSENT` |
| Refuse a terminal task with no verifier artifact | `validate-closeout.py:129-141` | Work completion requires a verifier; Archive is stage-armed (`ailedger --help:106-115`) | `BETTER` |
| Detect state claiming verifier completion when its output is missing | `validate-closeout.py:129-135` | Runs and typed artifacts are independently recorded and queryable, and rewritten coordinator requires both (`cognitive/skills/workflow-coordinator/SKILL.md:80-99`) | `BETTER` |
| Reject OPEN disposition and validated/rejected rows lacking citation or actor | `validate-closeout.py:137-152` | Claims cannot be resolved without linked directional evidence; each mutation has actor provenance (`ailedger --help:5,30-35`) | `BETTER` |
| Detect a lesson-bearing disposition with no ledger pointer | `validate-closeout.py:154-159` | Archive requires an eligible lesson mark, but only after Archive is requested (`ailedger --help:110-130`) | `BETTER` |
| Warn when an orchestration plan lacks File Ownership | `validate-closeout.py:161-165` | Multi-area work is refused without a recorded not-split alternative (`ailedger --help:117-120`), a stronger but not identical control | `BETTER` |
| Scan either local active/done trees or the global archive, with optional date bound | `validate-closeout.py:169-213` | `retrospective build` reads one named task only (`ailedger --help:14-18`) | `ABSENT` |
| Minter assigns content-addressed ids so retries cannot duplicate beliefs | `mint-lesson.py:75-79,266-301,368-389` | Lesson identity is derived from governed source task/record and duplicate ids are event-keyed; imported store rows show stable source identities (`lessons/lessons.jsonl:1-189`) | `EQUAL` |
| Validate required fields, classes, 1–4 slug tags, belief safety, verify reason, and replacement target before writing | `mint-lesson.py:44-66,177-263` | `lesson mark` requires source, repo, kind, class, verify, do-not, cognition and validates verify direction; tags are repeatable (`ailedger --help:55-63,132-191`) | `BETTER` |
| Require a real verify command or an explicit non-checkable reason | `mint-lesson.py:240-246` | Kernel additionally requires expected success/failure direction and offers confirmed single-command recheck (`ailedger --help:140-184`) | `BETTER` |
| Preview schema and all writes without mutation | `mint-lesson.py:15-18,328-404` | No lesson dry-run/schema command (`ailedger --help:55-70`) | `ABSENT` |
| Validate all lesson entries before any append | `mint-lesson.py:177-263,355-369,406-431` | Marks are separate mutations; no batch transaction spans multiple marks (`ailedger --help:55-63`) | `ABSENT` |
| Warn when submitted lessons omit a lesson-bearing verifier identifier | `mint-lesson.py:129-174,372-382` | No exposed completeness check between all dispositions and all marks (`ailedger --help:110-130`) | `ABSENT` |
| Append an index row, write detailed `lesson.md`, sync archive, and rebuild README atomically in one command path | `mint-lesson.py:294-325,406-438` | Archive mints marked event records into the JSONL lesson store and publishes through `--lesson-root`; detail is embedded rather than split (`ailedger --help:125-138,200-204`) | `EQUAL` |
| Preserve corrections append-only and hide superseded heads from recall | `lessons.md:18-45`; `build-readme.py:146-165` | `--supersedes` keeps older lessons from recall without deletion (`ailedger --help:125-130`) | `EQUAL` |
| Parse legacy multi-line and current one-line ledgers together | `build-readme.py:1-10,51-127` | Existing old lessons were imported into `lessons/lessons.jsonl:1-189` with original ids/citations; ongoing compatibility no longer requires parsing the Markdown formats | `EQUAL` |
| Generate repository counts, recent live lessons, stale rows, and archive links | `build-readme.py:130-238` | No generated or CLI aggregate index exists (`ailedger --help:7-78`) | `ABSENT` |
| Build a dated, versioned, self-contained distribution zip and warn when changelog version is absent | `build-bundle.sh:1-38` | No kernel command packages cognitive rules/skills or validates a release changelog (`ailedger --help:7-78`) | `ABSENT` |
| Deploy only the chain's skill directories to Claude/Codex and offer a drift-only check | `install.sh:1-87` | Runtime reads the cognitive root directly and context hashes detect later skill drift (`ailedger --help:20-25,84-99`), but there is no deployment/check utility | `ABSENT` |
| Report build identity and detect installed-version staleness | Old bundle only encoded version in filename and changelog (`build-bundle.sh:2,16-25`) | `ailedger version` reports build provenance; backlog records the installed-version warning (`ailedger --help:7`; `Backlog/Priorities.md:66`) | `BETTER` |

## Known live qualifications

These prevent a paper control from being mistaken for a working one:

- Stage arms are request-time guards, not schedulers. The corpus currently contains 46 task
  directories, and backlog row 29 records that 24 of 43 examined tasks had never transitioned.
- Reviewer isolation is artifact-deep only; the manifest still exposes goal and other governed truth.
- A reviewer refusal can satisfy the completed-run check.
- Workers and researchers can write reports but cannot file them as governed artifacts; their proof
  is limited to claims/evidence until backlog row 47 is addressed.
- Scope occupancy is directory-level and task-local. It is still stronger than the old prose-only
  ownership rule, but it serializes unrelated tests and does not prevent cross-task collision.

## Undetermined

- Whether the old Claude `Stop` hook was actually armed on every machine cannot be recovered from the
  repository. The old system explicitly distinguished the stronger armed hook from Codex's weaker
  explicit validator (`skills/workflow-coordinator/SKILL.md:29-42`). The matrix grades the shipped
  behavior, not an unverifiable installation rate.
- The missing `references/` files for `technical-researcher` and `code-reviewer` mean their referenced
  task-emphasis, research-standard, output-contract, and C#-lens details cannot be inventoried. They
  are not counted as lost kernel behavior because the old repository does not contain them either.
