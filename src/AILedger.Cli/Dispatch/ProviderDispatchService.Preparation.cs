using AILedger.Cli.Providers;
using AILedger.Cli.Verification;
using AILedger.Core.Contracts;
using AILedger.Core.Application;
using AILedger.Core.Domain;

namespace AILedger.Cli.Dispatch;

internal sealed partial class ProviderDispatchService
{
    private async Task<PreparedDispatch> PrepareAsync(ProviderDispatchRequest input, DispatchProgress receipt, CancellationToken token)
    {
        receipt.AssurancePreparation = _options.AssuranceAuthority is null && _options.AssuranceStore is null
            ? AssurancePreparationStatus.NotConfigured : AssurancePreparationStatus.Requested;
        ValidateRequest(input);
        var provider = input.Provider.ToLowerInvariant();
        _ = DispatchAssuranceInput.Selection(input);
        ValidateResume(input);
        var state = await RequireStateAsync(service, input.TaskId, token).ConfigureAwait(false);
        if (input.Mode == AgentLaunchMode.Resume && state.Runs.Values.Any(run =>
            run.Assurance is not null && run.ProviderSessionId == input.SessionId))
            throw new GovernanceException("Frozen assurance runs require a fresh provider session; use provider launch.");

        var assurance = DispatchAssuranceInput.Binding(state, input);
        // One skill reading supplies both preflight and the eventual start command.
        var skills = await _contextPreparation.CurrentSkillsAsync(state, input, token).ConfigureAwait(false);
        ProviderGrants grants;
        ProviderIsolation isolation;
        try
        {
            var preview = CreateStartRun(input, provider, input.SessionId, null, "preflight", skills) with { Assurance = assurance };
            _authority?.EnsureLaunchAllowed(state, preview);
            grants = ResolveAdmittedGrants(state, input, preview, skills);
            isolation = await PrepareEnvironmentAsync(state, input, preview, grants, receipt, token).ConfigureAwait(false);
        }
        catch (DispatchPreparationException preparation) when (preparation.CompatibilityException is GovernanceException)
        {
            await JournalLaunchRefusalAsync(input, input.Mode, _options.LedgerRoot, state.Version,
                (GovernanceException)preparation.CompatibilityException).ConfigureAwait(false);
            throw;
        }
        catch (GovernanceException refusal)
        {
            await JournalLaunchRefusalAsync(input, input.Mode, _options.LedgerRoot, state.Version, refusal).ConfigureAwait(false);
            throw;
        }
        if (provider == AgentRun.NoProvider)
            throw new CliUsageException("Provider launch cannot dispatch provider 'none'; use run start for a manual record.");
        return new(input, provider, state, skills, assurance, grants, isolation);
    }

    private ProviderGrants ResolveAdmittedGrants(GovernedTaskState state, ProviderDispatchRequest input,
        StartRunCommand preview, IReadOnlyList<ContextSkill>? skills)
    {
        if (ProviderLaunchPreflight.RequiresFullAdmission(state, input.Subject, input.WorkItemId, preview.Assurance))
        {
            ProviderLaunchPreflight.EnsurePermitted(state, preview);
            return ProviderGrantResolver.Resolve(state, input.ActorId, input.WorkItemId,
                input.WorkingDirectory, input.AdditionalDirectories, _options.LedgerRoot, preview.Assurance);
        }
        // Legacy grants precede the narrower dispatch preview; preserve that refusal order.
        var grants = ProviderGrantResolver.Resolve(state, input.ActorId, input.WorkItemId,
            input.WorkingDirectory, input.AdditionalDirectories, _options.LedgerRoot);
        ProviderLaunchPreflight.EnsurePermitted(state, input.ActorId, input.SubjectActorId, skills,
            input.WithoutBriefReason, input.StaleBriefEvidence, input.WorkItemId);
        return grants;
    }

    private async Task<ProviderIsolation> PrepareEnvironmentAsync(GovernedTaskState state, ProviderDispatchRequest input,
        StartRunCommand preview, ProviderGrants grants, DispatchProgress receipt, CancellationToken token)
    {
        var isolation = ProviderIsolationPreparation.Create(grants, _options.LedgerRoot, _options.LessonRoot, _options);
        if (_hostService is not AILedger.Core.Authority.IAgentSessionServiceFactory)
            throw new DispatchPreparationException(DispatchFailureKind.PreparationUnsupported, "agent_binding_unsupported",
                new GovernanceException("Provider launch requires storage with trusted agent-session binding support."));
        await VerificationLaunchPreflight.EnsureAsync(grants.WorkingDirectory, _verificationHost(), token).ConfigureAwait(false);
        if (input.Mode == AgentLaunchMode.New)
        {
            ProviderLaunchPreflight.EnsurePermitted(state, preview);
            var discovery = await PrepareAssuranceAsync(_hostService, input, state, grants, _options.LedgerRoot, token).ConfigureAwait(false);
            if (discovery is not null) receipt.AssurancePreparation = AssurancePreparationStatus.Prepared;
            await _contextPreparation.PrepareLaunchAsync(state, input, preview, discovery, token).ConfigureAwait(false);
        }
        return isolation;
    }

    private void ValidateResume(ProviderDispatchRequest input)
    {
        if (input.Mode != AgentLaunchMode.Resume) return;
        if (_options.AssuranceAuthority is not null)
            throw new CliUsageException("Handoff assurance requires a fresh provider session; recover partial records through a new independent inspection rather than provider resume.");
        if (input.Candidate is not null || input.AdditionalWork.Count > 0 || input.VerifierRun is not null)
            throw new GovernanceException("Frozen assurance runs require a fresh provider session; use provider launch.");
        if (string.IsNullOrWhiteSpace(input.SessionId))
            throw new CliUsageException("Provider resume requires '--session' with the exact provider session ID.");
    }

    private static void ValidateRequest(ProviderDispatchRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Provider)) throw new CliUsageException("A provider is required.");
        if (!Enum.IsDefined(input.Mode)) throw new CliUsageException("Unknown provider launch mode.");
        _ = SafeRunId(input.RunId.Value);
    }
}
