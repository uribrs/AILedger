using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
using AILedger.Core.Handoffs;
using AILedger.Providers.Assurance;
using AILedger.Providers.Process;
using AILedger.Storage.Episodes;

namespace AILedger.Cli.Assurance;

public sealed class GovernedCompletionHost(string authorityPath, string storePath, string principal,
    string ledgerRoot) : IWorkCompletionAdmission
{
    public async Task<IWorkCompletionLease> AcquireAsync(GovernedTaskState state, WorkItemId work, CancellationToken token)
    {
        var source = new CompletionPolicySource(authorityPath);
        var policy = await source.ReadAsync(token).ConfigureAwait(false);
        AssuranceValidation.Policy(policy);
        await AILedger.Storage.FileGovernedTaskService.CheckAssuranceCaseAsync(ledgerRoot, state.TaskId, policy, storePath, token).ConfigureAwait(false);
        var roots = state.WorkItems[work].ResourceScope;
        AssuranceHost.EnsureProtectedPaths(authorityPath, storePath, roots.Append(ledgerRoot));
        AssuranceHost.EnsureInputGrants(AssuranceConfiguration.InputPaths(policy, principal), roots, ledgerRoot);
        var service = new AssuranceService(new FileEpisodeStore(storePath), source, policy,
            new(principal, "completion-observer", "trusted-host", null), new SystemProcessRunner());
        return await service.AcquireCompletionAsync(state, work, token).ConfigureAwait(false);
    }
    private sealed class CompletionPolicySource(string path) : IAssurancePolicySource
    {
        public async Task<AssurancePolicy> ReadAsync(CancellationToken token)
        {
            if (!Path.IsPathFullyQualified(path) || new FileInfo(path).LinkTarget is not null || new FileInfo(path).Length > 65536)
                throw new ArgumentException("Completion policy requires a protected absolute bounded file.");
            return HandoffJson.ParseDocument<AssurancePolicy>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
        }
    }
}
