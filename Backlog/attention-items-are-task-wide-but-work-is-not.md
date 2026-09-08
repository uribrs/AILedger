attention items belong to the task and the work belongs to an item

`ArtifactRules.ValidateVerifierOutput` reads every attention id from the single current orchestration
plan, and demands the verifier's own body dispose each one:

    | id | final disposition | name | evidence |

there is nothing that scopes an attention item to a work item. so a task with two items either
carries the first item's attention items into the second item's plan, or drops them.

both options are wrong.

## what it forced

`2026-09-08_1048-refusal-journal` had six attention items for W1 and three for W2. carrying all nine
forward would have made W2's verifier write six `not-applicable` rows about work it never touched.
dropping them means the current plan no longer describes what the task has been paying attention to.

the task dropped them, and recorded why: six ceremonial dispositions per verifier output teaches an
agent that the table is paperwork, which is the opposite of what the gate exists to do. the history
survives only because a superseded artifact is kept — A5 holds W1's six, and A6 and A8 hold their
dispositions.

that is a workaround, not a design.

## the second cost, which compounds

an attention item can only be added by superseding the whole plan, and filing a plan needs an active
producer run that an operator can never close cleanly — see `the-coordinators-run-cannot-close`. so
each newly discovered attention item costs one superseded artifact plus one permanently untrue run
record.

the same task did that three times. A3 → A4 → A5 → A9, and runs R5, R7 and R12 all closed
`cancelled` having done their jobs.

the incentive that creates is the problem: the cheap way to record a newly found risk is to write it
into a constraint, where it reaches the manifest and no verifier is obliged to dispose it. the gate
pushes work toward the place it is not checked.

## the shape

give an attention item an optional work item, the way `work add` already takes `--depends-on` and
`artifact record` already takes `--work`. the validator then asks for dispositions of the items
scoped to the verifier's own item plus the unscoped ones.

the table gains a column and the plan stops being a shared mutable surface between items.

it does not fix the supersession cost. that is the other entry, and it is the one that decides
whether recording a risk is cheap.

## cost

one column, one filter in `ValidateVerifierOutput`, and its replay twin. the twin must keep accepting
plans whose tables have the old six columns, so the new column is optional by construction — which is
the safe shape the kernel already uses for every field added since.
