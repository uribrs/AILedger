the Ready arm proves a name was typed, not that anyone did the work

`StageTransitionRules.EnsureStagePrerequisites` guards Ready with three staffing refusals — a
working role, a Verifier, a CodeReviewer — and all three are satisfied by an `actor attach` and
nothing else:

    var assignedRoles = state.Roles.Values.Select(assignment => assignment.Role).ToHashSet();
    if (!assignedRoles.Any(IsWorkingRole)) throw ...
    if (!assignedRoles.Contains(RoleKind.Verifier)) throw ...
    if (!assignedRoles.Contains(RoleKind.CodeReviewer)) throw ...

No run is consulted. The arm exists to prove the roles the pipeline needs are staffed, and what it
actually proves is that someone typed three names.

The distinctness the three messages promise — "assigned to a distinct actor" — does hold, but by
construction rather than by check: `state.Roles` is keyed by `ActorId` and `RoleAssignment` carries
one `Role`, so three roles need three actors. Only engagement is missing.

## how it was found

On `2026-09-09_0812-cortex-asset-duplication`, Ready passed with:

    Worker              probe-worker              engaged=False
    ImplementationLead  probe-implementationLead  engaged=False
    CodeReviewer        probe-codeReviewer        engaged=False

Those three actors exist because `actor attach` has no dry run and `--help` names `--role ROLE`
without listing the values, so discovering the vocabulary meant attaching one actor per candidate
name into an append-only log. Seven survive on that task. Two of them are `probe-planningLead` and
`probe-planning-lead`: both spellings are accepted, so one role answers to two names.

The coordinator that hit this then staffed the real roles anyway and dispatched real runs — but
nothing required that, and the arm had already been satisfied by the accidents.

## the kernel already knows the answer

`who` computes engagement per role and prints it, and it means *a completed run*: on that same task
Verifier read `engaged=False` while run `RV1` existed, because `RV1` failed. The concept, the
computation and the display are all present. The arm is the only place that does not ask.

## what it should do

Ask for engagement, not assignment — but Ready is the wrong place to demand a *completed* run,
because at Ready no work has started. The honest shape is narrower:

* refuse an assignment that names an actor with no capability to hold the role's runs, and
* keep the staffing refusal but read it from the same projection `who` uses, so the arm and the
  view can never disagree.

The stronger form — that each staffed role has actually carried a run — belongs at the arms that
follow, where a run is a reasonable thing to have. Verification already asks for a completed
working run and Review for a completed Verifier run; the gap is that Ready lets a name stand in for
a plan, and nothing later checks that the *staffed* actors are the ones who ran.

Related: 29 (`the-arms-only-fire-if-you-walk-through-them`) — an arm that can be passed by decoration
and an arm that is never reached are the same hole from two sides. Also 22
(`the-reviewers-approval-goes-stale`), which is the same class: a gate asking whether *some* run of a
kind exists rather than whether the right one does.

Command-time only, per LD16: `TaskTransitionValidator` holds no Ready case, and a replay-side copy
would be wrong here since it must accept every history that was ever legal.

## cheap half, if the whole is not wanted

Give `actor attach` a dry run, or list the valid roles in `--help`. The junk actors that satisfied
this arm existed only because the vocabulary could not be discovered any other way.
