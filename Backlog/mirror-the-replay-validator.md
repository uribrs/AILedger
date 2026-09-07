decompose the replay validator to match, and pair the twins by folder

`CommandHandler` is 234 lines and sixteen named rule units. `TaskTransitionValidator` is still one
file of ~1700 lines. the pairing between them is the load-bearing invariant in this kernel — D13
protects the duplication deliberately, and the recalled lesson that opened the decomposition task
says the validator silently no-ops when a rule is written in one copy only.

so the structure is now lopsided: `ClaimRules.cs` is 155 lines and findable, and its twin is
somewhere inside 1700.

## what that already cost

the decomposition fixed the forward pointers, each unit naming its replay counterpart, and by doing
so made the reverse pointers stale: twelve comments in the validator read `Mirrors CommandHandler.X`
for members that had moved into a unit. a reader starting from a replay rule was sent to something
that no longer existed, which is worse than no comment because it reads as current.

both directions resolve now, by hand. nothing keeps them resolving.

worse, the way that was missed is instructive: the pairing was checked by grepping the validator for
references to the sixteen units, finding none, and concluding the duplication was intact. that grep
answers "is there a shared helper". it says nothing about whether either side's prose points at a
real member, and a comment is not compiled.

## the shape

decompose the validator the same way, one replay unit per command-time unit, then pair them by
concept rather than by layer:

    Rules/Claim/ClaimRules.cs          Rules/Claim/ClaimReplayRules.cs
    Rules/Work/WorkItemRules.cs        Rules/Work/WorkItemReplayRules.cs
    Rules/Stage/StageTransitionRules.cs  Rules/Stage/StageReplayRules.cs

this is the only folder layout here that buys something a filename cannot: **a folder with one file
in it is the defect**, visible from a directory listing without reading code. a missing twin stops
being a comment nobody checked and becomes a shape anybody notices.

flat is right for sixteen instances of one kind. it stops being right at thirty-two across two
kinds, which is what this change creates.

## the rule it must not break

no shared helper between the two sides. D13 accepts that a rule's message may be unpinnable at
command time and holds the invariant that the two copies *agree*, tested through the real file store.
a decomposition that tempts someone into extracting a common predicate defeats the thing it is
tidying. the folder pairing helps here too: the temptation is visible when the shared thing has to
live above both folders.

## do it as its own task

not alongside anything. changing both copies at once is the risk the two-copy discipline exists to
manage, and the attention item guarding it — `dual-kernel-rule-drift` — is the one finding a verifier
should be given room to hunt.
