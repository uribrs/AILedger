using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Findings;
using AILedger.Core.Alternatives;
using AILedger.Core.Artifacts;
using AILedger.Core.ClaimDispositions;

namespace AILedger.Core.Inspection;

public interface ITaskInspector
{
    Task<InspectionResult> InspectAsync(InspectionBinding binding, InspectionQuery query, CancellationToken cancellationToken);
    Task<RetrievalResult> RetrieveAsync(InspectionBinding binding, RetrievalQuery query, CancellationToken cancellationToken);
    Task<ReadinessResult> CheckReadinessAsync(InspectionBinding binding, ReadinessQuery query, CancellationToken cancellationToken);
}

// Trusted host input only. Neither attribution, grants nor observed cognitive content is tool input.
public sealed record InspectionBinding(TaskId TaskId, ActorId ActorId, RunId? RunId, string CorrelationId,
    EventId? CausationId = null, bool AllowInspect = false, bool AllowRunless = false,
    bool AllowRecordFindings = false, bool AllowRecordAlternatives = false,
    bool AllowSubmitArtifact = false, bool AllowRecordClaimDispositions = false,
    IReadOnlyList<ContextArtifact>? CognitiveArtifacts = null);

public sealed record InspectionQuery(int SchemaVersion = 1, string Selection = "relevant", int Offset = 0,
    int Limit = 20, long? ExpectedVersion = null);
public sealed record RetrievalQuery(int SchemaVersion, string Selection, string Kind, string Id,
    long ExpectedVersion, int Offset = 0, int Length = 8192, string? ExpectedSha256 = null);
public sealed record InspectionSnapshot(string TaskId, string ActorId, string? RunId, long LedgerVersion,
    string LastEventId, DateTimeOffset ObservedAt, string Stage, string Role, IReadOnlyList<string> Capabilities,
    string? CandidateId, string BriefFreshness);
public sealed record ContextReference(string Kind, string Id, string Summary, bool SummaryTruncated,
    int JsonCharacters, string Sha256, RetrievalQuery Retrieve);
public sealed record InspectionDiagnostic(string Code, string Action, string? Reference, string Prerequisite,
    string Reason, string Recovery);
public sealed record InspectionResult(string Status, InspectionSnapshot? Snapshot,
    IReadOnlyList<ContextReference> Records, int TotalSelected, int? NextOffset,
    string OmissionPolicy, IReadOnlyList<string> SupportedActions, InspectionDiagnostic? Diagnostic)
{
    public int SchemaVersion => 1;
}
// Data is a typed record serialized with LedgerJson (Claim, Evidence, Decision, ContextArtifact,
// ContextBuild or the named v1 receipt). Oversize records use exact JSON string chunks instead.
public sealed record RetrievalResult(string Status, InspectionSnapshot? Snapshot, string Kind, string Id,
    JsonElement? Data, string? JsonChunk, int? NextOffset, int TotalCharacters, string? Sha256,
    InspectionDiagnostic? Diagnostic)
{
    public int SchemaVersion => 1;
}

public sealed record WorkPreparation(string Id, string Title, string? Owner, IReadOnlyList<string> Claims,
    IReadOnlyList<string> Scope, string? NotSplitJustification = null);
public sealed record StageProposal(TaskStage Stage, string? Reason = null, string? SerialJustification = null);
// A closed action set, not a LedgerCommand/dry-run wrapper. One corresponding proposal is required.
public sealed record ReadinessQuery(int SchemaVersion, string Action, long ExpectedVersion,
    FindingsRequest? Findings = null, AlternativesRequest? Alternatives = null,
    ArtifactSubmissionRequest? Artifact = null, ClaimDispositionsRequest? Dispositions = null,
    WorkPreparation? Work = null, string? WorkId = null, StageProposal? Transition = null);
public sealed record ReadinessResult(string Status, string Action, InspectionSnapshot? Snapshot,
    InspectionDiagnostic? Diagnostic, string Execution, string CandidateInspection = "not_applicable",
    string EnvironmentInspection = "ledger_only", string Measurement = "not_collected")
{
    public int SchemaVersion => 1;
    public bool IsGrant => false;
}
