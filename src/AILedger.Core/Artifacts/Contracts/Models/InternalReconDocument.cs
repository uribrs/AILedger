using System.Text.Json.Serialization;

namespace AILedger.Core.Contracts;

/// <summary>A producer's explicit assessment of one complete claim-set revision.</summary>
public sealed record InternalReconDocument(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("taskId")] string TaskId,
    [property: JsonPropertyName("claimSetHash")] string ClaimSetHash,
    [property: JsonPropertyName("assessments")] IReadOnlyList<InternalReconAssessment> Assessments,
    [property: JsonPropertyName("report")] string Report)
{
    [JsonPropertyName("sourceReview")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReconSourceReview? SourceReview { get; init; }
}

public sealed record ReconSourceReview(
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("files")] IReadOnlyList<ReconSourceFile> Files,
    [property: JsonPropertyName("reuseReason")] string ReuseReason,
    [property: JsonPropertyName("uninspected")] IReadOnlyList<string> Uninspected);

public sealed record ReconSourceFile(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("claimIds")] IReadOnlyList<string> ClaimIds,
    [property: JsonPropertyName("evidenceIds")] IReadOnlyList<string> EvidenceIds,
    [property: JsonPropertyName("assessment")] string Assessment);

public sealed record InternalReconAssessment(
    [property: JsonPropertyName("claimId")] string ClaimId,
    [property: JsonPropertyName("domain")] string? Domain);
