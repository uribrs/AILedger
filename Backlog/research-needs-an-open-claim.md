closing every claim locks a task out of Archive

`AllowedTransitions[Discovery] = { Research }`. Discovery has exactly one exit. And the Research arm
is:

    if (!state.Claims.Values.Any(claim => claim.Status == ClaimStatus.Open))
        throw new GovernanceException("Research requires at least one open claim to investigate.");

so a task that resolves every claim it recorded cannot leave Discovery, and therefore cannot reach
Verification, Review, Learn or Archive. It cannot mint a lesson and it cannot close.

## hit twice in one session

both tasks worked today ran into it, in the same place: claims all resolved, `stage transition
--stage research` refused, and the only way forward was to open a further claim. on the second task
that claim was this finding, which is at least honest.

## why it is worth fixing rather than remembering

it punishes the exact discipline the kernel asks for. `open-claims-block-archive` argues Archive
should refuse while claims are open. combined with this arm, the two rules meet in the middle and
leave no legal state: too many open claims and Archive refuses, none and Research refuses. that entry
should not land until this one does.

it also reads backwards. an open claim is a question the task has not answered; requiring one to
*enter* Research is right. requiring one to enter Research **on the only path out of Discovery** makes
it a tax on every task, including the ones with nothing left to investigate.

## the shape

the arm is asking the wrong question. it wants "this task has something to research", and it tests
"this task has an unanswered claim", which is a different thing once the claims are answered.

- **narrowest fix:** the arm passes when the task has *ever* had an open claim, not only when one is
  open now. `state.Claims.Count != 0` is the honest predicate — a task with claims has done the
  recording the stage exists to check, and whether they are still open is the Archive arm's business.
- **alternative:** allow `Discovery -> Design` directly, so a task with nothing to research is not
  routed through a stage that has nothing to do. this is a change to the stage graph rather than to
  an arm, and it needs a view on whether Design's own arm (a completed Researcher run) then becomes
  unreachable — it would, so this alternative only works paired with relaxing that one too.

the first is one predicate and no graph change. prefer it.

## replay

command-time only, per LD16. the arms are not written into `TaskTransitionValidator` and the comment
there says why: they encode a methodology still being changed, and replay must accept every history
that was ever legal. loosening an arm is the safe direction anyway.

## cost

one predicate. the test that has to be seen to fail is a task with resolved claims transitioning to
Research, which is the shape both of today's tasks were in.
