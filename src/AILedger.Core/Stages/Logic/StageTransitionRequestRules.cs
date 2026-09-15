using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Roles;

namespace AILedger.Core.Stages;

internal static class StageTransitionRequestRules
{
    internal static StageTransitionRequest Validate(
        GovernedTaskState state,
        RequestStageTransitionCommand command)
    {
        StageTransitionPolicy.EnsureAllowed(state.Stage, command.TargetStage);

        var reason = ValidateReason(state.Stage, command.TargetStage, command.Reason);
        ValidateSerialTarget(state.Stage, command.TargetStage, command.SerialJustification);
        var waiver = ValidateWaiverText(command.WithoutPrerequisitesReason);
        ValidateSerialJustification(state, command.SerialJustification);
        ValidateWaiverAuthority(state, command.ActorId, waiver);

        return new StageTransitionRequest(reason, waiver);
    }

    private static string? ValidateReason(TaskStage current, TaskStage target, string? requestedReason)
    {
        var reason = TrimOrNull(requestedReason);
        if (StageTransitionPolicy.IsBackward(current, target))
        {
            if (requestedReason is null)
            {
                throw new GovernanceException(
                    $"Transition from '{current}' to '{target}' goes back in the pipeline " +
                    "and needs a reason. Pass --reason to record what was learned that sends the work back.");
            }

            if (reason is null)
            {
                throw new GovernanceException(
                    "A backward transition needs a reason; a blank reason records nothing.");
            }
        }
        else if (requestedReason is not null)
        {
            throw new GovernanceException(
                $"Transition from '{current}' to '{target}' goes forward and does not " +
                "take a reason. Only a transition that goes back records why.");
        }

        return reason;
    }

    private static void ValidateSerialTarget(
        TaskStage current,
        TaskStage target,
        AlternativeId? serialJustification)
    {
        if (serialJustification is null)
        {
            return;
        }

        if (target != TaskStage.Verification)
        {
            throw new GovernanceException(
                $"Transition from '{current}' to '{target}' cannot record a serial " +
                "justification. Serial justification applies only when entering 'Verification'.");
        }
    }

    private static void ValidateSerialJustification(
        GovernedTaskState state,
        AlternativeId? serialJustification)
    {
        if (serialJustification is { } alternativeId)
        {
            _ = CommandHandler.Get(state.Alternatives, alternativeId, "alternative");
        }
    }

    private static string? ValidateWaiverText(string? requestedWaiver)
    {
        var waiver = TrimOrNull(requestedWaiver);
        if (requestedWaiver is not null && waiver is null)
        {
            throw new GovernanceException(
                "Waiving stage prerequisites needs a reason; a blank waiver records nothing.");
        }

        return waiver;
    }

    private static void ValidateWaiverAuthority(
        GovernedTaskState state,
        ActorId actorId,
        string? waiver)
    {
        if (waiver is not null && !RoleAssignmentRules.IsOperator(state, actorId))
        {
            throw new GovernanceException("Only an operator can transition stages without prerequisites.");
        }
    }

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record StageTransitionRequest(string? Reason, string? Waiver);
