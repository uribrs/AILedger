using System.Text.Json;
using AILedger.Cli.ContextBriefing;
using AILedger.Cli.Providers;
using AILedger.Cli.Routing;
using AILedger.Cli.Runs;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Runs.Batch;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Preflight;

internal sealed class BatchPreflightCliCommands(
    CliCommandExecutor executor,
    ContextBriefingCliCommands contextCommands,
    JsonSerializerOptions json)
{
    public CliCommandRegistration Registration() => new(
        ["preflight batch", "batch preflight"],
        CliCommandOptions.Set("root", "task", "actor", "cognitive-root", "body-stdin"),
        isReadOnly: true,
        ExecuteAsync);

    private async Task ExecuteAsync(
        CliCommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        if (!invocation.Input.Flag("body-stdin"))
        {
            throw new CliUsageException("Batch preflight requires '--body-stdin' with its JSON request.");
        }

        // Resolve Console.In at execution time so an embedding host or test can replace standard
        // input after constructing CliApplication, just as a shell supplies it after process start.
        var body = await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        BatchPreflightCliRequest request;
        try
        {
            request = JsonSerializer.Deserialize<BatchPreflightCliRequest>(body, json)
                ?? throw new CliUsageException("Batch preflight request body cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new CliUsageException($"Invalid batch preflight JSON: {exception.Message}");
        }

        var state = await CliCommandExecutor.RequireStateAsync(invocation, cancellationToken)
            .ConfigureAwait(false);
        var servedNow = await contextCommands.CurrentSkillsAsync(
            state, invocation.Input, cancellationToken).ConfigureAwait(false);
        var actor = Actor(invocation.Input);
        if (request.Members is null)
        {
            throw new CliUsageException("Batch preflight request requires a 'members' array.");
        }

        var members = request.Members.Select((member, index) =>
        {
            if (member is null)
            {
                throw new CliUsageException(
                    $"Batch preflight member at index {index} cannot be null.");
            }

            return MapOrRefusal(member, actor, servedNow, invocation.LedgerRoot, state);
        }).ToArray();

        var result = BatchPreflight.Evaluate(state, new BatchPreflightRequest(members));
        await executor.WriteJsonAsync(new
        {
            result.CheckedTaskVersion,
            result.Admissible,
            result.Members,
            result.Causes,
            Limitation = "Snapshot admission only: no filesystem observation, provider probing, state reservation or guarantee of later launch."
        }).ConfigureAwait(false);
    }

    private static BatchPreflightMember MapOrRefusal(
        BatchPreflightCliMember member,
        ActorId actor,
        IReadOnlyList<ContextSkill>? servedNow,
        string ledgerRoot,
        GovernedTaskState state)
    {
        try
        {
            return Map(member, actor, servedNow, ledgerRoot, state);
        }
        catch (Exception refusal) when (refusal is GovernanceException or CliUsageException)
        {
            var kind = member.WorkItem is null
                ? BatchPreflightMemberKind.ProviderLaunch
                : BatchPreflightMemberKind.WorkItem;
            return new BatchPreflightMember(
                member.Id,
                PreparationRefusal: new BatchPreflightPreparationRefusal(kind, refusal.Message));
        }
    }

    private static BatchPreflightMember Map(
        BatchPreflightCliMember member,
        ActorId actor,
        IReadOnlyList<ContextSkill>? servedNow,
        string ledgerRoot,
        GovernedTaskState state)
    {
        PlannedWorkItemPreflight? work = null;
        if (member.WorkItem is { } planned)
        {
            var scopes = (planned.ResourceScope ?? [])
                .Select(ProviderGrantResolver.ResolveExistingScope)
                .Distinct(ProviderGrantResolver.PathComparer)
                .ToArray();
            ProviderGrantResolver.EnsureLedgerIsOutsideProviderDirectories(ledgerRoot, scopes);
            work = new PlannedWorkItemPreflight(
                actor,
                planned.WorkItemId,
                planned.Title,
                planned.Owner,
                planned.DependsOnClaims ?? [],
                scopes,
                planned.NotSplitJustification,
                planned.BaseRef,
                servedNow,
                planned.WithoutBriefReason,
                planned.StaleBriefEvidenceId);
        }

        ProviderLaunchPreflightRequest? launch = member.ProviderLaunch is null
            ? null
            : new ProviderLaunchPreflightRequest(
                actor,
                member.ProviderLaunch.SubjectActorId,
                member.ProviderLaunch.WorkItemId,
                servedNow,
                member.ProviderLaunch.WithoutBriefReason,
                member.ProviderLaunch.StaleBriefEvidenceId,
                member.ProviderLaunch.RunId,
                member.ProviderLaunch.Provider,
                AssuranceCliInput.Binding(state, member.ProviderLaunch.WorkItemId,
                    member.ProviderLaunch.CoveredWorkItemIds,
                    member.ProviderLaunch.CandidateId, member.ProviderLaunch.VerifierRunId,
                    explicitCoverage: member.ProviderLaunch.CoveredWorkItemIds is not null));

        return new BatchPreflightMember(member.Id, work, launch);
    }

    private sealed record BatchPreflightCliRequest(IReadOnlyList<BatchPreflightCliMember?>? Members);

    private sealed record BatchPreflightCliMember(
        string Id,
        PlannedWorkItemCliRequest? WorkItem = null,
        ProviderLaunchCliRequest? ProviderLaunch = null);

    private sealed record PlannedWorkItemCliRequest(
        WorkItemId WorkItemId,
        string Title,
        ActorId? Owner,
        IReadOnlyList<ClaimId>? DependsOnClaims,
        IReadOnlyList<string>? ResourceScope,
        AlternativeId? NotSplitJustification = null,
        string? BaseRef = null,
        string? WithoutBriefReason = null,
        EvidenceId? StaleBriefEvidenceId = null);

    private sealed record ProviderLaunchCliRequest(
        ActorId? SubjectActorId,
        WorkItemId? WorkItemId,
        string? WithoutBriefReason = null,
        EvidenceId? StaleBriefEvidenceId = null,
        IReadOnlyList<WorkItemId>? CoveredWorkItemIds = null,
        string? CandidateId = null,
        RunId? VerifierRunId = null,
        RunId? RunId = null,
        string? Provider = null);
}
