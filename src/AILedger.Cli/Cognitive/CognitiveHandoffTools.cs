using System.Text.Json;
namespace AILedger.Cli.Cognitive;

internal static class CognitiveHandoffTools
{
    internal const int MaximumConsultationClaims = 4096;
    internal static object Describe(IReadOnlyList<string>? consultationPurposes = null) => new
    {
        name = "cognitive_handoff",
        description = "Live host-bound cognitive operations; use only operations permitted by your role and stage. " +
            ConsultationGuidance(consultationPurposes ?? []) +
            " Routing assessments are judgments, never acceptance. No caller identities or approval fields. Exact mutation key/body retries return their receipt; uncertain writes require the original key/body/binding. Enum values use snake_case.",
        inputSchema = Schema(consultationPurposes ?? []),
        annotations = new { readOnlyHint = false, destructiveHint = false, idempotentHint = true, openWorldHint = false }
    };
    private static string ConsultationGuidance(IReadOnlyList<string> purposes) => purposes.Count == 0
        ? "No lesson consultation purpose is available for this role/run/stage. Workers consume recalled lessons from their brief as unverified context; record applicable findings with from_lesson. Do not initiate recon, research or reconsideration consultation."
        : purposes.Contains("recon")
            ? "For InternalRecon, after findings and claim changes, consult recon lessons, call recon_template (read-only, fresh on each call), classify every claim and fill report. Supply source_paths with the absolute files actually inspected; the host returns measured hashes in version-2 sourceReview. Fill scope, per-file claimIds/evidenceIds/assessment, reuseReason and uninspected. Named evidence must directionally support/refute the mapped claims. Empty file selection requires an explicit reuse/inapplicability reason. Hashes are observations, not proof of reading; only declared files are checked at filing and forward Design/Scope. Recon/reconsideration consultation may use claims: []; the host binds the complete current claim set automatically. This does not waive classifying every claim in InternalRecon. Submit the complete strict JSON as the governing_artifact markdown string. The host never classifies claims for you. In Design, the same eligible planning run can refresh recon and revise the contract/plan before returning; no separate recon-only run is required. The host must complete this producer before forward admission."
            : "Researcher: use lesson_consultation only with purpose research in Research, naming the claims being investigated. Do not perform a lead's recon or reconsideration consultation.";

    private static object Schema(IReadOnlyList<string> purposes) => new
    {
        type = "object", additionalProperties = false, required = new[] { "request_id", "operation" },
        properties = new { request_id = Text(128), operation = new { oneOf = Operations(purposes) } }
    };
    private static object[] Operations(IReadOnlyList<string> purposes) => [
        .. (purposes.Contains("recon") ? new[] { Operation("recon_template", new() { ["source_paths"] = Strings(256) }, "source_paths") } : []),
        Operation("governing_artifact", new() { ["artifact_kind"] = Values("internal_recon", "prompt_contract", "orchestration_plan"), ["title"] = Text(), ["markdown"] = Text(100000), ["supersedes"] = Text(256) }, "supersedes"),
        .. (purposes.Count > 0 ? new[] { Operation("lesson_consultation", new() { ["purpose"] = Values(purposes.ToArray()), ["question"] = Text(), ["tags"] = Strings(), ["claims"] = Strings(MaximumConsultationClaims) }) } : []),
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
    private static object Strings(int maximum = 16) => new { type = "array", maxItems = maximum, items = Text() };
    private static object Values(params string[] values) => new { type = "string", @enum = values };
    private static object EnumValues<T>() where T : struct, Enum => Values(Enum.GetNames<T>().Select(JsonNamingPolicy.SnakeCaseLower.ConvertName).ToArray());
}
