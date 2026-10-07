using AILedger.Core.Application;
using AILedger.Core.Authority;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
namespace AILedger.Cli.Orchestration;

public sealed record DriverPreparationPolicy(IReadOnlyList<PreparationRole> Roles, IReadOnlyList<PreparationWork> Work);

internal sealed class DriverPreparation
{
    private readonly IGovernedTaskService _service;
    private readonly DriverHostOptions _options;
    private readonly IContextAssembler _context;
    internal DriverPreparation(FileGovernedTaskService host, RoutineOrchestrationAuthority authority, DriverHostOptions options, IContextAssembler context)
    {
        _options = options; _context = context;
        _service = host.BindPreparation(new(authority, options.Preparation!.Roles, options.Preparation.Work));
    }
    internal async Task StaffAsync(CancellationToken token)
    {
        foreach (var profile in _options.Preparation!.Roles)
        {
            var state = await StateAsync(token).ConfigureAwait(false);
            if (state.Roles.TryGetValue(profile.Actor, out var existing))
            {
                if (existing.Role != profile.Role || !profile.Capabilities.ToHashSet().SetEquals(existing.Capabilities))
                    throw new GovernanceException("Existing role differs from trusted preparation policy; no automatic promotion or replacement.");
                continue;
            }
            await _service.ExecuteAsync(_options.TaskId, new AssignRoleCommand(_options.ActorId, null, "driver-staff", profile.Actor,
                profile.Role, profile.Capabilities) { ExpectedVersion = state.Version }, token).ConfigureAwait(false);
        }
    }
    internal async Task PrepareWorkAsync(CancellationToken token)
    {
        var state = await StateAsync(token).ConfigureAwait(false);
        var run = state.Runs.Values.Where(r => r.PreparationSelection is not null).OrderByDescending(r => r.StartedAt).FirstOrDefault();
        if (run?.Status != AgentRunStatus.Completed) throw new GovernanceException("No completed planning selection; dispatch Scope planning before preparation.");
        var selection = run.PreparationSelection!;
        PreparationSelectionRules.EnsureProducerCurrent(state, run);
        PreparationSelectionRules.EnsureApplicable(state, selection);
        foreach (var name in selection.Profiles)
        {
            var profile = _options.Preparation!.Work.SingleOrDefault(p => p.Profile == name)
                ?? throw new GovernanceException("Selected work profile is not preauthorized.");
            state = await StateAsync(token).ConfigureAwait(false);
            if (state.WorkItems.TryGetValue(profile.Work, out var existing))
            {
                if (existing.Owner != profile.Owner || !existing.ResourceScope.SequenceEqual(profile.Scope) ||
                    !existing.DependsOnClaims.ToHashSet().SetEquals(state.Decisions[selection.Decision].DependsOnClaims) ||
                    existing.Status is WorkItemStatus.Stale or WorkItemStatus.Blocked)
                    throw new GovernanceException("Prepared work differs from the current selection; bounded replanning is required.");
                continue;
            }
            var artifacts = await new CognitiveArtifactLoader().LoadAsync(_options.Dispatch.CognitiveRoot, token).ConfigureAwait(false);
            var brief = _context.Build(state, _options.ActorId, null, artifacts, DateTimeOffset.UtcNow);
            await _service.ExecuteAsync(_options.TaskId, new AddWorkItemCommand(_options.ActorId, null, "driver-prepare", profile.Work,
                profile.Title, profile.Owner, state.Decisions[selection.Decision].DependsOnClaims, profile.Scope,
                BaseRef: profile.BaseRef, SkillsServedNow: ContextSkills.From(brief.Artifacts)) { ExpectedVersion = state.Version }, token).ConfigureAwait(false);
        }
    }
    internal void ValidateSelectedWork(GovernedTaskState state, WorkItemId work)
    {
        var run = state.Runs.Values.Where(r => r.PreparationSelection is not null).OrderByDescending(r => r.StartedAt).FirstOrDefault();
        if (run?.Status != AgentRunStatus.Completed || run.PreparationSelection is not { } selection)
            throw new GovernanceException("No admitted planning selection applies to this work.");
        PreparationSelectionRules.EnsureProducerCurrent(state, run);
        PreparationSelectionRules.EnsureApplicable(state, selection);
        var profile = _options.Preparation!.Work.SingleOrDefault(p => p.Work == work && selection.Profiles.Contains(p.Profile));
        if (profile is null || !state.WorkItems.TryGetValue(work, out var item) || item.Owner != profile.Owner ||
            !item.ResourceScope.SequenceEqual(profile.Scope) || !item.DependsOnClaims.ToHashSet().SetEquals(state.Decisions[selection.Decision].DependsOnClaims))
            throw new GovernanceException("Selected work no longer matches the admitted bounded profile.");
    }

    internal WorkItemId? SingleSelectedWork(GovernedTaskState state)
    {
        var selected = state.Runs.Values.Where(r => r.PreparationSelection is not null).OrderByDescending(r => r.StartedAt).FirstOrDefault()?.PreparationSelection;
        if (selected?.Profiles.Count != 1) return null;
        PreparationSelectionRules.EnsureApplicable(state, selected);
        return _options.Preparation!.Work.Single(p => p.Profile == selected.Profiles.Single()).Work;
    }
    private async Task<GovernedTaskState> StateAsync(CancellationToken token) =>
        await _service.GetStateAsync(_options.TaskId, token).ConfigureAwait(false) ?? throw new InvalidDataException("Task unavailable.");
}
