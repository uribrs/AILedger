using AILedger.Core.Contracts;

namespace AILedger.Core.ContextBriefing;

public sealed record ContractRequirement(string Id, string Requirement, string Status,
    string Authority, string Source, IReadOnlyList<string> References, string? Detail = null, int? ReferenceCount = null);
public sealed record ActionContract(string Action, string ActorRole, string SubjectRole,
    string RequiredShape, IReadOnlyList<ContractRequirement> Requirements);
public sealed record ContractBindings(string? WorkId, string? ProducerRunId, string? ProducerActorId,
    string? CandidateId, string? VerifierRunId, AssuranceBinding? Assurance);
public sealed record RunReturnObservation(string RunId, string RecordedTermination,
    string RequiredOutputPresence, IReadOnlyList<string> OutputReferences,
    string AttributedOutcome, ProducerOutcome? ProducerDeclaration, string TechnicalAcceptance = "unknown",
    IReadOnlyList<VerifierDispositionObservation>? VerifierDispositions = null, int OutputReferenceCount = 0);
public sealed record NextActionContract(long ObservedTaskVersion, string DeliveryStatus,
    IReadOnlyList<ActionContract> Actions, ContractBindings Bindings, RunReturnObservation? Return,
    string Notice = "Observation only: not authority, acceptance, a reservation or a freshness guarantee. Execution revalidates.",
    string? Diagnostic = null);
