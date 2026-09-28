using System.Diagnostics;
using AILedger.Core.Findings;

namespace FindingsPilot;

internal static class PilotRecovery
{
    internal static async Task<double?> FirstBatchAsync(PilotLedger ledger, PilotTransport transport,
        FindingsRequest request, string scenario, int size, CancellationToken token)
    {
        if (scenario is "lost-response" or "journals-unavailable")
            return await LostResponseAsync(ledger, transport, request, size, token);
        if (scenario is "missing-reference" or "revoked-capability" or "invalid-local-reference")
            return await RefusalAsync(ledger, transport, request, scenario, token);
        PilotTransport.Committed(await transport.CallAsync(request, token));
        return scenario == "changed-key-content" ? await ConflictAsync(ledger, transport, request, token) : null;
    }

    private static async Task<double> LostResponseAsync(PilotLedger ledger, PilotTransport transport,
        FindingsRequest request, int size, CancellationToken token)
    {
        var start = Stopwatch.GetTimestamp();
        await transport.CallAsync(request, token, loseResponse: true);
        var bytes = await File.ReadAllBytesAsync(ledger.EventsPath, token);
        PilotJson.Require((await ledger.StateAsync(token)).Claims.Count == size,
            "Lost response did not leave the complete first batch.");
        PilotTransport.Committed(await transport.CallAsync(request, token), replay: true);
        await ledger.VerifyUnchangedAsync(bytes, token);
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private static async Task<double> RefusalAsync(PilotLedger ledger, PilotTransport transport,
        FindingsRequest request, string scenario, CancellationToken token)
    {
        if (scenario == "revoked-capability") await ledger.SetEvidenceCapabilityAsync(false, token);
        var before = await File.ReadAllBytesAsync(ledger.EventsPath, token);
        var invalid = request;
        if (scenario != "revoked-capability")
        {
            var evidence = request.Evidence.ToArray();
            evidence[^1] = evidence[^1] with { Supports = [scenario == "missing-reference"
                ? new(ClaimId: "absent-claim") : new(Finding: "absent-local")] };
            invalid = request with { Evidence = evidence };
        }
        var start = Stopwatch.GetTimestamp();
        PilotTransport.Refused(await transport.CallAsync(invalid, token),
            scenario == "invalid-local-reference" ? "invalid_reference" : "kernel_refused");
        await ledger.VerifyUnchangedAsync(before, token);
        if (scenario == "revoked-capability") await ledger.SetEvidenceCapabilityAsync(true, token);
        PilotTransport.Committed(await transport.CallAsync(request, token));
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private static async Task<double> ConflictAsync(PilotLedger ledger, PilotTransport transport,
        FindingsRequest request, CancellationToken token)
    {
        var before = await File.ReadAllBytesAsync(ledger.EventsPath, token);
        var changed = request with { Findings = request.Findings.Select((f, i) => i == 0
            ? f with { Statement = "Different content must not replace the original." } : f).ToArray() };
        var start = Stopwatch.GetTimestamp();
        PilotTransport.Refused(await transport.CallAsync(changed, token), "idempotency_conflict");
        await ledger.VerifyUnchangedAsync(before, token);
        PilotTransport.Committed(await transport.CallAsync(request, token), replay: true);
        await ledger.VerifyUnchangedAsync(before, token);
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
}
