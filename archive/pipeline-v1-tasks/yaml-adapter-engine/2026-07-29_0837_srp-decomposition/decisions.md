# Decisions

One decision per bullet. Amend in place if execution overturns one; do not delete.

- **D1 — Instruments before any `src/` change (S0).** `rules_audit` and the dependency-rule check are
  committed first, in their own commit, with no source edits. *Why: every figure in the ledger currently
  has no reproducible source, and the rule D14 broke has no counter at all.*

- **D2 — `Execution` before `Workflow`, `Definition` last.** *Why: `ARCHITECTURE.md`'s phase-3 order says
  Execution first; S5's seam depends on S4 putting `ExecuteStageAsync` on `IIntegrationEngine`; and
  `YamlIntegrationLoader` is independent with no method over 100 lines (A7).*

- **D3 — The contract does not freeze internal type shapes.** It specifies the SRP outcome, the named
  targets and the gates; the executor chooses shapes from the seams. *Why: two of six design-lens review
  passes in task 1 concluded the contract had specified the wrong shape (D9, D12), and the executor was
  correctly blocked from fixing it by contract authority.*

- **D4 — `ARCHITECTURE.md`'s 11-type plan is a strong prior, not a specification.** Deviations are
  allowed and must be recorded with the reason. *Why: the plan predates the sink inversion and two of its
  figures are already stale (A2).*

- **D5 — Measured values supersede `ARCHITECTURE.md`; the ledger is corrected in S0's commit.** Targets
  are 1,941 / 952 / 672, not 1,902 / 912 / 655. *Why: the table's as-of marker claims C3 while its file
  sizes appear to date from C1 — the third recurrence of the same staleness (D8, D13).*

- **D6 — Public overloads are preserved as a facade unless proven impossible (A1).** `ExecutionRequest`
  and `ExecutionOptions` become the internal core. *Why: the host boundary is worth more than signature
  elegance, and changing it is a decision to surface rather than a side effect of a refactor.*

- **D7 — "No file in more than one commit" is NOT promised this task.** S1, S2 and S3 all cut into
  `IntegrationEngine.cs`. *Why: task 1 could make that promise because its three targets were disjoint;
  claiming it here would be false, and A4 records the real dependency structure instead.*

- **D8 — Task-1 shapes are reshaped only where a seam makes the better shape obvious (A5); otherwise
  left alone.** *Why: reshaping `PaginationContext` because a reviewer disliked it, absent a seam that
  settles it, is the same "chase the number" error this task exists to correct.*

- **D9 — One idiom per role, chosen once.** The branch ships `readonly record struct` and `record class`
  justified by the same stated property (D6/D12 cross-cutting note). Pick one for call-scoped carriers and
  apply it to every new type. *Why: two idioms for one role is a readability cost with no compensating benefit.*

- **D10 — Mutation gates run filtered, always.** *Why: an unfiltered hang can only arise in the
  `Execution/` tests, so it cannot distinguish "mutation caught" from "unit tests went vacuous" — D11's
  own process note.*

- **D11 — Reviews run pre-commit, against the working-tree diff.** *Why: task 1's deliberate departure
  worked — 3 clean commits versus the predecessor's 6 `fix: N review findings` commits, each of which cost
  a SHA that reviews then cited.*

- **D12 — Artifacts are current at each commit boundary, not inside each commit.** *Why: `ai/active/` is
  gitignored by operator decision, so task 1's contract demanded a structural impossibility and scored it
  as met.*

- **D13 — Hard checkpoint after S4.** Stop, report, re-scope with the operator before S5. *Why: S0–S4 is
  plausibly a full task; running seven steps on one contract is how scope drift becomes invisible.*

- **D14 — Success criteria must be checkable against named evidence, not self-assessed.** Each criterion
  in the contract names what would falsify it. *Why: task 1 scored "no grab-bag record" as met while its
  own D9 recorded the hydrate site fabricating an unused `PaginationState`.*
