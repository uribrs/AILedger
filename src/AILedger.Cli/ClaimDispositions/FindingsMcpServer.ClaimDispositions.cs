using System.Diagnostics;
using System.Text.Json;
using AILedger.Core.ClaimDispositions;
using AILedger.Core.Findings;
using AILedger.Cli.ClaimDispositions;

namespace AILedger.Cli.Findings;

public sealed partial class FindingsMcpServer
{
    private async Task<ClaimDispositionsResult> InvokeClaimDispositionsAsync(JsonElement arguments, FindingsTransportAttempt attempt,
        CancellationToken cancellationToken)
    {
        ClaimDispositionsRequest request;
        try
        {
            attempt.RequestId = RequestId(arguments);
            request = ClaimDispositionsRequestParser.Parse(arguments);
            attempt.RequestId = request.RequestId;
        }
        catch (Exception e) when (e is FindingsRequestException or JsonException or InvalidOperationException)
        {
            var error = e as FindingsRequestException;
            return new(attempt.Id, null, false, new(error?.Code ?? "invalid_request",
                error?.Message ?? "Expected the exact record_claim_dispositions v1 request shape and valid Unicode.",
                "request", "unknown", "after_correction", error?.ItemPath));
        }
        ClaimDispositionsBinding binding;
        try
        {
            if (_host.Recorder is not IClaimDispositionsRecorder)
                throw new InvalidOperationException("ClaimDispositions service is unavailable.");
            binding = await _host.BindClaimDispositionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            attempt.Failure = "binding_cancelled";
            return new(attempt.Id, null, false, new("storage_unavailable",
                "Cancelled before application admission. Retry the same request and trusted binding.",
                "application", "unknown", "same_request"));
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return new(attempt.Id, null, false, new("authorization_denied",
                "Trusted host binding is unavailable or changed. Restore the original binding before retrying.",
                "binding", "unknown", "none"));
        }
        var started = Stopwatch.GetTimestamp();
        attempt.ApplicationEntered = true;
        try
        {
            var result = await ((IClaimDispositionsRecorder)_host.Recorder).RecordClaimDispositionsAsync(binding, request, cancellationToken).ConfigureAwait(false);
            attempt.ApplicationAttemptId = result.AttemptId;
            return result;
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            attempt.Failure = "application_exception";
            return new(attempt.Id, null, false, new("outcome_unknown",
                "The application did not return an outcome. Retry exactly the same body, key and trusted binding.",
                "application", "unknown", "same_request"));
        }
        finally { attempt.ApplicationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
    }

}
