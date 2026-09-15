# Harden the governed loop after stage-entry gating

The retrospective for `2026-09-14_0130-gate-the-entry-actions` showed that the kernel's central
model works: refusals changed behaviour, evidence survived provider hand-offs, repair findings were
carried forward, and the completed implementation replayed every live task. The remaining problems
are not a reason to redesign the ledger. They are places where a locally correct rule composes into
an impossible lifecycle, where a provider integration can turn successful work into a failed run,
or where the record lacks enough identity to explain its own self-hosting history.

This is one ordered hardening programme. Keep the five parts independently deliverable; do not turn
the umbrella into one repository-wide work item.

## 1. Make review happen before completion

Implement the preferred direction in `Backlog/review-before-complete.md`: code-bearing work cannot
be completed until a work-bound CodeReviewer run has completed with its `CodeReviewOutput`, after
the verifier run that saw the latest work.

The stage-entry task reproduced the full trap:

- W7 through W12 were completed after verification.
- Six reviewer launches were then refused because a Completed work item accepts no run.
- A task-wide CodeReviewer run could start, but could not file `CodeReviewOutput` because that
  artifact must name a work item and match the producer run.
- The task-wide run then could not close Completed because it had no matching output.

Every refusal was locally defensible. Together they formed an impossible reviewer lifecycle. The
fix belongs at `work complete`, where the caller is asserting that the item is finished, rather
than in another waiver or a special post-completion repair ritual.

Preserve the existing operator door. `--without-verification` should continue to waive the required
passes together with one durable reason, and documentation-only work must not acquire a code-review
requirement.

Acceptance:

- Completing code-bearing work without a current work-bound review is refused before the item is
  terminal.
- The refusal names the missing CodeReviewer run/output and the ordering it requires.
- A review older than the latest working or repair run does not satisfy completion.
- Non-code-bearing work and the existing operator waiver retain their intended behaviour.
- Command-time and replay rules change together only where old event shape makes replay tightening
  safe; otherwise preserve historical replay.

## 2. Preflight impossible task-wide reviewer launches

Until task-wide `CodeReviewOutput` is a real artifact shape, a CodeReviewer run without `--work` is
not a usable run. It can perform the entire review, but no legal filing command can complete it.

Refuse that launch before resolving or starting the provider. The message should say that
CodeReviewOutput is work-scoped and direct the coordinator to review the live work item before it is
completed. Do not let the generic advice that coordinating task-wide artifact runs omit `--work`
apply to Verifier or CodeReviewer roles.

Acceptance:

- A task-wide CodeReviewer provider launch is refused before provider process creation.
- The same preflight and command-time path share one rule and one message.
- The refusal does not prevent task-wide coordinating runs that can legally file their artifacts.
- If task-wide review is later made a first-class artifact, remove this refusal as part of that
  design rather than adding a bypass.

## 3. Put the output budget in every provider brief

`Backlog/one-long-line-destroys-a-whole-run.md` fixed AILedger's own reader so an overlong line can be
cut and counted. The stage-entry task exposed the upstream variant: Codex itself truncated a JSONL
tool-result record at roughly 1 MiB, so AILedger received malformed JSON rather than an overlong
complete line. RP4, R7, R9 and CV10R2 all reached useful or successful results but were recorded as
ProtocolError.

Constraint K12 stopped most recurrences only after three failures. It also demonstrated that this
cannot remain task-local folklore: CV10R2 later searched run JSON with a line-count bound, not a byte
bound, and crossed the same limit despite carrying K12.

Inject a provider-specific output-budget section into every generated brief. It should require large
outputs to be redirected to files and inspected with byte-bounded extracts, and explicitly state
that `head` or `tail` bounds lines, not the size of one JSON line. Keep this provider integration
guidance out of user-authored task constraints.

Acceptance:

- Every Codex and Claude launch brief carries the applicable per-command and per-line limits.
- The brief gives a safe, copyable pattern for redirecting and inspecting large output.
- Tests assert the generated brief, so a wording refactor cannot silently remove the protection.
- The run still records truncation or malformed protocol honestly; briefing is defence in depth,
  not permission to relabel corrupt streams as complete.

## 4. Stamp mutations and refusals with the running kernel build

`Backlog/kernel-version-stamp.md` established the installed binary's version and source commit. That
solves “what is installed now”; it does not answer “which kernel accepted or refused this historical
command.” During the stage-entry task, source, installed tool and active rules diverged while the
kernel was operating on its own task. The retrospective can infer that divergence from behaviour
and constraints K15/K17, but cannot prove the enforcing binary per event.

Record the running executable's embedded version, source commit and build time on every mutation.
Stamp refusal-journal rows too: a refusal is precisely where a self-hosting retrospective needs to
know which rule set spoke. Record the executable identity, not repository HEAD; the whole problem is
that those can differ.

Use optional trailing fields or a compatible envelope extension so old histories replay unchanged.
A dirty source tree affects stale-source warnings, not the identity of the already-built executable.

Acceptance:

- Every newly appended event and refusal row identifies the running kernel build.
- Provider launch/run provenance can be joined to that same identity.
- Old events and refusal rows without the fields replay and project normally as “not recorded.”
- `retrospective build` partitions self-hosting behaviour by kernel identity instead of inferring it
  from timestamps or refusal prose.
- The installed/current-source warning remains advisory; version drift is recorded, not forbidden.

## 5. Add batch preflight, then make scorer avoidability honest

### Batch preflight

Twenty-nine refusal rows represented roughly eleven causal incidents. Three identical stale-brief
refusals, three identical scope failures, and six completed-item reviewer refusals came from parallel
batches sharing one invalid precondition.

Add a read-only batch preflight that evaluates every planned work item or provider launch against one
task version before any member starts. Report each invalid member, but group shared causes so the
coordinator fixes the plan once. This must not weaken, suppress or reinterpret the real command-time
refusal; it moves discovery before fan-out and leaves execution subject to the same rules.

Acceptance:

- A batch with one shared invalid prerequisite starts no provider process.
- The result reports affected members and one grouped causal refusal.
- The preflight records or returns the task version it checked; execution against a changed version
  checks again rather than trusting stale approval.
- Individual commands retain their current refusal behaviour.

### Scorer heuristic cleanup

The retrospective labelled superseded decision GD5 an `avoidableError` because evidence GE18
predated it. GE18 established the problem GD5 addressed; it did not establish that GD5's first
solution was already known to be wrong. GD6 was a better interpretation of the same evidence three
minutes later. Event ordering alone cannot distinguish avoidable neglect from legitimate reasoning
improvement.

Do not emit a semantic verdict stronger than the recorded causality. Prefer one of:

- require decision supersession to name a reason and, when applicable, the evidence that made the
  earlier decision avoidable; or
- report the sequence fact neutrally as `priorEvidenceAvailable` until a scoring agent judges it.

Acceptance:

- Pre-existing evidence alone does not produce the label `avoidableError`.
- A positive avoidability verdict cites the exact prior record that already contradicted the
  decision, not merely evidence the decision depended on.
- Histories lacking a supersession reason remain measurable as sequence facts and explicitly
  unjudged semantically.
- The scorer still detects a decision that ignored an already-recorded contradiction.

## Engineering impression and priority

This is hardening around a proven architecture, not rescue work. The ledger did the difficult part:
it preserved enough exact history to distinguish guardrails doing their job from the process walking
into them. A weaker system would have produced fewer refusals by silently accepting the bad states
and would have left no causal record to inspect.

The priority order above is deliberate:

1. Lifecycle coherence first, because an individually valid set of rules must never compose into a
   run that cannot legally finish.
2. Early reviewer preflight next, because impossible work should cost zero provider tokens.
3. Provider output budgeting next, because successful work recorded as failure is operationally
   corrosive even when the code survives.
4. Kernel identity next, because further self-hosting without it will keep producing qualified,
   inferential retrospectives.
5. Batch ergonomics and scorer semantics last, after the underlying lifecycle and evidence are
   trustworthy.

Do not optimise for a smaller refusal count. Optimise for invalid plans being rejected once, before
fan-out, with enough provenance that the resulting retrospective can say exactly why.
