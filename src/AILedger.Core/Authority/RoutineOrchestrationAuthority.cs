using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Core.Authority;

/// <summary>
/// A host-owned, task-bound ceiling on routine orchestration. Construct only in trusted
/// composition; neither a serialized command nor an actor name can create this authority.
/// This is not an agent role and it does not replace kernel admission.
/// </summary>
public sealed class RoutineOrchestrationAuthority
{
    private readonly TaskId _task;
    private readonly ActorId _actor;
    private readonly HashSet<Capability> _capabilities;
    private readonly DateTimeOffset _expiresAt;
    private readonly Provenance _assignedBy;
    private int _revoked;

    public RoutineOrchestrationAuthority(GovernedTaskState state, ActorId actor, DateTimeOffset expiresAt)
    {
        if (!state.Roles.TryGetValue(actor, out var assignment) || assignment.Role != RoleKind.Operator)
            throw new GovernanceException("Routine orchestration requires a trusted operator delegation.");
        _task = state.TaskId;
        _actor = actor;
        _capabilities = assignment.Capabilities.ToHashSet();
        _expiresAt = expiresAt;
        _assignedBy = assignment.AssignedBy;
    }

    public void EnsureLaunchAllowed(GovernedTaskState state, StartRunCommand command) =>
        EnsureAllowed(state, command, DateTimeOffset.UtcNow);

    public void Revoke() => Interlocked.Exchange(ref _revoked, 1);

    internal void EnsureAllowed(GovernedTaskState? state, LedgerCommand command, DateTimeOffset now)
    {
        EnsureCurrent(state, command.ActorId, now);
        if (!IsRoutine(command))
            throw new GovernanceException("Operation requires a separate trusted human or execution-host path.");
    }

    internal void EnsureCurrent(GovernedTaskState? state, ActorId actor, DateTimeOffset now)
    {
        if (Volatile.Read(ref _revoked) != 0 || now >= _expiresAt)
            throw new GovernanceException("Orchestration authority has expired or been revoked.");
        if (state is null || state.TaskId != _task || actor != _actor)
            throw new GovernanceException("Command does not match the trusted orchestration binding.");
        if (!state.Roles.TryGetValue(_actor, out var assignment) || assignment.Role != RoleKind.Operator ||
            assignment.AssignedBy != _assignedBy || !_capabilities.SetEquals(assignment.Capabilities))
            throw new GovernanceException("Delegating authority changed; obtain a new host binding.");
    }

    private static bool IsRoutine(LedgerCommand command) => command switch
    {
        StartRunCommand start => start.WithoutBriefReason is null && start.StaleBriefEvidenceId is null &&
            start.LaunchTokenHash is not null,
        // Successful completion still requires the launcher's secret in the owning kernel rule.
        CompleteRunCommand complete => complete.LaunchToken is not null,
        RequestStageTransitionCommand transition => transition.WithoutPrerequisitesReason is null,
        RecordContextBuiltCommand => true,
        StartCoordinatorSessionCommand or CompleteCoordinatorSessionCommand => true,
        CompleteWorkItemCommand complete => complete.WithoutVerificationReason is null && complete.Admission is not null,
        _ => false
    };
}

/// <summary>Install at the storage command boundary so admission uses the locked, replayed state.</summary>
public sealed class RoutineOrchestrationCommandHandler(
    RoutineOrchestrationAuthority authority, ICommandHandler kernel) : ICommandHandler, IKernelIdentitySource
{
    public KernelBuildIdentity KernelIdentity => kernel is IKernelIdentitySource source
        ? source.KernelIdentity : RunningKernelIdentity.Current;

    public CommandOutcome Handle(GovernedTaskState? state, LedgerCommand command, DateTimeOffset now)
    {
        authority.EnsureAllowed(state, command, now);
        return kernel.Handle(state, command, now);
    }
}
