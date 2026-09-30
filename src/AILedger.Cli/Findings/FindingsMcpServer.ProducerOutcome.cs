using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;

namespace AILedger.Cli.Findings;

public sealed partial class FindingsMcpServer
{
    private async Task<JsonElement> InvokeProducerOutcomeAsync(JsonElement arguments,
        FindingsTransportAttempt attempt, CancellationToken token)
    {
        try
        {
            StrictJson.Validate(arguments);
            StrictJson.Members(arguments, ["outcome", "output_evidence_ids", "blocker_evidence_ids"], []);
            var declaration = new ProducerOutcome(StrictJson.Text(arguments, "outcome"),
                EvidenceIds(arguments, "output_evidence_ids"), EvidenceIds(arguments, "blocker_evidence_ids"));
            var binding = await _host.BindProducerOutcomeAsync(token).ConfigureAwait(false);
            if (!binding.AllowDeclareProducerOutcome || binding.RunId is null ||
                _host.Recorder is not IGovernedTaskService service)
                throw new GovernanceException("Producer outcome host grant and bound run are required.");
            var outcome = await service.ExecuteAsync(new(binding.TaskId), new DeclareProducerOutcomeCommand(
                new(binding.ActorId), binding.CausationId is null ? null : new EventId(binding.CausationId),
                binding.CorrelationId, new(binding.RunId), declaration), token).ConfigureAwait(false);
            attempt.InspectionStatus = "ok";
            return JsonSerializer.SerializeToElement(new { status = "ok", task_id = binding.TaskId,
                actor_id = binding.ActorId, run_id = binding.RunId, observed_version = outcome.State.Version,
                replayed = outcome.Events.Count == 0, declaration = outcome.State.Runs[new(binding.RunId)].ProducerOutcome,
                meaning = "attributed self-report; not acceptance or termination" }, LedgerJson.CreateOptions());
        }
        catch (Exception error) when (error is GovernanceException or JsonException or InvalidOperationException)
        {
            attempt.InspectionStatus = "error";
            return JsonSerializer.SerializeToElement(new { status = "error", reason = error.Message });
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            attempt.InspectionStatus = "unknown";
            return JsonSerializer.SerializeToElement(new { status = "unknown",
                reason = "Recording outcome unavailable; retry the identical declaration on the original binding." });
        }
    }

    private static EvidenceId[] EvidenceIds(JsonElement arguments, string name)
    {
        var value = arguments.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 16)
            throw new JsonException("Expected at most 16 evidence IDs.");
        return value.EnumerateArray().Select(item => new EvidenceId(item.GetString() ?? "")).ToArray();
    }
}
