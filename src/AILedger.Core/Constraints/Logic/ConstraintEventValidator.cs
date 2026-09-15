using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Domain.ReplayValidationRules;

namespace AILedger.Core.Constraints;

// Replay validates the event shape that command-time ConstraintRules emits. These checks remain
// separate because replay must continue accepting every history that was legal.
internal static class ConstraintEventValidator
{
    internal static void ValidateAdded(
        GovernedTaskState state,
        LedgerEvent @event,
        Constraint constraint)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageConstraints, operatorRequired: true);
        EnsureNew(state.Constraints, constraint.Id, "constraint");
        RequireId(constraint.Id.Value, nameof(constraint.Id));
        RequireText(constraint.Statement, nameof(constraint.Statement));
        RequireText(constraint.Source, nameof(constraint.Source));
        RequireDefined(constraint.Status, nameof(constraint.Status));
        if (constraint.Status != ConstraintStatus.Active)
        {
            throw new GovernanceException("A newly added constraint must be active.");
        }

        EnsureUnique(constraint.Scope, "Constraint scope entries", StringComparer.Ordinal);
        if (constraint.Scope.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("Constraint scope entries cannot be empty.");
        }

        ValidateProvenance(@event, constraint.Provenance, "constraint.add");
    }

    internal static void ValidateSuperseded(
        GovernedTaskState state,
        LedgerEvent @event,
        ConstraintSuperseded superseded)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageConstraints, operatorRequired: true);
        var constraint = Get(state.Constraints, superseded.ConstraintId, "constraint");
        if (constraint.Status != ConstraintStatus.Active)
        {
            throw new GovernanceException("Only an active constraint can be superseded.");
        }
    }
}
