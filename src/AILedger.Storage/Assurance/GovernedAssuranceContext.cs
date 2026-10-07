using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
using AILedger.Core.Authority;
using AILedger.Core.Domain;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Storage;

public sealed partial class FileGovernedTaskService
{
    // Every governed assurance call takes the task lock before the assurance journal lock.
    // The supplied state avoids nested GetState/inspection lock acquisition.
    public IGovernedAssuranceContext CreateAssuranceContext(GovernedTaskState initial, AssuranceSession session,
        bool providerSession, DateTimeOffset expiresAt, string? assuranceStore = null) => new AssuranceContext(this, initial, session, providerSession, expiresAt, assuranceStore);

    private sealed class AssuranceContext(FileGovernedTaskService service, GovernedTaskState initial,
        AssuranceSession session, bool providerSession, DateTimeOffset expiresAt, string? assuranceStore) : IGovernedAssuranceContext
    {
        public async Task<IGovernedAssuranceLease> AcquireAsync(AssurancePolicy policy, AssuranceSession actual, CancellationToken token)
        {
            Require(actual == session && policy.Governed?.TaskId == initial.TaskId.Value, "authorization_denied", "Assurance host binding mismatch.");
            var directory = service._pathResolver.Resolve(initial.TaskId);
            var gate = await service._mutationLock.AcquireAsync(Path.Combine(directory, service._layout.LockFileName), token).ConfigureAwait(false);
            try
            {
                var state = await service.ReplayAsync(initial.TaskId, directory, token).ConfigureAwait(false)
                    ?? throw new GovernanceException("Governed task is unavailable.");
                await service.CheckCoordinationAsync(directory, token).ConfigureAwait(false);
                Require(assuranceStore is not null, "missing_governed_host", "Governed assurance must identify its canonical store.");
                await CheckAssuranceCaseAsync(service.WorkspaceRoot, initial.TaskId, policy, assuranceStore!, token).ConfigureAwait(false);
                var lease = new AssuranceLease(gate, state, initial, session, providerSession, expiresAt, policy, ct => service.CheckCoordinationAsync(directory, ct));
                lease.EnsureCurrent();
                return lease;
            }
            catch { await gate.DisposeAsync().ConfigureAwait(false); throw; }
        }
    }

    private sealed class AssuranceLease(IAsyncDisposable gate, GovernedTaskState state, GovernedTaskState initial,
        AssuranceSession session, bool providerSession, DateTimeOffset expiresAt, AssurancePolicy policy,
        Func<CancellationToken, Task> checkOwner) : IGovernedAssuranceLease
    {
        public GovernedTaskState State => state;
        public async Task RevalidateAsync(CancellationToken token)
        {
            EnsureCurrent();
            await checkOwner(token).ConfigureAwait(false);
        }
        public void EnsureCurrent()
        {
            var actor = new ActorId(session.Principal);
            Require(DateTimeOffset.UtcNow < expiresAt && initial.Roles.TryGetValue(actor, out var old) &&
                state.Roles.TryGetValue(actor, out var assignment) && Hash(old) == Hash(assignment),
                "authorization_denied", "Governed assurance authority expired or was reassigned.");
            Require(state.Roles[actor].Capabilities.Contains(Capability.BuildContext), "authorization_denied", "Governed assurance requires current BuildContext capability.");
            var role = state.Roles[actor].Role;
            var assuranceRole = policy.Principals.Single(p => p.Id == session.Principal).Role;
            Require(assuranceRole switch
            {
                "acceptance" => !providerSession && role == RoleKind.Operator && state.Roles[actor].Capabilities.Contains(Capability.ManageWork),
                "review" => !providerSession && role is RoleKind.PlanningLead or RoleKind.Researcher,
                "verification" => role == RoleKind.Verifier,
                "synthesis" => role is RoleKind.PlanningLead or RoleKind.ImplementationLead or RoleKind.Operator,
                _ => false
            }, "incompatible_context", "Use a separately authorized requirements-aware context; blind review cannot receive these tools.");
            if (providerSession)
            {
                var authority = new AgentSessionAuthority(initial.TaskId, initial.Roles[actor], new(session.SessionId), session.Provider, expiresAt);
                authority.EnsureCurrent(state, DateTimeOffset.UtcNow);
            }
            foreach (var member in policy.Governed!.WorkItemIds)
            {
                var item = state.WorkItems.GetValueOrDefault(new(member));
                Require(item is not null && item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Abandoned or WorkItemStatus.Stale or WorkItemStatus.Blocked),
                    "wrong_governed_binding", "Governed assurance requires current nonterminal work.");
                var work = WorkItemRunObservations.LatestCompletedWork(state, new(member));
                Require(work?.ActorId.Value == policy.Implementer, "wrong_governed_binding", "Implementer attribution must match actual working provenance.");
            }
        }
        public ValueTask DisposeAsync() => gate.DisposeAsync();
    }
}
