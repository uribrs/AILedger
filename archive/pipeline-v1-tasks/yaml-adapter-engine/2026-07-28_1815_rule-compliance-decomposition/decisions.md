# Decisions

## D1 — C1 uses composed records, not one flat context

Three `internal` records in `Resilience/Contracts/Models/`, one per file: the response that came back
(`StatusCode`, `ResponseBody`, `Headers`); what rule evaluation needs (`ErrorHandling`, `PageState`,
`MaxInProcessDelay`); and the full classification context, which **composes** the second and adds
`Success`, `RecoveryConfig`, `Recovery`.

*Why:* the hydrate path (`IntegrationEngine.cs:1821`) needs only the rule subset. One flat record
would force it to pass nulls for members it has no opinion about — the grab-bag `CLAUDE.md` rule 5
names as the thing the rule exists to prevent. Falling back to a flat carrier is a stop-and-report,
not a fallback.

**Amended during C1 execution.** The third record does **not** hold the `CursorRecoveryTracker`, as
originally written here. The tracker lives in `Pagination/Logic/Recovery/` and the record lives in
`Resilience/Contracts/Models/`, so including it would make `Contracts/` depend on `Logic/` —
a direction `ARCHITECTURE.md` does not sanction (it grants `Contracts/` → other concepts'
`Contracts/` only). A parameter-count fix that introduces a layering violation is not a fix. The
tracker is a separate argument, so `ClassifyResponse` takes 3 parameters rather than 2. All five
targeted violations still close. Delivered shape:

| record | members | in |
|---|---|---|
| `ResponseSnapshot` | `StatusCode`, `ResponseBody`, `Headers` | `Resilience/Contracts/Models/` |
| `RuleEvaluationContext` | `ErrorHandling`, `PageState`, `MaxInProcessDelay` | same |
| `ClassificationContext` | `Rules`, `Success`, `RecoveryConfig` | same |

## D2 — C2 uses one record for the common core, not one per method

All four `IPaginator` members share `(PaginationConfig config, PaginationState state)`. A single
record covering that pair yields `ApplyToRequest(request, requestBody, ctx)` = 3,
`UpdateState(responseBody, headers, ctx)` = 3, `HasMorePages(ctx)` = 1,
`CreateInitialState(config)` unchanged.

*Why:* inventing separate request-side and response-side records would produce two carriers each
consumer only partly uses. One coherent pair is what actually recurs.

## D3 — C2's record is `public`, deliberately

`IPaginator` is `public` today, so a record in its signature must be too. This adds one type to a
public surface already flagged at 101 types.

*Why accepted:* the alternative is narrowing `IPaginator` to `internal`, which is the phase-3
public-surface question and explicitly out of scope. Note it and move on; do not smuggle phase 3 into
this task.

## D4 — `UpdateState`'s optional `responseHeaders` loses its default

Today `responseHeaders` is `= null` and its XML doc says the default exists so "existing callers and
tests need not supply it". Under D2, parameter order changes and the default cannot survive in the
same position.

*Why accepted:* an optional parameter that exists to spare 52 test call sites from being explicit is
the tail wagging the dog, and it is the mechanism by which a header-driven paginator can be tested
without ever exercising headers. Making it explicit is an improvement, not a cost — but it means C2
edits those 52 sites, which is why A1 gates the commit.

## D5 — Rule 3 and rule 4 counts do not move, and that is correct

Every method over 100 lines and every class over 500 lines is out of scope. The final report states
rule 3 at 3 and rule 4 at 3, unchanged, as a deliberate outcome rather than an omission.

*Why:* the three oversize items are `ExecuteOperationCoreAsync`, `WorkflowRunner.RunAsync` and
`YamlIntegrationLoader`. Each is a multi-day decomposition with real behavioural risk. Mixing one of
them into a low-risk parameter-object pass is how a "quality" branch becomes another week.

## D6 — Target totals corrected before execution

The brief handed down "74 → 52". That subtracts C1+C2+C3's 22 rule-5 closures but not C3's 3 rule-1
closures, which the same brief states separately as "rule 1 5 → 2". The arithmetic target is
**74 → 49**: rule 1 5→2, rule 3 3→3, rule 4 3→3, rule 5 63→41.

Two further corrections to the brief, from reading the source rather than the audit summary:

- `TryParseJoin` is **82 lines**, under the 100-line limit. It is a watch-band method, not a rule-3
  violation. C3 closes **zero** rule-3 violations.
- `TryParseJoin` takes **2** parameters, not 4. C3's single rule-5 closure is `TryParseExpression`,
  and only if reshaped (A2).

## D8 — `progress_log.md` is kept current at each commit boundary, not committed

`.gitignore:58` ignores `ai/active/`, on the operator's standing instruction ("ai/active stays
ignored, I have eyes on the repo"), reaffirmed on the predecessor branch when a task artifact was
pushed against intent and had to be force-reverted. So the directive "update `progress_log.md` **in**
the same commit as the work" is literally unsatisfiable for that file.

**Resolution:** `progress_log.md` is written before the first commit and brought current in the same
working-tree state as each commit — so at every commit boundary it describes work already done, never
work still pending. `CHANGELOG.md` **is** tracked and genuinely goes **in** each of the three
commits. `state.json` step statuses are likewise current at each boundary.

*Flagged for the operator rather than assumed:* if the intent was for the task directory to be
tracked this time, say so and it will be committed. Nothing here is pushed by default.

## D7 — Commit order is C1, C2, C3

C1 first: smallest, `internal`, zero test-file edits. C2 second: 52 test call sites, the largest
single edit. C3 last: it moves code between folders, which produces the noisiest diff and is easiest
to review in isolation.
