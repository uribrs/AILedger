# Priorities

Updated 2026-09-24 against the current source and implementation history. **Done means the backlog change is delivered**, not merely that a related investigation was archived. Partial work remains open. Original item numbers are preserved.

## Counts

- **62 numbered entries**: 23 done, 37 open or partial, 1 deferred, 1 measurement reference.
- **30 bugs, 19 features, 13 changes.**

Completed entries retain only a heading and short description here; their standalone backlog files are removed. Task histories and lessons are unchanged. Entries 61 and 62 were previously present as files without priority rows.

## Open work, in priority order

| # | backlog entry | task id | status | kind | notes |
|---|---------------|---------|--------|------|-------|
| 58 | a-dead-run-leaves-no-map-to-what-it-left | not opened | open | bug | **A run that dies rolls nothing back and announces nothing.** `Cancelled`/`Failed` leaves files in the working tree and often partial ledger writes; the run record carries status and `endedAt` and nothing about what it accomplished, so the next coordinator guesses — and guesses "nothing". Measured on `2026-09-20_0817-read-the-refusal-back`, three runs died on one work item leaving three different states: `RW4` (cancelled at timeout, 60 min) left code and 14 tests and filed nothing; `RW6` (failed on a provider spend limit, 10 min) left all three `D13` repairs and filed nothing; `RW7` (cancelled by the operator, 63 min) left the same **plus 5 claims and 8 evidence records** — the candidate identity, the suite at 1310/1310, the full mutation matrix mapping each mutant to the tests it kills, the no-install check, and a finding that the xunit-host procedure in `cognitive/RULES.md` has two defects beyond the four it documents. It was ~95% done; only `execution_notes.md` was missing. The coordinator relaunched without looking, briefed the replacement to redo the completed mutation work, burned 25 minutes, and published a cost analysis claiming 133 minutes died "leaving no record" — wrong about the largest of the three. **Fix, either half:** on a non-`Completed` end, record the event ids the run wrote and a `git status` delta against the item's `BaseRef` — the launcher holds both; **or** refuse a relaunch on a work item whose last run did not complete until the coordinator has rebuilt context since the death, the same shape as the existing brief gate. Distinct from row 36, which is about runs that wrote *nothing* and argues for writing earlier, and from row 24, which is about `Cancelled` being four endings in one status — four precise statuses still would not say what was left behind. |
| 57 | a-rule-has-no-id-so-its-prose-is-used-as-one | not opened | open | bug | **Three defects in one work item, all silent, all in the flattering direction.** `RefusalRecord` carries no rule identity, so everything asking "which rule was this?" normalises the prose — `RefusalRuleKey.Of` takes the first line and replaces quoted literals with `'X'`. On `2026-09-20_0817-read-the-refusal-back` that produced: RR1 Major, the new diagnostic lines name records **unquoted** so normalisation never reaches them and one rule refusing one actor sixteen times became sixteen keys; RR2 Minor, `'[^']*'` matches across a newline so a literal spanning one is severed by the first-line cut; RR3 Blocker, an **apostrophe in prose** is read as an opening quote and the pairing runs one quote out of step, leaking both ids into the key. Each time `RepeatedKeys` keeps only rows with `Repeats > 0`, so the rule leaves the retrospective rather than undercounting. Two of the three were found by a code reviewer, not a verifier and not the suite — the tests on that path used fixed strings with no identifiers and passed either way. **The fix is a stable rule id emitted at the throw site**, after which `RefusalRuleKey` is deleted rather than patched a fourth time. Deferred from that task by `D13`, which applied the third patch only because the defect was live and measured its blast radius — though with the wrong instrument: `E27` counted the 836 rows already written and found one key changing, while the **code** carries 27 possessive messages of which **two** mis-key today, the second (`RunDispatchRules.cs:45`) invisible to the corpus because it has never fired. The delivered lookbehind fixes both, and the code comment above the pattern says it is the last patch there. `RefusalRecord` is written on a failure path by every command, so the new field is optional-and-trailing or every journal ever written stops parsing. |
| 55 | the-kernel-refuses-wrong-records-and-not-wrong-actions | not opened | open | change | **The maturity assessment, 2026-09-12, measured over 48 tasks, 472 runs, 8,804 events, 350 refusals, 311 stage transitions and 220 lessons.** The kernel is excellent at refusing a wrong *record* and absent at preventing a wrong *action*. Everything downstream of a write is validated rigorously; everything upstream — the brief, the dispatch, the decision to start — is unguarded. One week's error profile makes the shape plain: five runs lost to briefs carrying instructions the kernel refuses, four claims validated before checking, and **zero bad records**. **Mature, trustworthy unattended:** the event log, replay and storage, with state byte-compared against a fresh replay; the epistemic core (a claim cannot be validated without evidence naming it by direction) which caught the operator four times this week; roles, capabilities and cross-provider verification, whose measured value is two code reviews finding eight defects that nine verifier rounds missed; the context gate with per-skill digests and its two named doors; the refusal journal, which diagnosed three failures in one command each. **Immature:** artifacts, where the rules are right but a lean task cannot be verified at all and that is discoverable only by losing a verification; lessons, where mint, tag, recall-by-intersection and verify all work but a false published lesson is permanent (row 50); measurement, days old with its gate unpassed. **Weak, and these are the 1.0 list:** (1) **briefs are unvalidated** — nothing checks at dispatch whether the artifact a brief names exists, whether the subject holds the capability the instruction needs, or whether the work item is in a state that accepts it; biggest single win and the cheapest. (2) **the pipeline is optional in practice** — 14 of 48 tasks never transitioned once, and there are **151 waiver events against 311 transitions**, nearly one waived arm for every two taken, most of them the operator's. (3) **no trigger exists for anything** — closeout, scoring, stale work all depend on a person remembering; 14 tasks sit unclosed now. (4) **scope cannot be widened** (row 41), four full item cycles lost to it in one session. Also measured: **run mortality is 24%** — 111 of 472 runs ended other than completed, 40 failed and 7 to protocol errors — and until 2026-09-10 a dead run left no reason on its record. None of the four is research; all are ordinary work against a record that already holds. |
| 52 | the-cli-tells-you-less-than-it-knows | not opened | open | bug | Two small diagnostics, both of which cost real round trips tonight. **One:** an enum refusal names the enum but not the option. `lesson mark` carries two — `--lesson-actor` is `LessonActor` (researcher, executor, verifier, recon) and `--audience` is `RoleKind` (no `executor`) — so `"'executor' is not a valid RoleKind"` sent me to the wrong flag, and I validated a claim, `FC3`, blaming help text that was correct. Superseded by `FC4`. **Two:** `--base-ref` is honoured by `work add` and appears nowhere in its help, which is the residue of row 26's correction. Both are one-line changes in `src/AILedger.Cli`, deferred only because a verifier run holds that area. |
| 26 | the-reviewers-isolation-is-artifact-deep-only | not opened | open | bug | **blocks 9's capability-utilization dimension.** `ReviewerExclusions` withholds six artifact kinds but the manifest still hands a reviewer the task goal, every claim, decision, alternative and their evidence — and the skill forbids the user's intent by name. Worse, a coordinator writing verifier findings into a constraint routes them past the filter: item 6's reviewer refused to review for exactly that reason and was right to. **Corrected 2026-09-10 — the baseRef half of this row was wrong.** It claimed the kernel has no such field. `WorkItem.BaseRef` is declared at `GovernanceModels.cs:264` and `ResolveBaseRef` (`CliApplication.cs:2007-2021`) captures `git rev-parse HEAD` from the first scope's directory automatically at `work add`, or takes an explicit `--base-ref`; 37 of 112 live work items carry one and the rest predate the resolver. The real defect is discoverability: **`--base-ref` appears nowhere in `work add`'s help**, so an actor that needs to correct it cannot learn the option exists. Validated as `C3` on `2026-09-10_1750-what-the-skills-drove`. This wrong row was cited by that task's recon as independent corroboration and propagated into a published lesson before it was caught — recorded there as `C2` and `E3`. On 2026-09-10, a code-reviewer run on `2026-09-10_0845-pipeline-mandatory` stopped rather than review because eight constraints carried verifier verdicts and one told it to read the verifier artifact by id. Validated claim C12 with evidence E15 establishes the mechanism in source: `ContextAssembler` applies its six-kind `ReviewerExclusions` only to a CodeReviewer, while Constraint is in `AlwaysIncludedKinds` and excluded from nothing. Superseding the eight briefs and filing one minimal bundle produced a manifest the reviewer accepted; it then found three defects that three verifier rounds missed. This is a role that can silently produce nothing, not a theoretical isolation concern. |
| 20 | research-needs-an-open-claim | not opened | open | bug | Discovery's only exit is Research, and that arm refuses without an open claim. hit on both of items 1 and 2. see 16 — closing every claim currently locks a task out of Archive, which 16 would make worse. found while working items 1 and 2; unranked until now. |
| 47 | a-researcher-and-a-worker-cannot-prove-they-ran | `2026-09-10_1117-researcher-can-prove-it-ran` | **blocked** | feature | W1 is blocked. `GovernedArtifactKind` still has no research-output or execution-notes member, while `RoleDefaults` grants `recordArtifact` to neither role. A run in either role can leave only claims and evidence, so a brief that asks it for a filed document is unsatisfiable. Minted as a lesson on 2026-09-10 from `2026-09-10_1007-rewrite-the-skills` claim XC4. |
| 48 | the-learn-arm-counts-a-reviewer-run-without-reading-what-it-produced | not opened | open | bug | On 2026-09-10, a code-reviewer run completed after filing a `CodeReviewOutput` whose entire content was a refusal to review. The Learn stage arm requires a completed CodeReviewer run for code-bearing work and would have accepted it. The arm cannot distinguish a review from a documented refusal, the one case where run completion means the opposite of what the arm is checking for. |
| 46 | scope-occupancy-does-not-cross-tasks-and-tests-is-one-area-everything-needs | `2026-09-10_0858-occupancy-crosses-tasks` | open | bug | `ScopeOccupancyRules` iterates one task's state, so two tasks can hold the same directory and the kernel refuses neither. Validated claim C5 with evidence E5 measures the second half: because scope is directory-level, `tests` is a single area every code-bearing work item needs, so all test-bearing work serialises whether or not it collides. Three launches were held by hand on 2026-09-10 for this reason alone; not one of the three pairs would have touched the same file. |
| 35 | the-timeout-is-set-at-the-median-run | not opened | partial | change | ProviderLauncher now defaults to 1800 seconds. Adaptive timeouts and graceful recording before termination remain proposals. |
| 31 | four-roles-cannot-record-a-discarded-approach | not opened | open | bug | **live defect, C28.** `RoleDefaults` grants `RecordAlternative` to the two lead roles only, so Worker, Researcher, Verifier and CodeReviewer are all refused — and `CLAUDE.md` documents the command with a worker actor in its own example. four journalled refusals across two tasks on two days. what is lost is the most evidence-bearing discarded approach there is: one an implementer actually tried. `EnsureSafe` names the four capabilities deliberately withheld and this is not one of them. |
| 43 | read-the-refusal-back | not opened | partial | feature | Record-state diagnostics and own-repetition reporting shipped in 580cbf5. Cross-task prior occurrences with dispositions remain unfinished. |
| 36 | the-ledger-is-written-at-the-end-or-not-at-all | not opened | open | bug | median first ledger write lands at **72% of run duration** across 249 runs (p25 36%, p75 83%); R26 wrote all ten of its records in 78 seconds after fifteen minutes of work. **50 runs recorded nothing at all** — 19 of 30 failed, 16 of 43 cancelled. `CLAUDE.md` asks for the claim before the work; measured, it arrives after. `millisecondsToFirstLedgerWrite` cannot support the follow-up because its null means four different things. |
| 34 | scope-cannot-follow-a-worktree | not opened | open | bug | **the largest single loss in the ledger.** scope is stored as an absolute path resolved at `work add` against wherever the operator stood, so the kernel cannot tell that two checkouts of one repository are the same governed area. `AILedger-memory` and `AILedger-provider-preflight` are git worktrees, not separate repositories — the kernel refuses the very isolation pattern used to run agents in parallel. all thirteen scope refusals in the ledger are in `standalone-memory-index`, over eleven hours; that task is 42% of all runs ever made and **63% of all runs ever lost**, 120 runs at a 33% loss rate against 0-19% everywhere else. the worktree half is a small fix; whether scope may leave the repository at all is a separate question the end goal still forces. |
| 13 | the-append-is-quadratic | `2026-09-10_0907-append-in-place` | partial | bug | Append-in-place and torn-command recovery are implemented. Incremental state advancement remains a separate unfinished successor: 2026-09-14_0030-state-advances-from-a-bookmark. |
| 12 | resolve-claims-where-the-evidence-lands | `2026-09-07_2136-claim-resolution-ergonomics` | open | feature | `status` now reports 76 of 80 open claims on `ledger-learning` are clearable in one pass, which is higher than the entry estimated. |
| 22 | the-reviewers-approval-goes-stale | not opened | partial | bug | Work completion now checks review freshness after latest work and verification. The Learn arm still checks completed reviewer roles rather than current review coverage; keep the stage-level follow-up open. |
| 27 | owed-does-not-say-what-blocks | not opened | open | bug | `TaskDebt` reports open claims, items awaiting verification and lesson debt, and nothing about escalations — while an open escalation on an item refuses `work complete` outright. Item 6's `owed` read all zeros for four hours with two escalations open; the refusal was the first thing that surfaced them, and both questions had already been answered by other routes without acknowledgement. |
| 32 | a-correction-is-one-command-away-and-cannot-be-undone | not opened | open | bug | **cost measured tonight, C30.** `claim resolve --status superseded` derives refinement-versus-correction from the replacement's status at that instant. superseding by a claim still `open` lands a correction, which invalidates dependent decisions, blocks dependent work items, and cannot be undone — and a blocked item can be neither unblocked nor completed, only replaced. it cost item 6's W2 a re-verification cycle after three completed runs. one predicate and one `--accept-correction` flag; the refusal must not become a rule that forces every supersession to look like a refinement. |
| 33 | the-suite-is-not-comparable-across-runs | not opened | open | bug | **C31.** `ACleanTreeAtADifferentCommitWarns` and `ADirtyTreeAtADifferentCommitStillWarns` are the only two tests that create a real git commit, so they fail in a run whose sandbox refuses `git commit` and pass in one that allows it. the same tree reported 526/2, 526/2 and then 534/0 across three runs. three agents recorded the count correctly and none asked what the two were, so every "same failures in both states" argument in this task rests on a coincidence of permissions. prove the fire path against the already-pure `Warning`; keep one git-backed test that names the environment in its failure. |
| 23 | attention-items-are-task-wide-but-work-is-not | not opened | open | bug | `ValidateVerifierOutput` reads attention ids from the one current plan and demands every verifier dispose all of them. item 5 had six for W1 and three for W2, so its second verifier either writes six `not-applicable` rows or the plan stops describing the task. |
| 11 | attention-items-as-a-gate | `2026-09-07_2136-attention-item-gate` | open | feature |  |
| 15 | waivers-need-a-floor | `2026-09-07_2136-waiver-floor` | open | change |  |
| 16 | open-claims-block-archive | `2026-09-07_2136-archive-open-claims` | open | change | see 20, `research-needs-an-open-claim` — closing every claim currently locks a task out of Archive, which this entry would make worse. |
| 18 | archived-memory-conservation | `2026-09-07_2136-archive-compaction` | open | feature |  |
| 24 | cancelled-means-four-different-things | not opened | open | bug | item 5 holds five `Cancelled` runs: four operator filing runs that succeeded, and one nine-minute verification the host killed for memory. `self-scoring` asks for failed and retried runs as a cost signal and would read five where the true number is one. |
| 10 | single-agent-relaxation | `2026-09-07_2136-single-provider-mode` | open | change |  |
| 54 | the-memory-document-carries-less-than-the-run-record | `2026-09-10_1420-model-the-coordinator` | partial | bug | The shared run-completion projection is fixed. LedgerDocumentNormalizer still omits usage, truncation and timeout details from RunSummary documents; retain the document-level follow-up. |
| 50 | a-published-lesson-cannot-be-retracted | `2026-09-10_1955-retract-two-false-lessons` | open | bug | Cross-task lesson retraction remains unavailable: LessonMarkRules resolves superseded lessons in the current task state. The archived correction task did not add store-wide supersession. |
| 51 | a-corrective-lesson-must-inherit-the-tags-of-what-it-corrects | `2026-09-10_2010-corrective-lessons-must-inherit-tags` | partial | feature | The two corrective lessons were republished with matching tags. Automatic validation or warnings for corrective tag inheritance remain unimplemented. |
| 40 | staffing-is-a-name-not-a-run | `2026-09-09_1132-ready-arm-staffing` | open | bug | Ready still checks assigned role names rather than engagement. The referenced archived task investigated the gap; it did not implement the proposed change. |
| 41 | scope-cannot-be-widened-after-review | `2026-09-09_1010-retrospective-projection` | open | bug | Work-item scope still cannot be widened after review. Bundled assurance is available, but does not implement scope amendment or repair inheritance. |
| 28 | the-launch-does-not-ask-if-the-host-can-hold-it | `2026-09-09_0858-provider-launch-preflight` | open | feature | The archived task produced the readiness proposal. General provider authentication, sandbox and host-memory admission remains unfinished; current launch preflight checks governance and configured verification environments. |
| 38 | provider-authentication-and-sandbox-preflight | `2026-09-09_0858-provider-launch-preflight` | open | feature | Authentication and sandbox readiness before provider execution remains the follow-up shared with item 28; the archived proposal task is not proof of implementation. |
| 39 | standalone-semantic-memory | `2026-09-08_1909-standalone-memory-index` | partial | feature | The standalone SQLite/FTS5 shadow index is delivered. Activation, freshness recovery and the opt-in real-task trial remain in 2026-09-09_2118-memory-index-activation; retain the entry for that unfinished design. |
| 49 | re-grade-the-pre-kernel-behaviour-matrix | `2026-09-10_1750-what-the-skills-drove` | open | change | The behaviour matrix was produced, but its kernel-side re-grade remains outstanding after rejected claims MC2 and MC5. Recheck against source rather than citing this backlog as evidence; also disposition MC3. |
| 61 | measure-lessons-against-current-reality | not opened | open | change | Check lessons against their sources; retain what survives and remove refuted or stale memory as described in the entry. |

## Deferred work

| # | backlog entry | task id | status | kind | notes |
|---|---------------|---------|--------|------|-------|
| 60 | [score-adaptation-and-avoidable-cost](score-adaptation-and-avoidable-cost.md) | not opened | deferred until October 2026 | change | Clarify D4 planning/decision/adaptation judgments, D6 detection opportunities and D8 productive versus avoidable cost. Preserve the existing ten-row format and historical reports; version the rubric and calibrate on four to six contrasting tasks. No overall weighted score or measurement-class refactor. Deferred by the operator until the token budget replenishes. |

## Reference material

- **37 — what-a-run-actually-costs:** historical cost baseline and remaining measurement gaps; see [the entry](what-a-run-actually-costs.md).
- [Maintained self-scoring rubric](../docs/self-scoring-rubric.md): scoring capability is complete (item 9); improvements remain deferred under item 60.

## Done

### 1 — record-that-context-was-built

Records the manifest hash and artifact count used to brief a run.

### 2 — did-the-lesson-matter

Records lesson citations on claims, decisions and alternatives so their influence can be measured.

### 3 — make-status-say-what-is-owed

Reports outstanding claim, verification and lesson-citation debt in task status.

### 4 — kernel-version-stamp

Reports the installed kernel version and warns when its source identity is stale.

### 5 — record-the-refusals

Journals command refusals alongside task history without affecting replay.

### 6 — see-inside-a-run

Persists provider run usage, timing and result sidecars. Remaining measurement gaps are tracked in item 37.

### 7 — measure-before-scoring

Builds a deterministic retrospective of task activity, costs, refusals and explicitly unmeasured fields.

### 8 — route-the-workflow-lesson

Adds workflow lesson kinds, audiences, separate recall selection and expected-direction verification.

### 9 — score-the-governance

Records evidence-bound, ten-dimension workflow retrospectives; the current rubric is docs/self-scoring-rubric.md.

### 14 — mirror-the-replay-validator

Pairs concept-local command rules with replay validators; the central validator now routes to those units.

### 17 — decompose-the-cli

Splits CLI handling into command families and preserves file scopes instead of widening them to directories.

### 19 — review-before-complete

Requires a current code review after verification before scoped work can complete (e691e20).

### 21 — the-coordinators-run-cannot-close

Allows explicitly declared no-provider filing runs to complete; coordinator sessions separately record coordination activity. Usage gaps remain in item 37.

### 25 — a-productive-task-starves-its-successor

Ranks recalled lessons by tag overlap and limits each source task to three slots.

### 29 — the-arms-only-fire-if-you-walk-through-them

Gates stage-specific entry actions so work cannot bypass the pipeline merely by remaining in Discovery (764553c).

### 30 — valid-JSON-non-object-line-kills-a-run

Treats valid JSON non-object provider lines as malformed input without throwing away the surrounding run (a9070ab).

### 42 — one-long-line-destroys-a-whole-run

Handles oversized provider output without the original per-line run failure; later stream handling preserves complete JSON records within limits.

### 44 — the-context-gate-and-its-two-doors

Requires fresh, skill-hashed context before work creation and provider dispatch, with explicit waiver paths.

### 45 — the-six-pipeline-skills-rewritten-against-the-kernel

Rewrites pipeline skills around the kernel commands and removes obsolete pre-kernel instructions.

### 53 — a-run-leaves-its-scratch-tree-inside-the-repository

Provides external scratch/build paths and governed test execution guidance to avoid nested source copies contaminating repository builds.

### 56 — recon-cannot-satisfy-the-design-arm

Adds InternalRecon artifacts and permits current internal recon to satisfy Design when external research is unnecessary (c4333e2).

### 59 — the-manifest-is-filtered-by-role-and-nothing-else

Prioritizes work dependencies and bounds delivered context with explicit omission metadata or an oversized-input refusal (8737d4e).

### 62 — harden-the-governed-loop-after-stage-entry-gating

Hardens review-before-completion, launch and batch preflight, provider output guidance, kernel-build provenance and retrospective causality (e691e20).
