using System.Diagnostics;
using System.Text.Json;
using AILedger.Core.Assurance;
using AILedger.Core.Domain;

namespace AILedger.Cli.Findings;

public sealed partial class FindingsMcpServer
{
    private async Task<JsonElement> InvokeAssuranceAsync(JsonElement arguments, FindingsTransportAttempt attempt, CancellationToken token)
    {
        attempt.RequestId = RequestId(arguments);
        if (_host.Assurance is null || !_host.Assurance.Operations.Contains(attempt.AssuranceTool!))
        {
            attempt.InspectionStatus = "error";
            return AssuranceValidation.Json(new AssuranceResponse("error", attempt.Id, null, false, null,
                new("authorization_denied", "No trusted grant for this assurance operation.", "Ask the operator to configure an explicit scoped host binding.", "not_committed")));
        }
        var start = Stopwatch.GetTimestamp(); attempt.ApplicationEntered = true;
        try
        {
            try { await _host.ValidateSessionAsync(token).ConfigureAwait(false); }
            catch (Exception error) when (error is GovernanceException or InvalidDataException)
            {
                attempt.InspectionStatus = "error";
                return AssuranceValidation.Json(new AssuranceResponse("error", attempt.Id, null, false, null,
                    new("authorization_denied", error.Message, "Obtain a current trusted host binding.", "not_committed")));
            }
            var result = await _host.Assurance.InvokeAsync(attempt.AssuranceTool!, arguments, token).ConfigureAwait(false);
            attempt.ApplicationAttemptId = result.AttemptId; attempt.InspectionStatus = result.Status;
            attempt.AssuranceResult = result;
            return AssuranceValidation.Json(result);
        }
        finally { attempt.ApplicationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    }
}
