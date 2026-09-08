a code reviewer's approval never expires

the completion gate is time-aware. the Learn arm is not.

    work complete    → WorkItemRules.HasVerifierRunAfterLatestWork
    stage Learn      → completedRoles.Contains(RoleKind.CodeReviewer)

`completedRoles` is every completed run's subject role with no ordering at all. so a reviewer run
that finished before three repair cycles still satisfies the arm, and the code that reaches Archive
is not the code anyone reviewed.

## measured, not hypothetical

`2026-09-08_1048-refusal-journal`:

    R9   code reviewer   completed, found RC1
    R10  worker          repaired RC1 — public method became internal, plus InternalsVisibleTo
    R11  verifier        passed, re-verified the repair independently
         work complete   accepted

the reviewer that approved W1 never saw the assembly attribute or the visibility change it had
itself asked for. the kernel raised no objection, because it had none to raise.

the verifier half worked exactly as intended in the same task: R8's PASS stopped counting the moment
R10 touched the code, which is why R11 existed at all. one of the two checks knows about time.

## why this is not paranoia

the asymmetry rewards the wrong order. a lead that wants to finish can review early, repair late, and
archive with an approval that describes different code — without waiving anything, without an
operator flag, and without a line in the log saying so. `waivers-need-a-floor` exists because the
one visible hole through the arms should be worth reading; this one is invisible.

`review-before-complete` is the sibling entry and the opposite failure: completing an item makes its
review impossible. together they say the reviewer's position in the order is not actually pinned at
either end.

## the shape

mirror the predicate that already exists. `HasVerifierRunAfterLatestWork` is in `WorkItemRules` and
is asked by the completion gate; the Learn arm needs the same question about `CodeReviewer`.

command time only. replay must keep accepting every task already archived on a stale approval, and
there are some.

one caveat worth stating rather than discovering: a repair that only lands the change the reviewer
demanded will then require a second review of the reviewer's own instruction. that is a real cost
and it is the reason this entry is not simply "add the check" — decide whether the arm asks for a
review after the latest *work*, or after the latest work that was not itself a review finding. the
first is simpler and the kernel cannot tell the difference between the two.

## cost

one predicate, already written, asked in a second place. plus the decision above, which is the part
that needs thinking rather than typing.
