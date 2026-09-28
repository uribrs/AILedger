using System.Diagnostics;
using System.Text.Json;
using AILedger.Core.Artifacts;
using AILedger.Core.Findings;
using AILedger.Cli.Artifacts;

namespace AILedger.Cli.Findings;

public sealed partial class FindingsMcpServer
{
    private async Task<ArtifactSubmissionResult> InvokeArtifactSubmissionAsync(JsonElement arguments, FindingsTransportAttempt attempt,
        CancellationToken cancellationToken)
    {
        ArtifactSubmissionRequest request;
        try
        {
            attempt.RequestId = RequestId(arguments);
            request = ArtifactSubmissionRequestParser.Parse(arguments);
            attempt.RequestId = request.RequestId;
        }
        catch (Exception e) when (e is FindingsRequestException or JsonException or InvalidOperationException)
        {
            var error = e as FindingsRequestException;
            return new(attempt.Id, null, false, new(error?.Code ?? "invalid_request",
                error?.Message ?? "Expected the exact submit_artifact v1 request shape and valid Unicode.",
                "request", "unknown", "after_correction", error?.ItemPath));
        }
        ArtifactSubmissionBinding binding;
        try
        {
            if (_host.Recorder is not IArtifactSubmitter)
                throw new InvalidOperationException("ArtifactSubmission service is unavailable.");
            binding = await _host.BindArtifactSubmissionAsync(cancellationToken).ConfigureAwait(false);
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
            var result = await ((IArtifactSubmitter)_host.Recorder).SubmitArtifactAsync(binding, request, cancellationToken).ConfigureAwait(false);
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
