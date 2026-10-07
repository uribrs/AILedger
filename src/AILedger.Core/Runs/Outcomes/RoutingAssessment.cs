namespace AILedger.Core.Contracts;

// Authored routing judgment, never technical acceptance or authority to complete work.
public enum CognitiveWorkKind { Discovery, Recon, Research, Design, Scope, Implementation, Verification, Review, Repair, Closeout, Findings }
public enum RoutingDisposition { Proceed, Repair, Replan, Blocked }
public sealed record RoutingAssessment(CognitiveWorkKind Work, RoutingDisposition Disposition,
    string Rationale, IReadOnlyList<EvidenceId> EvidenceIds);
public sealed record RecordRoutingAssessmentCommand(ActorId ActorId, EventId? CausationId,
    string CorrelationId, RunId RunId, RoutingAssessment Assessment)
    : LedgerCommand(ActorId, CausationId, CorrelationId);
public sealed record RoutingAssessmentRecorded(RunId RunId, RoutingAssessment Assessment) : LedgerEventData;
