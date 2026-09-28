using System.Text;
using System.Text.Json;
using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Core.Findings;
using AILedger.Cli.Findings;

namespace AILedger.Cli.Artifacts;

internal static class ArtifactSubmissionRequestParser
{
    internal static ArtifactSubmissionRequest Parse(JsonElement body)
    {
        if (Encoding.UTF8.GetByteCount(body.GetRawText()) > FindingsValidation.MaximumBodyBytes)
            throw new FindingsRequestException("invalid_request", "The request exceeds 256 KiB.");
        StrictJson.Validate(body);
        StrictJson.Members(body, ["schema_version", "request_id", "kind", "title", "content"],
            ["expected_content_sha256", "supersedes_artifact_id"]);
        if (!FindingsRequestParser.IsVersionOne(body.GetProperty("schema_version"))) throw new JsonException("Expected schema_version 1.");
        var kind = StrictJson.Text(body, "kind") switch
        {
            "verifier-output" => GovernedArtifactKind.VerifierOutput,
            "code-review-output" => GovernedArtifactKind.CodeReviewOutput,
            _ => throw new FindingsRequestException("unsupported_artifact_kind",
                "Only verifier-output and code-review-output are supported; other kinds require the artifact CLI.", "kind")
        };
        return ArtifactSubmissionIdentity.Validate(new(1, StrictJson.Text(body, "request_id"), kind,
            StrictJson.Text(body, "title"), StrictJson.Text(body, "content"),
            FindingsRequestParser.OptionalText(body, "expected_content_sha256"),
            FindingsRequestParser.OptionalText(body, "supersedes_artifact_id")));
    }
}
