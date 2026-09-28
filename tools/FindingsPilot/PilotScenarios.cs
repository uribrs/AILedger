using System.Diagnostics;
using AILedger.Core.Findings;

namespace FindingsPilot;

internal sealed record Trial(string Scenario, string Dataset, int Repetition, int BatchSize, double ExchangeTotalMs, double RecordingWallMs,
    double FirstExchangeMs, double? RecoveryMs, int HarnessCalls, int? ObservedToolAttempts,
    int? ObservedApplicationAttempts, int CommittedTransactions, int GranularEvents, int CoverageGaps,
    bool ExactTextAndReferences, string Directory);

internal static class PilotScenarios
{
    internal static async Task<Trial> RunAsync(string root, FindingsRequest source, string dataset, string scenario,
        int size, int repetition, CancellationToken token)
    {
        var ledger = new PilotLedger(root);
        await ledger.OpenAsync(token);
        var requests = PilotJson.Batches(source, size);
        PilotJson.Require(requests.Sum(r => r.Evidence.Count) == 20, "Batch split lost evidence dependencies.");
        var transport = new PilotTransport(ledger);
        double? recovery = null;
        if (scenario == "journals-unavailable")
        {
            Directory.CreateDirectory(Path.Combine(ledger.DirectoryPath, "findings-attempts.jsonl"));
            await File.WriteAllTextAsync(ledger.Diagnostics, "blocked collector", token);
        }
        var recordingStarted = Stopwatch.GetTimestamp();
        recovery = await PilotRecovery.FirstBatchAsync(ledger, transport, requests[0], scenario, size, token);
        // Check early persistence before later prepared batches. This is incremental recording,
        // not a claim that a live researcher generated the input in four reasoning phases.
        var checkpoint = await new PilotLedger(root).StateAsync(token);
        PilotJson.Require(checkpoint.Claims.Count == size && checkpoint.Evidence.Count == size, "Early batch not independently readable.");
        await PilotJson.SaveAsync(Path.Combine(root, "early-checkpoint.json"),
            new { checkpoint.Version, Claims = checkpoint.Claims.Count, Evidence = checkpoint.Evidence.Count }, token);
        foreach (var request in requests.Skip(1)) PilotTransport.Committed(await transport.CallAsync(request, token));
        var recordingWallMs = Stopwatch.GetElapsedTime(recordingStarted).TotalMilliseconds;
        var report = await ledger.ReportAsync(token);
        await ledger.VerifyAsync(source, report, requests.Length, token);
        var expectedCalls = requests.Length + (scenario == "normal" ? 0 : scenario == "changed-key-content" ? 2 : 1);
        PilotJson.Require(transport.Calls.Count == expectedCalls, "Unexpected harness call population.");
        PilotJson.Require(report.ObservedToolAttempts == (scenario == "journals-unavailable" ? null : expectedCalls), "Tool measurement mismatch.");
        PilotJson.Require(report.ObservedApplicationAttempts == (scenario == "journals-unavailable" ? null :
            expectedCalls - (scenario == "invalid-local-reference" ? 1 : 0)), "Application measurement mismatch.");
        if (scenario == "journals-unavailable") PilotJson.Require(report.CoverageGaps.Count > 0, "Missing telemetry must remain visible.");
        if (scenario == "lost-response") PilotJson.Require(report.Attempts.Any(a => a.Kind == "transport" &&
            a.Observation.ResponseDelivery == "failed" && a.CanonicalTransactionId is not null), "Lost delivery not joined to canonical commit.");
        return new(scenario, dataset, repetition, size, transport.Calls.Sum(c => c.ElapsedMs), recordingWallMs, transport.Calls[0].ElapsedMs,
            recovery, transport.Calls.Count, report.ObservedToolAttempts, report.ObservedApplicationAttempts,
            report.CommittedTransactions!.Value, report.GranularEvents!.Value, report.CoverageGaps.Count, true, root);
    }
}
