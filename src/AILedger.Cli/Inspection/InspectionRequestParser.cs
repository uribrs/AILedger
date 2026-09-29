using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AILedger.Core.Inspection;
using AILedger.Core.Findings;
using AILedger.Cli.Findings;
using AILedger.Cli.Alternatives;
using AILedger.Cli.Artifacts;
using AILedger.Cli.ClaimDispositions;

namespace AILedger.Cli.Inspection;

internal static class InspectionRequestParser
{
    internal static JsonSerializerOptions Json { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) }
    };

    internal static T Read<T>(JsonElement body)
    {
        Validate(body);
        if (typeof(T) == typeof(InspectionQuery))
            StrictJson.Members(body, ["schema_version"], ["selection", "offset", "limit", "expected_version"]);
        if (typeof(T) == typeof(RetrievalQuery))
            StrictJson.Members(body, ["schema_version", "selection", "kind", "id", "expected_version", "expected_sha256"], ["offset", "length"]);
        if (typeof(T) == typeof(WorkPreparation))
            StrictJson.Members(body, ["id", "title", "claims", "scope"], ["owner", "not_split_justification"]);
        if (typeof(T) == typeof(StageProposal))
            StrictJson.Members(body, ["stage"], ["reason", "serial_justification"]);
        return body.Deserialize<T>(Json) ?? throw new JsonException("Missing typed request.");
    }

    internal static ReadinessQuery Readiness(JsonElement body)
    {
        Validate(body);
        StrictJson.Members(body, ["schema_version", "action", "expected_version"], ["proposal"]);
        var version = body.GetProperty("schema_version").GetInt32();
        var action = StrictJson.Text(body, "action");
        var expected = body.GetProperty("expected_version").GetInt64();
        var proposal = body.TryGetProperty("proposal", out var p) ? p : default;
        return action switch
        {
            "record_findings" => new(version, action, expected, Findings: FindingsRequestParser.Parse(proposal)),
            "record_alternatives" => new(version, action, expected, Alternatives: AlternativesRequestParser.Parse(proposal)),
            "submit_artifact" => new(version, action, expected, Artifact: ArtifactSubmissionRequestParser.Parse(proposal)),
            "record_claim_dispositions" => new(version, action, expected, Dispositions: ClaimDispositionsRequestParser.Parse(proposal)),
            "prepare_work" => new(version, action, expected, Work: Read<WorkPreparation>(proposal)),
            "complete_work" => new(version, action, expected, WorkId: WorkId(proposal)),
            "transition_stage" => new(version, action, expected, Transition: Read<StageProposal>(proposal)),
            _ => new(version, action, expected)
        };
    }

    private static string WorkId(JsonElement proposal)
    {
        StrictJson.Members(proposal, ["work_id"]);
        return StrictJson.Text(proposal, "work_id");
    }

    private static void Validate(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(body.GetRawText()) > FindingsValidation.MaximumBodyBytes)
            throw new JsonException("Expected an object of at most 256 KiB.");
        StrictJson.Validate(body);
    }
}
