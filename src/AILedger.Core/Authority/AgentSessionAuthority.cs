using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Core.Authority;

/// <summary>Host-owned authority ceiling, rechecked under the storage mutation lock.</summary>
public sealed class AgentSessionAuthority(TaskId task, RoleAssignment assignment, RunId run,
    string provider, DateTimeOffset expiresAt)
{
    private readonly HashSet<Capability> _capabilities = assignment.Capabilities.ToHashSet();
    private int _revoked;

    public void Revoke() => Interlocked.Exchange(ref _revoked, 1);

    public void EnsureCurrent(GovernedTaskState? state, DateTimeOffset now)
    {
        if (Volatile.Read(ref _revoked) != 0 || now >= expiresAt)
            throw new GovernanceException("Provider host binding expired or was revoked.");
        if (state is null || state.TaskId != task ||
            !state.Runs.TryGetValue(run, out var active) || active.Status != AgentRunStatus.Active ||
            active.ActorId != assignment.ActorId || active.Provider != provider ||
            active.SubjectRole is { } role && role != assignment.Role ||
            !state.Roles.TryGetValue(assignment.ActorId, out var current) || current.Role != assignment.Role ||
            current.AssignedBy != assignment.AssignedBy || !_capabilities.SetEquals(current.Capabilities))
            throw new GovernanceException("Provider host binding no longer matches its task, run or assigned authority.");
    }

    public void EnsureAllowed(GovernedTaskState? state, LedgerCommand command, DateTimeOffset now)
    {
        EnsureCurrent(state, now);
        if (command.ActorId != assignment.ActorId || command.CorrelationId != run.Value || !Allowed(command))
            throw new GovernanceException("Command is outside the bound agent session's grants.");
    }

    private bool Allowed(LedgerCommand command) => command switch
    {
        AddEvidenceCommand evidence => !string.Equals(evidence.SourceType?.Trim(), "process-termination", StringComparison.Ordinal),
        AddClaimCommand or RecordAlternativeCommand or ResolveClaimCommand => true,
        RecordArtifactCommand artifact => artifact.ProducerRunId == run ||
            artifact.Kind == GovernedArtifactKind.CloseoutSynthesis && artifact.ProducerRunId is null,
        ConsultLessonsCommand consult => consult.RunId == run,
        RecordRoutingAssessmentCommand assessment => assessment.RunId == run,
        RaiseEscalationCommand => true,
        ProposeDecisionCommand => true,
        ResolveDecisionCommand => assignment.Role == RoleKind.PlanningLead,
        SelectPreparationCommand select => select.RunId == run,
        MarkLessonBearingCommand => true,
        DeclareProducerOutcomeCommand outcome => outcome.RunId == run,
        _ => false
    };
}

public sealed class AgentSessionCommandHandler(AgentSessionAuthority authority, ICommandHandler kernel)
    : ICommandHandler, IKernelIdentitySource
{
    public KernelBuildIdentity KernelIdentity => kernel is IKernelIdentitySource source
        ? source.KernelIdentity : RunningKernelIdentity.Current;

    public CommandOutcome Handle(GovernedTaskState? state, LedgerCommand command, DateTimeOffset now)
    {
        authority.EnsureAllowed(state, command, now);
        return kernel.Handle(state, command, now);
    }
}

public interface IAgentSessionServiceFactory
{
    IGovernedTaskService BindAgentSession(AgentSessionAuthority authority);
}
