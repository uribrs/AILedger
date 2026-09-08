let a claim be resolved by the command that earns it

183 of 329 claims are open. `open-claims-block-archive` argues for a refusal at Archive. this is the
other half, and it should ship first, because a gate in front of a two-command workflow just moves
the cost rather than removing it.

## the friction, precisely

earning a claim is two commands with different authority:

    ailedger evidence add   --id E20 --supports C20 ...     # implementation lead can do this
    ailedger claim resolve  --id C20 --status validated --evidence E20   # it cannot

the second needs `resolveClaim`, held by an operator and a planning lead. so the agent that found
the evidence stops there, correctly, and the resolution waits for a person who is not in the loop at
that moment. multiply by 183.

the capability split is right and should not change. a lead grading its own claim is the thing it
prevents. what is wrong is that the ungraded pile has no route anywhere.

## two changes, both subtractive

**one: let the resolving actor do it in one command.**

    ailedger evidence add --id E20 --supports C20 --resolves validated --actor operator

same authority check, same two events. an operator or planning lead recording evidence almost always
already knows the verdict, and today has to type the id three more times to say so. this does not
loosen anything — an actor without `resolveClaim` passing `--resolves` gets the same refusal it gets
now.

**two: let the pile be cleared in one pass.**

    ailedger claims --unresolved
      C41  open   2 supporting, 0 refuting   "The adapter discards the session on failure"
      C42  open   0 evidence                 "Replay accepts a history with no stage events"

    ailedger claim resolve --all-supported --actor operator
      → lists the 14 claims with supporting evidence and no refuting evidence, one confirmation

the ledger already knows which open claims have evidence pointing at them by direction. that is the
batch a person can actually review, and it turns 183 individual acts into a session that happens at
a natural pause. the claims with no evidence at all stay open, which is correct — those are the ones
that were never investigated and should look unfinished.

## why the batch is safe

`--all-supported` never invents a verdict. it validates only claims that already have at least one
`--supports` record and no `--refutes` record, it prints them before acting, and every resolution
still lands as its own `claim.resolved` event naming its evidence. it is a typing shortcut over
evidence the ledger already holds, not an inference.

anything with evidence on both sides is excluded and stays for a human, which is the only case where
judgement is actually required.

## cost

smaller than it looks, and it splits cleanly.

`claims --unresolved` and `claim resolve --all-supported` need **no kernel change at all**. both are
CLI-side: the query reads `GovernedTaskState`, and the batch is a loop issuing the
`ResolveClaimCommand` that already exists, once per claim, through the same `ExecuteAsync` and the
same authorization. nothing new to write twice, nothing to replay.

`evidence add --resolves` does touch the kernel — one command emitting two events, which
`ApplyEvents` already supports, plus an authorization check for both `addEvidence` and
`resolveClaim` on the acting actor. that half can wait; the CLI half addresses the 183 on its own.
