using System.Text.Json.Serialization;

namespace AILedger.Core.Contracts;

/// <summary>A producer's explicit assessment of one complete claim-set revision.</summary>
public sealed record InternalReconDocument(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("taskId")] string TaskId,
    [property: JsonPropertyName("claimSetHash")] string ClaimSetHash,
    [property: JsonPropertyName("assessments")] IReadOnlyList<InternalReconAssessment> Assessments,
    [property: JsonPropertyName("report")] string Report);

public sealed record InternalReconAssessment(
    [property: JsonPropertyName("claimId")] string ClaimId,
    [property: JsonPropertyName("domain")] string? Domain);
