using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
namespace AILedger.Core.Authority;

public sealed record PreparationRole(ActorId Actor, RoleKind Role, IReadOnlyList<Capability> Capabilities);
public sealed record PreparationWork(string Profile, WorkItemId Work, string Title, ActorId Owner,
    IReadOnlyList<string> Scope, string? BaseRef = null);

public sealed class PreparationAuthority
{
    private readonly RoutineOrchestrationAuthority _delegation;
    private readonly IReadOnlyList<PreparationRole> _roles;
    private readonly IReadOnlyList<PreparationWork> _work;
    public PreparationAuthority(RoutineOrchestrationAuthority delegation, IReadOnlyList<PreparationRole> roles, IReadOnlyList<PreparationWork> work)
    {
        _delegation = delegation;
        _roles = roles.Select(r => r with { Capabilities = r.Capabilities.ToArray() }).ToArray();
        _work = work.Select(w => w with { Scope = w.Scope.ToArray() }).ToArray();
        if (_roles.Any(r => r.Role == RoleKind.Operator || r.Capabilities.Any(c => c is Capability.ManageRoles or Capability.ManageScope or Capability.ManageConstraints or Capability.ResolveEscalation)) ||
            _work.Any(w => w.Scope.Count != 1 || !Path.IsPathFullyQualified(w.Scope[0])) ||
            _work.Select(w => w.Profile).Distinct().Count() != _work.Count)
            throw new ArgumentException("Preparation policy requires non-operator roles and unique single-scope work profiles.");
    }
    internal void EnsureAllowed(GovernedTaskState? state, LedgerCommand command, DateTimeOffset now)
    {
        _delegation.EnsureCurrent(state, command.ActorId, now);
        if (command is AssignRoleCommand role && !state!.Roles.ContainsKey(role.TargetActorId) &&
            _roles.Any(r => r.Actor == role.TargetActorId && r.Role == role.Role && r.Capabilities.ToHashSet().SetEquals(role.Capabilities))) return;
        if (command is AddWorkItemCommand work && work.WithoutBriefReason is null && work.StaleBriefEvidenceId is null && work.NotSplitJustification is null)
        {
            var profile = _work.SingleOrDefault(p => p.Work == work.WorkItemId && p.Owner == work.Owner && p.Title == work.Title &&
                p.Scope.SequenceEqual(work.ResourceScope) && p.BaseRef == work.BaseRef);
            var run = state!.Runs.Values.Where(r => r.PreparationSelection is not null).OrderByDescending(r => r.StartedAt).FirstOrDefault();
            if (profile is not null && run?.Status == AgentRunStatus.Completed &&
                state.Roles.TryGetValue(run.ActorId, out var producer) && producer.Role == RoleKind.PlanningLead && producer.AssignedBy.RecordedAt <= run.StartedAt &&
                run.PreparationSelection is { } selection &&
                selection.Profiles.Contains(profile.Profile))
            {
                PreparationSelectionRules.EnsureProducerCurrent(state, run);
                PreparationSelectionRules.EnsureApplicable(state, selection);
                if (state.Decisions[selection.Decision].DependsOnClaims.ToHashSet().SetEquals(work.DependsOnClaims)) return;
            }
        }
        throw new GovernanceException("Operation does not match current admitted selection and preauthorized preparation policy.");
    }
}

public sealed class PreparationCommandHandler(PreparationAuthority authority, ICommandHandler kernel) : ICommandHandler, IKernelIdentitySource
{
    public KernelBuildIdentity KernelIdentity => kernel is IKernelIdentitySource source ? source.KernelIdentity : RunningKernelIdentity.Current;
    public CommandOutcome Handle(GovernedTaskState? state, LedgerCommand command, DateTimeOffset now)
    { authority.EnsureAllowed(state, command, now); return kernel.Handle(state, command, now); }
}
