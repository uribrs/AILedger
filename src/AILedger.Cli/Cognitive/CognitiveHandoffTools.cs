using System.Text.Json;
namespace AILedger.Cli.Cognitive;

internal static class CognitiveHandoffTools
{
    internal static object Describe() => new
    {
        name = "cognitive_handoff",
        description = "Live host-bound cognitive operations. File governing artifacts before returning. Routing assessments are evidence-backed judgments, never acceptance. No caller identities or approval fields. Exact key/body retries on this session return its receipt; uncertain writes require the original key/body/binding for durable reconciliation. Enum values use snake_case.",
        inputSchema = Schema(),
        annotations = new { readOnlyHint = false, destructiveHint = false, idempotentHint = true, openWorldHint = false }
    };
    private static object Schema() => new
    {
        type = "object", additionalProperties = false, required = new[] { "request_id", "operation" },
        properties = new { request_id = Text(128), operation = new { oneOf = Operations() } }
    };
    private static object[] Operations() => [
        Operation("governing_artifact", new() { ["artifact_kind"] = Values("internal_recon", "prompt_contract", "orchestration_plan"), ["title"] = Text(), ["markdown"] = Text(100000), ["supersedes"] = Text(256) }, "supersedes"),
        Operation("lesson_consultation", new() { ["purpose"] = Values("recon", "research", "reconsideration"), ["question"] = Text(), ["tags"] = Strings(), ["claims"] = Strings() }),
        Operation("routing_assessment", new() { ["work"] = Values("discovery", "recon", "research", "design", "scope", "implementation", "verification", "review", "repair", "closeout", "findings"), ["disposition"] = Values("proceed", "repair", "replan", "blocked"), ["rationale"] = Text(), ["evidence_ids"] = Strings() }),
        Operation("escalation", new() { ["escalation_kind"] = Values("business_decision", "true_unknown"), ["question"] = Text(), ["options"] = Strings(), ["evidence_ids"] = Strings(), ["recommendation"] = Text() }, "recommendation"),
        Operation("decision_proposal", new() { ["statement"] = Text(), ["rationale"] = Text(), ["claims"] = Strings() }),
        Operation("decision_resolution", new() { ["decision_id"] = Text(), ["status"] = Values("accepted", "superseded"), ["expected_version"] = new { type = "integer", minimum = 1 }, ["evidence_ids"] = Strings(), ["artifact_ids"] = Strings() }),
        Operation("preparation_selection", new() { ["decision_id"] = Text(), ["plan_id"] = Text(), ["request_artifact_id"] = Text(), ["profiles"] = Strings(), ["evidence_ids"] = Strings() }),
        Operation("lesson_mark", new() { ["source_kind"] = Values("validated_claim", "rejected_claim", "rejected_alternative", "resolved_escalation"), ["source_id"] = Text(), ["class"] = EnumValues<AILedger.Core.Contracts.LessonClass>(), ["repository"] = Text(), ["tags"] = Strings(), ["verify"] = Text(), ["do_not"] = Text(), ["lesson_actor"] = EnumValues<AILedger.Core.Contracts.LessonActor>(), ["verify_expects"] = Values("present", "absent") }),
        Operation("closeout_synthesis", new() { ["title"] = Text(), ["markdown"] = Text(100000), ["supersedes"] = Text(256) }, "supersedes")
    ];
    private static object Operation(string kind, Dictionary<string, object> properties, params string[] optional)
    {
        properties.Add("kind", new { type = "string", @const = kind });
        return new { type = "object", additionalProperties = false, required = properties.Keys.Except(optional).ToArray(), properties };
    }
    private static object Text(int maximum = 8192) => new { type = "string", minLength = 1, maxLength = maximum };
    private static object Strings() => new { type = "array", maxItems = 16, items = Text() };
    private static object Values(params string[] values) => new { type = "string", @enum = values };
    private static object EnumValues<T>() where T : struct, Enum => Values(Enum.GetNames<T>().Select(JsonNamingPolicy.SnakeCaseLower.ConvertName).ToArray());
}
