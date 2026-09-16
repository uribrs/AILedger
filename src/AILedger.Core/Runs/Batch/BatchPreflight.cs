using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Stages;
using AILedger.Core.WorkItems;

namespace AILedger.Core.Runs.Batch;

/// <summary>
/// Evaluates a proposed fan-out without appending events or starting providers.
/// </summary>
/// <remarks>
/// The reported version is the one snapshot used for the whole evaluation. Valid planned work is
/// projected into an in-memory preview in request order so later members can reference it and so
/// conflicts inside the batch are found. The preview is never authority: every real command must
/// still pass the normal command-time rules against current task state.
/// </remarks>
public static class BatchPreflight
{
    public static BatchPreflightResult Evaluate(
        GovernedTaskState state,
        BatchPreflightRequest request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Members);

        EnsureWellFormed(request.Members);

        var preview = state;
        var results = new List<BatchPreflightMemberResult>(request.Members.Count);
        foreach (var member in request.Members)
        {
            var kind = member.PreparationRefusal?.Kind
                ?? (member.WorkItem is null
                    ? BatchPreflightMemberKind.ProviderLaunch
                    : BatchPreflightMemberKind.WorkItem);
            try
            {
                if (member.PreparationRefusal is { } preparationRefusal)
                {
                    throw new GovernanceException(preparationRefusal.Refusal);
                }

                if (member.WorkItem is { } work)
                {
                    var command = new AddWorkItemCommand(
                        work.ActorId,
                        CausationId: null,
                        CorrelationId: $"batch-preflight:{member.Id}",
                        work.WorkItemId,
                        work.Title,
                        work.Owner,
                        work.DependsOnClaims,
                        work.ResourceScope,
                        work.NotSplitJustification,
                        work.BaseRef,
                        work.SkillsServedNow,
                        work.WithoutBriefReason,
                        work.StaleBriefEvidenceId);

                    _ = new AuthorizationPolicy().Authorize(preview, command);
                    EntryActionStageRules.EnsureAllowed(preview, command);
                    var events = WorkItemLifecycleRules.Add(preview, command);
                    var added = events.OfType<WorkItemAdded>().Single().WorkItem;
                    var workItems = new Dictionary<WorkItemId, WorkItem>(preview.WorkItems)
                    {
                        [added.Id] = added
                    };
                    preview = preview with { WorkItems = workItems };
                }
                else
                {
                    var launch = member.ProviderLaunch!;
                    ProviderLaunchPreflight.EnsurePermitted(
                        preview,
                        launch.ActorId,
                        launch.SubjectActorId,
                        launch.SkillsServedNow,
                        launch.WithoutBriefReason,
                        launch.StaleBriefEvidenceId,
                        launch.WorkItemId);
                }

                results.Add(new BatchPreflightMemberResult(member.Id, kind, Admissible: true));
            }
            catch (GovernanceException refusal)
            {
                results.Add(new BatchPreflightMemberResult(
                    member.Id, kind, Admissible: false, refusal.Message));
            }
        }

        var causes = results
            .Where(result => !result.Admissible)
            .GroupBy(result => result.Refusal!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new BatchPreflightCause(
                group.Key,
                group.Select(result => result.Id).Order(StringComparer.Ordinal).ToArray()))
            .ToArray();

        return new BatchPreflightResult(
            state.Version,
            results.All(result => result.Admissible),
            results,
            causes);
    }

    private static void EnsureWellFormed(IReadOnlyList<BatchPreflightMember> members)
    {
        var duplicate = members
            .Where(member => !string.IsNullOrWhiteSpace(member.Id))
            .GroupBy(member => member.Id.Trim(), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Batch member id '{duplicate.Key}' appears more than once.", nameof(members));
        }

        foreach (var member in members)
        {
            if (string.IsNullOrWhiteSpace(member.Id))
            {
                throw new ArgumentException("Every batch member requires a nonblank id.", nameof(members));
            }

            var specifiedKinds = (member.WorkItem is null ? 0 : 1)
                + (member.ProviderLaunch is null ? 0 : 1)
                + (member.PreparationRefusal is null ? 0 : 1);
            if (specifiedKinds != 1)
            {
                throw new ArgumentException(
                    $"Batch member '{member.Id}' must specify exactly one of workItem, providerLaunch, or preparationRefusal.",
                    nameof(members));
            }

            if (member.PreparationRefusal is { Refusal: var refusal }
                && string.IsNullOrWhiteSpace(refusal))
            {
                throw new ArgumentException(
                    $"Batch member '{member.Id}' preparation refusal must be nonblank.",
                    nameof(members));
            }
        }
    }
}
