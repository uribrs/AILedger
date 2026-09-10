using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Command time only, and it has no replay counterpart on purpose.
//
// Every task in this ledger was created before context.built existed. A replay rule requiring one
// would make all of them permanently unreadable, which is the failure this repository has already
// produced twice. CommandHandler may tighten; TaskTransitionValidator may not. If a later change
// adds an arm keyed on context.built to the validator, that is the stop condition, not a repair.
//
// The two doors below are the other half of the same rule. A gate must be unbypassable by accident
// and openable on purpose with a record: this was the only hard refusal in the kernel with no
// override (C6), and within two hours of landing it locked every actor out of adding work and
// dispatching because another agent was rewriting the skills the hashes are taken over. An
// enforcement more rigid than the methodology it serves is a defect. What it must never become is
// optional, which is why each door leaves an event rather than a silent pass.
internal static class ContextGateRules
{
    // Returns the waiver to record when a door was used, and null when the brief itself satisfied
    // the gate. The caller emits it before the event it accompanies.
    internal static ContextBriefWaived? EnsureBriefed(
        GovernedTaskState state,
        ActorId actorId,
        IReadOnlyList<ContextSkill>? servedNow,
        string action,
        string? withoutBriefReason,
        EvidenceId? staleBriefEvidenceId)
    {
        var reason = TrimOrNull(withoutBriefReason);
        if (withoutBriefReason is not null && reason is null)
        {
            throw new GovernanceException(
                "Proceeding without a brief needs a reason; a blank waiver records nothing.");
        }

        // Naming both says which of the two failures was true of neither. They are different
        // failures and the kernel checks different things for each, so it will not guess.
        if (reason is not null && staleBriefEvidenceId is not null)
        {
            throw new GovernanceException(
                "An absent brief and a stale brief are different failures. Pass '--without-brief REASON' " +
                "to proceed with no brief, or '--with-stale-brief EVIDENCE-ID' to proceed on one that is " +
                "no longer current, but not both.");
        }

        // The operator door, for an absent brief. Evidence cannot discharge one, because there is
        // no evidence that an unread brief was read; only a decision can carry it, and the log
        // keeps that decision permanently. Nothing about the brief is examined here — that is the
        // point of the door — so it is answered before the state is read.
        if (reason is not null)
        {
            if (!RoleAssignmentRules.IsOperator(state, actorId))
            {
                throw new GovernanceException($"Only an operator can {action} without a brief.");
            }

            return new ContextBriefWaived(action, reason, null);
        }

        var briefed = state.ContextBuilds.TryGetValue(actorId, out var build);

        // The conditional door, for a stale brief. The kernel checks that the evidence record
        // exists and that there is a brief for it to be about; it never judges whether the reason
        // justifies proceeding, exactly as NotSplitJustification checks that an alternative exists
        // and never that it is a good one. That asymmetry is what makes the door usable under time
        // pressure while leaving a citation a later reader can check.
        if (staleBriefEvidenceId is { } evidenceId)
        {
            if (!briefed)
            {
                throw new GovernanceException(
                    $"Actor '{actorId}' has no context brief at all, so there is no stale brief to proceed " +
                    $"on and it cannot {action}. Run: ailedger context build --task {state.TaskId} --actor " +
                    $"{actorId} --cognitive-root <path>, or an operator may pass '--without-brief REASON'.");
            }

            _ = Get(state.Evidence, evidenceId, "evidence");
            return new ContextBriefWaived(action, null, evidenceId);
        }

        if (!briefed)
        {
            // Two routes, not three. The stale door is deliberately absent: it is refused for an
            // actor holding no brief, and a refusal that sends the reader to a second refusal is
            // the cost the doors exist to remove.
            throw new GovernanceException(
                $"Actor '{actorId}' has not built its context on this task and cannot {action}. " +
                $"Run: ailedger context build --task {state.TaskId} --actor {actorId} --cognitive-root <path>, " +
                "or an operator may pass '--without-brief REASON'.");
        }

        // Null means the caller could not read the cognitive layer, or did not try. That is a
        // refusal, not a pass. It was a pass once, and it made the gate optional: a caller who
        // moved --cognitive-root to an empty directory got the presence check alone and the
        // freshness half of the gate simply did not run (VC1). A brief that cannot be checked is
        // not a current brief, and the honest outcome of having no cognitive layer to be briefed
        // from is a refusal that says so.
        if (servedNow is null)
        {
            throw new GovernanceException(
                $"The skills the cognitive layer serves actor '{actorId}' cannot be read, so its brief " +
                $"cannot be shown to be current and it cannot {action}. Point '--cognitive-root' at the " +
                $"cognitive layer and run: ailedger context build --task {state.TaskId} --actor {actorId} " +
                $"--cognitive-root <path>. {OtherRoutes}");
        }

        // Nothing served and nothing recorded compare equal, which would let a readable but empty
        // cognitive layer satisfy a mandate about content. An actor served no skill has no brief to
        // be current, so it is refused on the same grounds as one that cannot be read.
        if (servedNow.Count == 0 || build!.Skills.Count == 0)
        {
            throw new GovernanceException(
                $"Actor '{actorId}' was served no skills, so it holds no brief that can be current and " +
                $"cannot {action}. Point '--cognitive-root' at the cognitive layer and run: " +
                $"ailedger context build --task {state.TaskId} --actor {actorId} --cognitive-root <path>. " +
                OtherRoutes);
        }

        if (ContextSkills.SameAs(build!.Skills, servedNow))
        {
            return null;
        }

        throw new GovernanceException(
            $"The skills served to actor '{actorId}' have changed since its context was built, so it " +
            $"cannot {action}. Run: ailedger context build --task {state.TaskId} --actor {actorId} " +
            $"--cognitive-root <path>. {OtherRoutes}");
    }

    // The routes out, for the three refusals where a brief exists and only its currency is in
    // question. A gate that names only the slow way out is how two hours went, so every one of them
    // names all three: rebuild the brief, an operator's decision, or a citation for proceeding on
    // the brief as it stands.
    private const string OtherRoutes =
        "Or an operator may pass '--without-brief REASON'. Or pass '--with-stale-brief EVIDENCE-ID' " +
        "naming an evidence record on this task for why proceeding on the brief as recorded is correct.";
}
