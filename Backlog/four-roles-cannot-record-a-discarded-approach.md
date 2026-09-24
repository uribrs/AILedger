# Four roles cannot record a discarded approach

Open priority item 31. The refusal journal itself is delivered (completed item 5).

    Actor 'claude-impl' lacks capability 'RecordAlternative'.   ×4, two tasks, two days

`RoleDefaults` grants `RecordAlternative` to the two lead roles only. Worker, Researcher, Verifier and
CodeReviewer are all refused — four of the six non-operator roles. `CLAUDE.md` documents the command
with `--actor claude-impl`, a worker, in its own example, and the manifest tells a launched agent to
record what it discarded.

So the worker did the right thing four times and was refused four times. What is lost is the most
evidence-bearing kind of discarded approach there is: one an implementer actually tried. Recorded as
C28 with E38 and E39. `RoleDefaults.cs` sits in `src/AILedger.Cli`, held by item 6's W2, so it is
recorded rather than fixed on the spot.

`EnsureSafe` in the same file names the four capabilities deliberately withheld from non-operators —
`ManageRoles`, `ManageScope`, `ResolveEscalation`, `ManageConstraints`. `RecordAlternative` is not
among them, and the file carries no comment about it while the rest of this codebase comments every
rule. It reads as an omission, not a decision.

