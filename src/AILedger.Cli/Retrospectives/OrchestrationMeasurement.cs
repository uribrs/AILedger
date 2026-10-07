using System.Text.Json;
using AILedger.Cli.Orchestration;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Storage;

namespace AILedger.Cli.Retrospectives;

internal static class OrchestrationMeasurement
{
    internal static async Task<OrchestrationAdoptionReport> BuildAsync(FileGovernedTaskService host,
        GovernedTaskState state, IReadOnlyList<LedgerEvent> history, RetrospectiveRefusalJournal? refusals,
        CancellationToken token)
    {
        var observations = new List<OrchestrationObservation>();
        var unknown = new List<string>
        {
            "User corrections and manual routing require attributed human observations; actor names and correlation labels do not prove human involvement.",
            "Human attention/time is unknown; task/provider duration is not attention time.",
            "Routine engineering versus genuine user-decision escalation requires semantic assessment; recorded escalation kind is reported without that inference.",
            "Findings after repair are temporal associations, not proof of repair-induced or repeated defect classes.",
            "Missed acceptance and productivity benefit are unknown; historical completion/acceptance receipts do not establish current applicability.",
            "Provider USD values are reported observations, not billing verification; absent usage/cost stays unknown."
        };
        CoordinationSnapshot? snapshot = null;
        DriverJournal? journal = null;
        var status = "absent";
        try
        {
            snapshot = await host.InspectCoordinationAsync(state.TaskId, token).ConfigureAwait(false);
            if (snapshot?.Payload is { } payload)
            {
                journal = JsonSerializer.Deserialize<DriverJournal>(payload, LedgerJson.CreateOptions());
                if (journal is null || journal.SchemaVersion != 1 || journal.Intents is null ||
                    journal.Intents.Count > 256 || journal.Intents.Any(i => i?.Request is null || i.Receipt is { Retention: null }))
                    throw new InvalidDataException("Unsupported driver observation schema.");
                status = "observed";
                AddDispatches(state, journal, observations);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            journal = null;
            status = "unavailable";
            unknown.Add("Coordination observations unavailable: " + error.Message);
        }
        AddEvents(state, history, journal, observations);
        if (refusals is null || refusals.UnreadableRows > 0)
            unknown.Add("Refusal telemetry is absent or partial; observed counts are not a total.");
        var current = await host.GetStateAsync(state.TaskId, token).ConfigureAwait(false);
        if (current?.Version != state.Version)
            unknown.Add("Task changed while measuring; observations describe the named versions, not an atomic current snapshot.");
        return new(1, state.Version, status, snapshot?.Revision, snapshot?.Epoch, observations,
            await ReadUsageAsync(host, state, unknown, token).ConfigureAwait(false),
            refusals?.Rows.GroupBy(r => r.Cause ?? "unclassified").ToDictionary(g => g.Key, g => g.Count()), unknown);
    }

    private static async Task<IReadOnlyList<OrchestrationProviderUsage>> ReadUsageAsync(FileGovernedTaskService host,
        GovernedTaskState state, List<string> unknown, CancellationToken token)
    {
        var usage = new List<OrchestrationProviderUsage>();
        foreach (var run in state.Runs.Values)
        {
            decimal? usd = null;
            try
            {
                var result = await host.InspectProviderResultAsync(state.TaskId, run.Id, token).ConfigureAwait(false);
                if (result is not null && result.Provider == run.Provider) usd = RunCostReader.ReadReportedUsd(result.Provider, result.Events);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException)
            { unknown.Add($"Run {run.Id.Value} retained usage unavailable: {error.Message}"); }
            usage.Add(new(run.Id.Value, run.Provider, run.Model, run.OutputTokens, run.TokensInUncached,
                run.TokensInCacheWrite, run.TokensInCacheRead, usd, $"events.jsonl:run/{run.Id.Value}; runs/{run.Id.Value}.json"));
        }
        return usage;
    }

    private static void AddDispatches(GovernedTaskState state, DriverJournal journal, List<OrchestrationObservation> rows)
    {
        foreach (var intent in journal.Intents)
        {
            var id = intent.Request.RunId.Value;
            var source = $"coordination-v1.json:intents/{id}";
            var receipt = intent.Receipt;
            rows.Add(new("dispatch", source, receipt?.Failure?.Kind.ToString() ?? receipt?.Completed?.Status.ToString() ?? "unknown",
                id, Detail: $"work={intent.Request.CognitiveWork}; phase={receipt?.Phase}; retention={receipt?.Retention.Status}; code={receipt?.Failure?.Code}"));
            if (intent.Request.Recovery is { } recovery)
                rows.Add(new("recovery", source, receipt?.Failure?.Kind.ToString() ??
                    (state.Runs.TryGetValue(intent.Request.RunId, out var run) ? run.RoutingAssessment?.Disposition.ToString() : null) ?? "unknown",
                    id, recovery.DispatchRun?.Value, recovery.Code));
            if (intent.Request.CognitiveWork == CognitiveWorkKind.Repair)
                rows.Add(new("repair-dispatch", source, receipt?.Completed?.Status.ToString() ?? "unknown", id));
        }
        foreach (var group in journal.Intents.GroupBy(i => (i.Basis, i.Stage, i.Request.CognitiveWork, i.Request.WorkItemId)).Where(g => g.Count() > 1))
            foreach (var repeated in group.Skip(1))
                rows.Add(new("repeated-dispatch", $"coordination-v1.json:intents/{repeated.Request.RunId.Value}", "same-recorded-basis",
                    repeated.Request.RunId.Value, group.First().Request.RunId.Value));
        if (journal.Stop is { } stop)
            rows.Add(new("driver-stop", "coordination-v1.json:stop", stop.Status.ToString(), Detail: stop.Code));
    }

    private static void AddEvents(GovernedTaskState state, IReadOnlyList<LedgerEvent> history,
        DriverJournal? journal, List<OrchestrationObservation> rows)
    {
        var repairs = journal?.Intents.Where(i => i.Request.CognitiveWork == CognitiveWorkKind.Repair)
            .Select(i => i.Request.RunId).ToHashSet() ?? [];
        RunId? lastRepair = null;
        foreach (var entry in history)
        {
            var source = "events.jsonl:" + entry.EventId.Value;
            if (entry.Data is RunCompleted completed && repairs.Contains(completed.RunId)) lastRepair = completed.RunId;
            if (entry.Data is ClaimAdded claim && lastRepair is not null)
                rows.Add(new("finding-after-repair", source, claim.Claim.Status.ToString(), RelatedRunId: lastRepair.Value.Value, Detail: claim.Claim.Id.Value));
            if (entry.Data is StageTransitioned transition)
                rows.Add(new("routing-transition", source, transition.Current.ToString(), Detail: $"actor={entry.ActorId}; correlation={entry.CorrelationId}"));
            if (entry.Data is WorkItemCompleted work)
                rows.Add(new("completion", source, work.Acceptance is null ? "no-associated-acceptance" : "historical-associated-acceptance", Detail: work.WorkItemId.Value));
        }
        foreach (var escalation in state.Escalations.Values)
            rows.Add(new("escalation", "events.jsonl:escalation/" + escalation.Id.Value, escalation.Status.ToString(),
                Detail: $"kind={escalation.Kind}; actor={escalation.Provenance.ActorId}"));
    }
}
