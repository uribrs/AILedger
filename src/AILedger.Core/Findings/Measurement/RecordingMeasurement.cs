using AILedger.Core.Contracts;

namespace AILedger.Core.Findings;

internal static class RecordingMeasurement
{
    public static RecordingMeasurementReport<T> Build<T>(GovernedTaskState state,
        IReadOnlyList<LedgerEvent> history, IReadOnlyList<T>? receipts,
        IReadOnlyList<LocatedFindingsAttempt> observations, IReadOnlyList<FindingsCoverageGap> gaps) where T : class, IRecordingReceiptIdentity
    {
        var coverage = gaps.ToList();
        var canonical = receipts ?? [];
        var applications = observations.Where(x => x.Kind == "application")
            .ToDictionary(x => x.Observation.AttemptId!);
        var runs = state.Runs.Values.OrderBy(x => x.Id.Value, StringComparer.Ordinal)
            .Select(run => new FindingsRunObservation(run.Id.Value, run.ActorId.Value,
                run.Provider, run.ProviderVersion,
                history.Select(e => e.Data).OfType<RunStarted>()
                    .FirstOrDefault(e => e.Run.Id == run.Id)?.Run.Model,
                history.Select(e => e.Data).OfType<RunCompleted>()
                    .LastOrDefault(e => e.RunId == run.Id))).ToArray();
        foreach (var run in runs.Where(r => r.Completion?.TruncatedLines > 0))
            coverage.Add(new("run:" + run.RunId, "Provider stream was truncated; retained usage/outcome observations may be incomplete."));
        var joins = observations.Select(row => Join(row, applications, canonical, runs, coverage)).ToArray();
        foreach (var receipt in canonical)
        {
            if (!joins.Any(x => x.Kind == "application" && x.CanonicalTransactionId == receipt.TransactionId))
                coverage.Add(new("canonical:" + receipt.TransactionId, "No matching application observation; commit remains proven by canonical receipt."));
        }
        foreach (var app in joins.Where(x => x.Kind == "application"))
            if (!joins.Any(x => x.Kind == "transport" && x.JoinedApplicationAttemptId == app.Observation.AttemptId))
                coverage.Add(new(app.Source, "No matching transport observation; direct application call or missing transport telemetry.", app.Line));

        return new(state.Version, receipts?.Count, receipts?.Sum(x => x.EventIds.Count),
            CountObserved(observations, "transport", _ => true),
            CountObserved(observations, "transport", x => x.MessageKind == "tool_call"),
            CountObserved(observations, "application", _ => true), canonical, joins, runs, coverage,
            ["Attempt counts are observed lower bounds. Best-effort journals have no completeness watermark; a crash or failed write can leave no row.",
             "Transport, application and phase timings nest; they are not added to task elapsed time.",
             "Response delivery 'written' is local write/flush, not client acknowledgement. Commit and delivery are independent.",
             "Exact negotiated MCP versions, relay authentication failures, provider permission/search-hook denials and client acknowledgements are not captured by these journals.",
             "Run completion supplies joined session/model/turn observations only when recorded; no requested identity or event count fills a missing observation.",
             "Provider usage remains once per run in the existing retrospective cost measures; attempts, transactions and granular events are separate populations.",
             "Kernel refusal rows remain in the existing refusal report with original command/reason/build. Journals have no shared refusal ID; no heuristic one-to-one refusal join is asserted."]);
    }

    private static int? CountObserved(IReadOnlyList<LocatedFindingsAttempt> rows, string kind,
        Func<FindingsAttemptObservation, bool> predicate) =>
        rows.Any(x => x.Kind == kind) ? rows.Count(x => x.Kind == kind && predicate(x.Observation)) : null;

    private static FindingsAttemptJoin Join<T>(LocatedFindingsAttempt row,
        IReadOnlyDictionary<string, LocatedFindingsAttempt> applications,
        IReadOnlyList<T> receipts, IReadOnlyList<FindingsRunObservation> runs,
        List<FindingsCoverageGap> gaps) where T : class, IRecordingReceiptIdentity
    {
        var o = row.Observation;
        void Gap(string reason) => gaps.Add(new(row.Source, reason, row.Line));
        var run = runs.FirstOrDefault(r => r.RunId == o.RunId && r.ActorId == o.ActorId &&
            (o.Provider is null || r.Provider == o.Provider));
        if (o.RunId is not null && run is null) Gap("Run attribution does not match canonical task/actor/provider.");
        if (run is not null && run.Completion?.ProviderSessionId is null)
            Gap("Provider session not observed on run completion.");
        if (o.ProviderSessionId is not null && run?.Completion?.ProviderSessionId is { } session && o.ProviderSessionId != session)
        {
            Gap("Transport session observation conflicts with run completion; no session enrichment.");
            run = null;
        }
        LocatedFindingsAttempt? application = null;
        if (o.ApplicationAttemptId is { } id)
        {
            if (applications.TryGetValue(id, out var candidate) && SameRequest(o, candidate.Observation)) application = candidate;
            else Gap("Application attempt is missing or has conflicting attribution.");
        }
        if (o.ApplicationEntered == true && o.ApplicationAttemptId is null)
            Gap("Application entered without an observed attempt identity (for example an exception).");
        if (o.ApplicationEntered == true && o.ApplicationCollectionStatus != "collected")
            Gap("Application telemetry collection was unavailable or unobserved.");

        // An observation can contain proposed IDs even when append failed. Only the storage-owned
        // receipt reader proves a commit. Request-only joins require a matching fingerprint too.
        var receipt = MatchReceipt(o, receipts);
        if (receipt is null && application is not null) receipt = MatchReceipt(application.Observation, receipts);
        if (receipt is not null && o.CorrelationId is not null &&
            (o.CorrelationId != receipt.CorrelationId || o.CausationId != receipt.CausationId))
        {
            Gap("Transport correlation/causation conflicts with canonical receipt.");
            receipt = null;
        }
        if (o.TransactionId is not null && receipt is null)
            Gap("Observed transaction has no matching canonical receipt; its commit is not established by telemetry.");
        return new(row.Source, row.Line, row.Kind, o, receipt?.TransactionId,
            application?.Observation.AttemptId, run?.RunId, run?.Completion?.ProviderSessionId);
    }

    private static bool SameRequest(FindingsAttemptObservation a, FindingsAttemptObservation b) =>
        a.TaskId == b.TaskId && a.ActorId == b.ActorId && a.RunId == b.RunId &&
        a.RequestId == b.RequestId && (a.PayloadFingerprint is null || b.PayloadFingerprint is null ||
            a.PayloadFingerprint == b.PayloadFingerprint) &&
        (a.TransactionId is null || b.TransactionId is null || a.TransactionId == b.TransactionId) &&
        (a.EventIds is null || b.EventIds is null || a.EventIds.SequenceEqual(b.EventIds));

    private static T? MatchReceipt<T>(FindingsAttemptObservation o, IReadOnlyList<T> receipts) where T : class, IRecordingReceiptIdentity =>
        receipts.FirstOrDefault(r => r.TaskId == o.TaskId && r.ActorId == o.ActorId && r.RunId == o.RunId &&
            r.RequestId == o.RequestId && r.PayloadFingerprint == o.PayloadFingerprint &&
            (o.CorrelationId is null || o.CorrelationId == r.CorrelationId && o.CausationId == r.CausationId) &&
            (o.TransactionId is null || o.TransactionId == r.TransactionId) &&
            (o.EventIds is null || o.EventIds.SequenceEqual(r.EventIds)));
}
