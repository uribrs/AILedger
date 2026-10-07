using System.Text.Json;
using System.Text.Json.Serialization;

namespace AILedger.Core.Assurance;

// Operator-owned policy. Never deserialized from an agent operation or a retrieved package.
public sealed record AssurancePolicy(int SchemaVersion, string CaseId, string CandidateRoot,
    string Implementer, IReadOnlyList<AssuranceArea> Areas, IReadOnlyList<AssurancePrincipal> Principals,
    IReadOnlyList<AssuranceCheck> Checks)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GovernedAssuranceScope? Governed { get; init; }
}
public sealed record AssuranceArea(string Id, IReadOnlyList<string> CandidatePaths,
    IReadOnlyList<string> RequirementPaths, IReadOnlyList<string> SourcePaths,
    IReadOnlyList<string> DependsOnAreas, IReadOnlyList<AssuranceCriterion> Criteria);
public sealed record AssuranceCriterion(string Id, string Description, string CheckId);
public sealed record AssurancePrincipal(string Id, string Role, bool Enabled, DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Areas);
public sealed record AssuranceCheck(string Id, string Executable, string ExecutableSha256,
    IReadOnlyList<string> Arguments, int TimeoutSeconds);
public sealed record AssuranceSession(string Principal, string SessionId, string Provider, string? RequestedModel);
public interface IAssurancePolicySource
{
    Task<AssurancePolicy> ReadAsync(CancellationToken token);
}
public interface IAssuranceService
{
    IReadOnlyList<string> Operations { get; }
    Task<AssuranceResponse> InvokeAsync(string operation, JsonElement request, CancellationToken token);
}
public sealed record AssuranceResponse(string Status, string AttemptId, AssuranceReceipt? Receipt,
    bool Replayed, JsonElement? Data, AssuranceError? Error);
public sealed record AssuranceError(string Code, string Message, string Recovery, string CommitState);
public sealed record AssuranceReceipt(string Id, string Operation, string RequestId, string RequestSha256,
    string Principal, string SessionId, string Provider, string? RequestedModel, string PolicySha256, string ScopePolicySha256,
    string ContentSha256, int ContentBytes, string ContentReference, DateTimeOffset RecordedAt);
public sealed record AssuranceEntry(AssuranceReceipt Receipt, JsonElement Payload);
public sealed record AssuranceInput(string Kind, string Path, string Sha256, int Bytes, string Content);
public sealed record AssuranceSnapshot(string AreaId, string CandidateSha256, string RequirementSha256,
    string BindingSha256, IReadOnlyList<AssuranceInput> Inputs, IReadOnlyDictionary<string, string> Dependencies,
    IReadOnlyList<AssuranceCriterion> Criteria, string Implementer, string Origin)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GovernedAssuranceBasis? Governed { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PhysicalBindingSha256 { get; init; }
}
public sealed record InspectAssuranceRequest(int SchemaVersion, string AreaId, string? ReceiptId = null, int Offset = 0, int? ExpectedVersion = null);
public sealed record ReadAssuranceRequest(int SchemaVersion, string RequestId, string AreaId,
    string ExpectedBinding, IReadOnlyList<string> Paths);
public sealed record RunAssuranceChecksRequest(int SchemaVersion, string RequestId, string AreaId,
    string ExpectedBinding, IReadOnlyList<string> CheckIds);
public sealed record RecordAssuranceRequest(int SchemaVersion, string RequestId, string AreaId,
    string ExpectedBinding, string Status, string Summary, IReadOnlyList<string> InspectedPaths,
    IReadOnlyList<string> ReadReceipts, IReadOnlyList<AssuranceObservation> Checks,
    IReadOnlyList<AssuranceFinding> Findings, IReadOnlyList<string> Uncertainty,
    string? Supersedes = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AssuranceFindingJudgment>? FindingJudgments { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AssuranceRepairImpact? RepairImpact { get; init; }
}
public sealed record AssuranceObservation(string CriterionId, string Status, string Evidence,
    IReadOnlyList<string> TestReceipts);
public sealed record AssuranceFinding(string Key, string Kind, string Statement, IReadOnlyList<string> Paths,
    IReadOnlyList<string> Contradicts)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RequirementIds { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Uncertainty { get; init; }
}
// Adjudication preserves the original finding and uses host-observed evidence.
public sealed record AssuranceFindingJudgment(int SchemaVersion, string FindingId, string Decision,
    string Rationale, IReadOnlyList<string> RequirementIds, IReadOnlyList<string> Uncertainty,
    IReadOnlyList<AssuranceFindingEvidence> Evidence);
public sealed record AssuranceFindingEvidence(string Kind, string ReceiptId, string PathOrCheckId,
    string Observation);
public sealed record AcceptAssuranceRequest(int SchemaVersion, string RequestId, string AreaId,
    string ExpectedBinding, IReadOnlyList<string> Reports, string Rationale,
    IReadOnlyList<AssuranceDisposition> Dispositions);
public sealed record AssuranceDisposition(string FindingId, string Decision, string Rationale,
    IReadOnlyList<string> EvidenceReports);
public sealed record AssuranceReport(AssuranceSnapshot Snapshot, RecordAssuranceRequest Report,
    IReadOnlyList<string> UninspectedPaths, string Role);
public sealed record AssuranceTestResult(string CheckId, string Outcome, int? ExitCode,
    string StandardOutput, string StandardError, int? TruncatedLines, string Environment,
    string ExecutableSha256, IReadOnlyList<string> Arguments, DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt, string? Limitation);
public sealed record AssuranceTestBatch(AssuranceSnapshot Snapshot, IReadOnlyList<AssuranceTestResult> Tests);
public sealed record AssuranceAcceptance(AssuranceSnapshot Snapshot, AcceptAssuranceRequest Decision);
public sealed record AssuranceRead(AssuranceSnapshot Snapshot, IReadOnlyList<AssuranceInput> Inputs);
public sealed record AssuranceApplicability(string Id, string Operation, string Principal,
    string Status, IReadOnlyList<string> Reasons, JsonElement? Payload);
public sealed record AssuranceInspection(AssuranceSnapshot Snapshot, IReadOnlyList<AssuranceApplicability> History,
    IReadOnlyList<string> UninspectedPaths, IReadOnlyList<string> UntestedCriteria,
    IReadOnlyList<string> UnresolvedFindings, string Acceptance, string Measurement,
    int UncommittedFiles, IReadOnlyList<string> PendingChecks, int JournalVersion, int TotalHistory, int? NextOffset, IReadOnlyList<JsonElement> CheckAttempts);

// Authored judgment; the host separately retains before/after physical observations.
public sealed record AssuranceRepairImpact(int SchemaVersion, string BeforeReceipt,
    IReadOnlyList<string> AffectedAreas, IReadOnlyList<string> RequirementIds,
    IReadOnlyList<string> DependencyAreas, string DependencyRationale,
    IReadOnlyList<string> RequiredChecks, IReadOnlyList<string> AddressedFindings,
    IReadOnlyList<string> ReuseReceipts, IReadOnlyList<string> InvalidateReceipts,
    IReadOnlyList<string> Uncertainty)
{
    public IReadOnlyList<string> ImpactReadReceipts { get; init; } = [];
}
