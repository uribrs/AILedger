using System.Text.Json;
using AILedger.Cli.Cognitive;
using AILedger.Cli.Dispatch;
using AILedger.Core.Domain;
using AILedger.Storage;
namespace AILedger.Cli.Findings;

public sealed partial class FindingsMcpServer
{
    private async Task<JsonElement> InvokeCognitiveAsync(JsonElement arguments, FindingsTransportAttempt attempt, CancellationToken token)
    {
        HostHandoffReceipt result;
        var id = RequestId(arguments) ?? "invalid";
        attempt.RequestId = id;
        try
        {
            var request = CognitiveHandoffParser.Parse(arguments);
            var binding = await _host.BindProducerOutcomeAsync(token).ConfigureAwait(false);
            if (!binding.AllowCognitiveHandoffs || _host.Cognitive is null)
                result = new(HostHandoffStatus.Unsupported, id, [], Diagnostic: "No live cognitive handoff grant.");
            else result = await _host.Cognitive.InvokeAsync(request, token).ConfigureAwait(false);
        }
        catch (NotSupportedException error) { result = new(HostHandoffStatus.Unsupported, id, [], Diagnostic: error.Message); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or GovernanceException or InvalidDataException)
        { result = new(HostHandoffStatus.Refused, id, [], Diagnostic: error.Message); }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { result = new(HostHandoffStatus.Unknown, id, [], Diagnostic: "Binding or recording unavailable; preserve original request."); }
        attempt.InspectionStatus = result.Status == HostHandoffStatus.Recorded ? "ok" : result.Status == HostHandoffStatus.Unknown ? "unknown" : "error";
        return JsonSerializer.SerializeToElement(result, LedgerJson.CreateOptions());
    }
}
