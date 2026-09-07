using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Replay counterparts: ValidateConstraintAdded and ValidateConstraintSuperseded.
internal static class ConstraintRules
{
    internal static IReadOnlyList<LedgerEventData> AddConstraint(
        GovernedTaskState state,
        AddConstraintCommand command,
        DateTimeOffset now)
    {
        RequireId(command.ConstraintId.Value, nameof(command.ConstraintId));
        EnsureNew(state.Constraints, command.ConstraintId, "constraint");
        RequireText(command.Statement, nameof(command.Statement));
        RequireText(command.Source, nameof(command.Source));
        EnsureUnique(command.Scope, "Constraint scope entries", StringComparer.Ordinal);
    
        if (command.Scope.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("Constraint scope entries cannot be empty.");
        }
    
        var constraint = new Constraint(
            command.ConstraintId,
            command.Statement.Trim(),
            command.Source.Trim(),
            command.Scope.Select(entry => entry.Trim()).ToArray(),
            ConstraintStatus.Active,
            new Provenance(command.ActorId, now, "constraint.add"));
        return [new ConstraintAdded(constraint)];
    }

    internal static IReadOnlyList<LedgerEventData> SupersedeConstraint(
        GovernedTaskState state,
        SupersedeConstraintCommand command)
    {
        var constraint = Get(state.Constraints, command.ConstraintId, "constraint");
        if (constraint.Status != ConstraintStatus.Active)
        {
            throw new GovernanceException("Only an active constraint can be superseded.");
        }
    
        return [new ConstraintSuperseded(command.ConstraintId)];
    }
}
