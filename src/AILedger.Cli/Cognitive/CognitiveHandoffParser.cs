using System.Text;
using System.Text.Json;
using AILedger.Cli.Dispatch;
using AILedger.Cli.Findings;
using AILedger.Core.Contracts;
using AILedger.Core.Findings;
namespace AILedger.Cli.Cognitive;

internal static class CognitiveHandoffParser
{
    internal static CognitiveHostHandoff Parse(JsonElement body)
    {
        StrictJson.Validate(body);
        StrictJson.Members(body, ["request_id", "operation"]);
        if (Encoding.UTF8.GetByteCount(body.GetRawText()) > 128 * 1024) throw new JsonException("Handoff exceeds 128 KiB.");
        var id = Text(body, "request_id", 128);
        if (!FindingsValidation.IsRequestId(id)) throw new JsonException("Invalid request ID.");
        var op = body.GetProperty("operation");
        var kind = Text(op, "kind");
        return new(id, kind switch
        {
            "governing_artifact" => Artifact(op),
            "recon_template" => ReconTemplate(op),
            "lesson_consultation" => Lesson(op),
            "routing_assessment" => Assessment(op),
            "escalation" => Escalation(op),
            "decision_proposal" => Decision(op),
            "decision_resolution" => Resolution(op),
            "preparation_selection" => Selection(op),
            "lesson_mark" => Mark(op),
            "closeout_synthesis" => Closeout(op),
            _ => throw new NotSupportedException("Unsupported cognitive operation.")
        });
    }
    private static CognitiveHostOperation ReconTemplate(JsonElement op)
    {
        StrictJson.Members(op, ["kind"], ["source_paths"]);
        return new ReconTemplateHandoff(op.TryGetProperty("source_paths", out _) ? Strings(op, "source_paths", 256) : null);
    }
    private static CognitiveHostOperation Artifact(JsonElement op)
    {
        StrictJson.Members(op, ["kind", "artifact_kind", "title", "markdown"], ["supersedes"]);
        return new GoverningArtifactHandoff(EnumValue<GoverningArtifactHandoffKind>(op, "artifact_kind"),
            Text(op, "title"), Text(op, "markdown", 100000), Predecessor(op));
    }
    private static CognitiveHostOperation Closeout(JsonElement op)
    {
        StrictJson.Members(op, ["kind", "title", "markdown"], ["supersedes"]);
        return new CloseoutSynthesisHandoff(Text(op, "title"), Text(op, "markdown", 100000), Predecessor(op));
    }
    private static ArtifactId? Predecessor(JsonElement op) => op.TryGetProperty("supersedes", out _) ? new ArtifactId(Text(op, "supersedes", 256)) : null;
    private static CognitiveHostOperation Lesson(JsonElement op)
    {
        StrictJson.Members(op, ["kind", "purpose", "question", "tags", "claims"]);
        return new LessonConsultationHandoff(EnumValue<LessonConsultationPurpose>(op, "purpose"), Text(op, "question"),
            Strings(op, "tags"), Strings(op, "claims", CognitiveHandoffTools.MaximumConsultationClaims)
                .Select(id => new ClaimId(id)).ToArray());
    }
    private static CognitiveHostOperation Assessment(JsonElement op)
    {
        StrictJson.Members(op, ["kind", "work", "disposition", "rationale", "evidence_ids"]);
        return new RoutingAssessmentHandoff(new(EnumValue<CognitiveWorkKind>(op, "work"),
            EnumValue<RoutingDisposition>(op, "disposition"), Text(op, "rationale"), Evidence(op)));
    }
    private static CognitiveHostOperation Escalation(JsonElement op)
    {
        StrictJson.Members(op, ["kind", "escalation_kind", "question", "options", "evidence_ids"], ["recommendation"]);
        return new EscalationHandoff(EnumValue<EscalationKind>(op, "escalation_kind"), Text(op, "question"), Strings(op, "options"),
            op.TryGetProperty("recommendation", out _) ? Text(op, "recommendation") : null, Evidence(op));
    }
    private static CognitiveHostOperation Decision(JsonElement op)
    {
        StrictJson.Members(op, ["kind", "statement", "rationale", "claims"]);
        return new DecisionProposalHandoff(Text(op, "statement"), Text(op, "rationale"),
            Strings(op, "claims").Select(id => new ClaimId(id)).ToArray());
    }
    private static CognitiveHostOperation Resolution(JsonElement op)
    {
        StrictJson.Members(op, ["kind", "decision_id", "status", "expected_version", "evidence_ids", "artifact_ids"]);
        return new DecisionResolutionHandoff(new(Text(op, "decision_id")), EnumValue<DecisionStatus>(op, "status"),
            op.GetProperty("expected_version").GetInt64(), Evidence(op), Strings(op, "artifact_ids").Select(id => new ArtifactId(id)).ToArray());
    }
    private static CognitiveHostOperation Selection(JsonElement op)
    {
        StrictJson.Members(op, ["kind", "decision_id", "plan_id", "request_artifact_id", "profiles", "evidence_ids"]);
        return new PreparationSelectionHandoff(new(new(Text(op, "decision_id")), new(Text(op, "plan_id")),
            new(Text(op, "request_artifact_id")), Strings(op, "profiles"), Evidence(op)));
    }
    private static CognitiveHostOperation Mark(JsonElement op)
    {
        StrictJson.Members(op, ["kind", "source_kind", "source_id", "class", "repository", "tags", "verify", "do_not", "lesson_actor", "verify_expects"]);
        return new LessonMarkHandoff(EnumValue<LessonSourceKind>(op, "source_kind"), Text(op, "source_id"),
            EnumValue<LessonClass>(op, "class"), Text(op, "repository"), Strings(op, "tags"), Text(op, "verify"),
            Text(op, "do_not"), EnumValue<LessonActor>(op, "lesson_actor"), EnumValue<VerifyExpectation>(op, "verify_expects"));
    }
    private static EvidenceId[] Evidence(JsonElement op) => Strings(op, "evidence_ids").Select(id => new EvidenceId(id)).ToArray();
    private static string[] Strings(JsonElement op, string name, int maximum = 16) => FindingsRequestParser.Array(op.GetProperty(name), maximum)
        .Select(value => value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 8192 } text
            ? text : throw new JsonException("Expected bounded nonempty strings.")).ToArray();
    private static string Text(JsonElement op, string name, int maximum = 8192)
    {
        var value = StrictJson.Text(op, name);
        return !string.IsNullOrWhiteSpace(value) && value.Length <= maximum ? value : throw new JsonException("Expected bounded nonempty text.");
    }
    private static T EnumValue<T>(JsonElement op, string name) where T : struct, Enum
    {
        var text = Text(op, name);
        foreach (var value in Enum.GetValues<T>())
            if (JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()) == text) return value;
        throw new JsonException("Unknown enum value.");
    }
}
