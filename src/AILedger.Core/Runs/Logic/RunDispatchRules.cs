using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.ContextBriefing;
using AILedger.Core.Domain;
using AILedger.Core.Roles;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Runs;

// Owns admission and refusal ordering before a run is dispatched.
internal static class RunDispatchRules
{
    // The refusals a provider launch must produce before it has cost anything, in the order they
    // have to speak. StartRun calls this, and so does the launcher before it resolves an adapter or
    // runs a provider binary for its version — one implementation, two moments. It was two moments
    // and no shared implementation that let an unbriefed actor spawn a process and only then be
    // refused (VC3).
    //
    // Returns the brief waiver to record when a door was opened, and null otherwise. The pre-flight
    // discards it; StartRun records it with the run.
    //
    // workItemId is the item the run would be held against, and the two refusals below that read it
    // are the item's own status and the subject's role, in that order. A caller that passes none
    // asks for the refusals that do not depend on an item.
    internal static ContextBriefWaived? EnsurePermitted(
        GovernedTaskState state,
        ActorId actorId,
        ActorId? subjectActorId,
        IReadOnlyList<ContextSkill>? servedNow,
        bool isProviderLaunch,
        WorkItemId? workItemId = null,
        string? withoutBriefReason = null,
        EvidenceId? staleBriefEvidenceId = null)
    {
        // A run is authorised by one actor and worked by another only when an operator dispatches
        // it. Starting the run under the working actor is what made the roles that hold no run
        // authority — researcher, worker, verifier, code reviewer — impossible to launch at all.
        // Dispatch separates the two without handing those roles authority they must not have.
        var subject = subjectActorId ?? actorId;
        if (subject != actorId)
        {
            if (!RoleAssignmentRules.IsOperator(state, actorId))
            {
                throw new GovernanceException(
                    $"Only an operator can start a run on another actor's behalf; '{actorId}' cannot " +
                    $"dispatch for '{subject}'.");
            }

            if (!state.Roles.ContainsKey(subject))
            {
                throw new GovernanceException($"Run subject '{subject}' has no assigned role.");
            }
        }

        // A provider launch, and only a provider launch. The launcher is the one caller that sets
        // LaunchTokenHash, so it is what tells a dispatch from a run started by hand (IC3), and a
        // dispatch is the other moment the pipeline is supposed to have been read first.
        //
        // Placed after the dispatch-authority checks above, not before them. An actor that may
        // never dispatch at all should be told that, not told to go and build context first: the
        // refusal an actor reads is the instruction it acts on, so the gate that cannot be
        // satisfied has to speak before the gate that can.
        ContextBriefWaived? briefWaiver = null;
        if (isProviderLaunch)
        {
            briefWaiver = ContextGateRules.EnsureBriefed(
                state, actorId, servedNow, "launch a provider",
                withoutBriefReason, staleBriefEvidenceId);
        }
        // A run started by hand is not gated, so it cannot be waived either. A door offered where
        // there is no gate would record a waiver for a refusal that never happens, and a log full
        // of waivers nothing needed is how an override stops being read as a decision.
        else if (withoutBriefReason is not null || staleBriefEvidenceId is not null)
        {
            throw new GovernanceException(
                "A run started by hand is not subject to the context gate, so there is nothing to waive. " +
                "The doors are for 'provider launch' and 'work add'.");
        }

        // Code review output is necessarily about one work item: it is filed against that item and
        // can only follow the verifier that established the item is ready to review. Admitting a
        // task-wide reviewer therefore creates a run that cannot produce its required artifact.
        // Keep this in the shared dispatch rule so provider pre-flight and command-time admission
        // refuse the same impossible run with the same instruction.
        if (workItemId is null &&
            state.Roles.TryGetValue(subject, out var taskWideSubjectAssignment) &&
            taskWideSubjectAssignment.Role == RoleKind.CodeReviewer)
        {
            throw new GovernanceException(
                "A CodeReviewer run must name a work item because CodeReviewOutput is work-scoped. " +
                "Start it with --work after that item's verifier run has completed.");
        }

        // A coordinating role plans the work and dispatches it; it does not do it. Without this the
        // lead's own run counted as the item's working pass, so 'work complete' was satisfied by a
        // pass in which nobody worked. Stated as an action rather than a flag because there is no
        // door: the only route past a missing working run stays the operator's
        // 'work complete --without-verification REASON', which records its reason permanently.
        //
        // Here rather than in StartRun, which is where it was first written. There it sat past the
        // pre-flight, so a provider launch resolved an executable and ran the provider binary for
        // its version before the refusal spoke — the exact cost the pre-flight exists to avoid, and
        // the second time one refusal living at only one of the two moments produced it (VC3). The
        // decision behind the rule says it is refused at dispatch, and the pre-flight is dispatch.
        //
        // Placed after the two refusals above for the reason stated there: an actor that may never
        // dispatch, or has never been briefed, must read that first. The move brought the item's
        // status check with it rather than leaving it behind in StartRun, because the same ordering
        // rule puts the status ahead of the role: see the block below.
        //
        // Command-time only. SubjectRole is null on every run recorded before the field existed, so
        // a replay rule keyed on it would refuse histories that were legal when written, and this
        // ledger alone holds 16 work items closed with a coordinating run as their only working run.
        // RunEventValidator.ValidateStarted deliberately re-derives none of it.
        if (workItemId is { } item)
        {
            // The item's own state speaks first, for the reason the two refusals above are ordered
            // as they are. A Blocked, Stale, Completed or Abandoned item accepts no run from any
            // subject; a coordinating subject is a property of the dispatch, and the caller changes
            // it by dispatching a Worker. Refused the other way round, the instruction an operator
            // reads — dispatch a Worker against the item — points at an item that is gone.
            //
            // The only statement of this rule at command time. StartRun reaches this function on
            // every path, with its own work item id, so the copy that used to stand below the
            // lookup there would now only be a second model of a rule that already exists (KC1).
            // Its replay twin in RunEventValidator.ValidateStarted is unchanged, and the
            // set of histories refused is the same set: only which refusal speaks first has moved.
            var workItem = Get(state.WorkItems, item, "work item");
            if (workItem.Status is WorkItemStatus.Blocked or WorkItemStatus.Stale or
                WorkItemStatus.Completed or WorkItemStatus.Abandoned)
            {
                throw new GovernanceException($"Cannot start a run for work item in status '{workItem.Status}'.");
            }

            if (state.Roles.TryGetValue(subject, out var subjectAssignment) &&
                subjectAssignment.Role is RoleKind.Operator or RoleKind.PlanningLead or RoleKind.ImplementationLead)
            {
                throw new GovernanceException(
                    $"A run against work item '{item}' cannot be held by a coordinating role, and " +
                    $"'{subject}' is a {subjectAssignment.Role}. Dispatch a Worker, Researcher, Verifier or " +
                    "CodeReviewer against the item, or start this run without --work to file task-wide " +
                    "artifacts.");
            }
        }

        return briefWaiver;
    }
}
