using System.Diagnostics;
using System.Text.Json;
using AILedger.Core.Inspection;
using AILedger.Core.Findings;
using AILedger.Cli.Inspection;

namespace AILedger.Cli.Findings;

public sealed partial class FindingsMcpServer
{
    private async Task<JsonElement> InvokeInspectionAsync(JsonElement arguments, FindingsTransportAttempt attempt, CancellationToken token)
    {
        try
        {
            // Parse before binding or application entry, as for the four mutation tools.
            object query = attempt.InspectionTool switch
            {
                "inspect_task" => InspectionRequestParser.Read<InspectionQuery>(arguments),
                "retrieve_context" => InspectionRequestParser.Read<RetrievalQuery>(arguments),
                _ => InspectionRequestParser.Readiness(arguments)
            };
            var binding = await _host.BindInspectionAsync(token).ConfigureAwait(false);
            if (_host.Recorder is not ITaskInspector inspector)
                throw new FindingsRequestException("unsupported", "The host has no inspection service.");
            attempt.ApplicationEntered = true;
            var started = Stopwatch.GetTimestamp();
            object result;
            try
            {
                result = query switch
                {
                    InspectionQuery read => await inspector.InspectAsync(binding, read, token).ConfigureAwait(false),
                    RetrievalQuery read => await inspector.RetrieveAsync(binding, read, token).ConfigureAwait(false),
                    ReadinessQuery read => await inspector.CheckReadinessAsync(binding, read, token).ConfigureAwait(false),
                    _ => throw new InvalidOperationException("Unknown inspection request.")
                };
            }
            finally { attempt.ApplicationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
            var body = JsonSerializer.SerializeToElement(result, result.GetType(), InspectionRequestParser.Json);
            attempt.InspectionStatus = body.GetProperty("status").GetString();
            return body;
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            var code = e is FindingsRequestException known ? known.Code :
                e is JsonException or InvalidOperationException or FormatException or OverflowException ? "invalid_request" : "inspection_unavailable";
            attempt.InspectionStatus = attempt.InspectionTool == "check_readiness" ? "unknown" : "error";
            return JsonSerializer.SerializeToElement(new { schema_version = 1, status = attempt.InspectionStatus,
                diagnostic = new InspectionDiagnostic(code, attempt.InspectionTool!, null,
                    "Valid typed request and trusted inspection binding", e is FindingsRequestException ? e.Message : "Inspection could not be completed.",
                    "Correct the request or restore the original host binding; no mutation was attempted.") }, InspectionRequestParser.Json);
        }
    }
}
