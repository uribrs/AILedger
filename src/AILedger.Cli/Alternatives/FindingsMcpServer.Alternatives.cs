using System.Diagnostics;
using System.Text.Json;
using AILedger.Core.Alternatives;
using AILedger.Core.Findings;
using AILedger.Cli.Alternatives;

namespace AILedger.Cli.Findings;

public sealed partial class FindingsMcpServer
{
    private async Task<AlternativesResult> InvokeAlternativesAsync(JsonElement arguments, FindingsTransportAttempt attempt,
        CancellationToken cancellationToken)
    {
        AlternativesRequest request;
        try
        {
            attempt.RequestId = RequestId(arguments);
            request = AlternativesRequestParser.Parse(arguments);
            attempt.RequestId = request.RequestId;
        }
        catch (Exception e) when (e is FindingsRequestException or JsonException or InvalidOperationException)
        {
            var error = e as FindingsRequestException;
            return new(attempt.Id, null, false, new(error?.Code ?? "invalid_request",
                error?.Message ?? (e is JsonException { Path: not null } shape ? shape.Message : null) ?? "Expected the exact record_alternatives v1 request shape and valid Unicode.",
                "request", "not_committed", "after_correction", error?.ItemPath ?? (e as JsonException)?.Path));
        }
        AlternativesBinding binding;
        try
        {
            if (_host.Recorder is not IAlternativesRecorder)
                throw new InvalidOperationException("Alternatives service is unavailable.");
            binding = await _host.BindAlternativesAsync(cancellationToken).ConfigureAwait(false);
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
            var result = await ((IAlternativesRecorder)_host.Recorder).RecordAlternativesAsync(binding, request, cancellationToken).ConfigureAwait(false);
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
