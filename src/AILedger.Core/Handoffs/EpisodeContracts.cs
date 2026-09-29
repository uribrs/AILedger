using AILedger.Core.Artifacts;
using AILedger.Core.Inspection;

namespace AILedger.Core.Handoffs;

// Preparation contracts only: requests describe intent, never authority or execution receipts.
public sealed record EpisodeSpec(int SchemaVersion, string Id, string Profile, string Objective,
    IReadOnlyList<string> NonGoals, IReadOnlyList<string> ExpectedOutputs,
    IReadOnlyList<string> AcceptanceChecks, IReadOnlyList<EpisodeGrant> RequestedGrants,
    EpisodeBudget Budget, IReadOnlyList<string> StopConditions);
public sealed record EpisodeGrant(string Action, string Scope);
public sealed record EpisodeBudget(int MaximumPackageBytes, int MaximumPreparationReads,
    int MaximumAdditionalReads, int MaximumElapsedMinutes, decimal MaximumCostUsd);
public sealed record InputSelection(string Kind, string Id, string Sha256, bool Include, bool Material,
    string Reason, string OmissionRisk, string RetrieveWhen);
// Source content is supplied by the curator, never opened by the preparer. Digest is exact UTF-8.
// Location/version must identify a frozen source, not imply permission to open it.
public sealed record PinnedSource(string Key, string Location, string Version, string Sha256,
    string? Content, bool Material, string Reason, string OmissionRisk, string RetrieveWhen);
public sealed record PreservationCheck(string Category, IReadOnlyList<string> InputKeys, string Assessment);
public sealed record HandoffRequest(int SchemaVersion, string LedgerIdentity, long ExpectedVersion,
    string Selection, EpisodeSpec Spec, IReadOnlyList<InputSelection> Inputs,
    IReadOnlyList<PinnedSource> Sources, IReadOnlyList<PreservationCheck> Preservation);
public sealed record HandoffIndex(string LedgerIdentity, string Selection, InspectionSnapshot Snapshot,
    IReadOnlyList<ContextReference> Records, string OmissionPolicy, int InspectionCalls);
public sealed record PackagedInput(string Key, ContextReference Reference, InputSelection Selection,
    string? RecordJson, ArtifactContentIdentity? Artifact);
public sealed record ArtifactContentIdentity(string ContentReference, string ContentSha256, int ContentBytes);
public sealed record HandoffMeasurements(int VisibleRecords, int IncludedRecords, int OmittedRecords,
    int IncludedSources, int OmittedSources, int IncludedContentBytes, int InspectionCalls,
    int RetrievalCalls, int RetrievedBytes, int? ObservedAdditionalReads = null,
    long? ObservedAdditionalBytes = null, decimal? ObservedCostUsd = null,
    long? ObservedElapsedMilliseconds = null);
public sealed record HandoffPackage(int SchemaVersion, string LedgerIdentity, string Selection,
    InspectionSnapshot Snapshot, EpisodeSpec Spec, string SpecSha256,
    IReadOnlyList<PackagedInput> Inputs, IReadOnlyList<PinnedSource> Sources,
    IReadOnlyList<PreservationCheck> Preservation, string OmissionPolicy, string AuthorityBoundary,
    HandoffMeasurements Measurements);
// Hash/size cover the exact PackageJson UTF-8 string, not a reserialized outer envelope.
public sealed record PreparedHandoff(int SchemaVersion, string PackageSha256, int PackageBytes, string PackageJson);
public sealed record EpisodeCheckResult(string Check, string Status, string Evidence, string Limitation);
public sealed record EpisodeReadObservation(string InputKey, string Sha256, int Bytes, string Reason);
// Authored report, not a process completion, endorsement, artifact registration or acceptance receipt.
public sealed record EpisodeResult(int SchemaVersion, string EpisodeId, string PackageSha256,
    string Status, string Summary, IReadOnlyList<SubmittedArtifact> RecordedOutputs,
    IReadOnlyList<EpisodeCheckResult> Checks, IReadOnlyList<string> Uncertainty,
    IReadOnlyList<string> StopReasons, IReadOnlyList<EpisodeReadObservation>? AdditionalReads,
    string? HostExecutionReceipt);
