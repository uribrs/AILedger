using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Findings;

namespace AILedger.Core.Artifacts;

public static class ArtifactSubmissionIdentity
{
    public const string Algorithm = "artifact-submission-v1-c14n1";
    public const int MaximumContentBytes = 128 * 1024;
    public static string ContentHash(string content) => Hash(Encoding.UTF8.GetBytes(content));
    public static bool IsHash(string? value) => value?.Length == 64 &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string KindName(GovernedArtifactKind kind) => kind switch
    {
        GovernedArtifactKind.VerifierOutput => "verifier-output",
        GovernedArtifactKind.CodeReviewOutput => "code-review-output",
        _ => throw new FindingsRequestException("unsupported_artifact_kind",
            "submit_artifact supports only verifier-output and code-review-output; use the existing artifact CLI for other kinds.", "kind")
    };

    public static ArtifactSubmissionRequest Validate(ArtifactSubmissionRequest request)
    {
        if (request is null || request.SchemaVersion != 1 || !FindingsValidation.IsRequestId(request.RequestId))
            throw new FindingsRequestException("invalid_request", "Expected schema version 1 and a valid request ID.");
        _ = KindName(request.Kind);
        FindingsValidation.Text(request.Title, 512, "title");
        FindingsValidation.Text(request.Content, MaximumContentBytes, "content");
        FindingsValidation.OptionalText(request.SupersedesArtifactId, 256, "supersedes_artifact_id");
        if (request.ExpectedContentSha256 is { } hash && !IsHash(hash))
            throw new FindingsRequestException("invalid_request", "Expected a lowercase SHA-256 hex digest.", "expected_content_sha256");
        if (Encoding.UTF8.GetByteCount(request.Content) > MaximumContentBytes || RequestBytes(request).Length > FindingsValidation.MaximumBodyBytes)
            throw new FindingsRequestException("invalid_request", "Content exceeds 128 KiB UTF-8 or the request exceeds 256 KiB.", "content");
        if (request.ExpectedContentSha256 is { } expected && expected != ContentHash(request.Content))
            throw new FindingsRequestException("content_identity_mismatch", "Content differs from expected_content_sha256; restore the intended body or correct the assertion.", "expected_content_sha256");
        return request;
    }

    public static string Compute(ArtifactSubmissionBinding binding, ArtifactSubmissionRequest request) => Hash(Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("operation", "submit_artifact");
        writer.WriteNumber("schema_version", 1);
        writer.WriteString("task_id", binding.TaskId.Value);
        writer.WriteString("actor_id", binding.ActorId.Value);
        writer.WriteString("run_id", binding.RunId.Value);
        writer.WriteString("correlation_id", binding.CorrelationId);
        writer.WriteString("causation_id", binding.CausationId?.Value);
        Body(writer, request);
        writer.WriteEndObject();
    }));

    private static byte[] RequestBytes(ArtifactSubmissionRequest request) => Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteNumber("schema_version", request.SchemaVersion);
        writer.WriteString("request_id", request.RequestId);
        Body(writer, request);
        writer.WriteEndObject();
    });
    private static void Body(Utf8JsonWriter writer, ArtifactSubmissionRequest request)
    {
        writer.WriteString("kind", KindName(request.Kind));
        writer.WriteString("title", request.Title);
        writer.WriteString("content", request.Content);
        writer.WriteString("expected_content_sha256", request.ExpectedContentSha256);
        writer.WriteString("supersedes_artifact_id", request.SupersedesArtifactId);
    }
    private static byte[] Write(Action<Utf8JsonWriter> action)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) action(writer);
        return stream.ToArray();
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // This reference identifies inline immutable content; it is not a path or a fetchable URL.
    public static SubmittedArtifact Describe(string taskId, GovernedArtifact artifact) => new(
        artifact.ArtifactId.Value, KindName(artifact.Kind), artifact.Title, "text/markdown; charset=utf-8",
        ContentHash(artifact.Content), Encoding.UTF8.GetByteCount(artifact.Content),
        $"ailedger-artifact:{Uri.EscapeDataString(taskId)}:{Uri.EscapeDataString(artifact.ArtifactId.Value)}",
        artifact.WorkItemId?.Value, artifact.ProducerRunId!.Value.Value, artifact.SupersedesArtifactId?.Value,
        Array.AsReadOnly((artifact.Assurance?.WorkItemIds.Select(id => id.Value) ?? [artifact.WorkItemId!.Value.Value]).ToArray()),
        artifact.Assurance?.CandidateId, artifact.Assurance?.VerifierRunId?.Value,
        Array.AsReadOnly(artifact.Assurance?.WorkVersions.Select(v => new SubmittedWorkVersion(v.WorkItemId.Value, v.WorkingRunId.Value)).ToArray() ?? []),
        Array.AsReadOnly(artifact.MemberReplacements?.Select(r => new SubmittedReplacement(r.WorkItemId.Value,
            Array.AsReadOnly(r.ArtifactIds.Select(id => id.Value).ToArray()))).ToArray() ?? []));
}
