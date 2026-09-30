namespace AILedger.Core.Contracts;

// A bounded producer declaration, never independent acceptance or run termination.
public sealed record ProducerOutcome(
    string Outcome,
    IReadOnlyList<EvidenceId> OutputEvidenceIds,
    IReadOnlyList<EvidenceId> BlockerEvidenceIds);

public sealed record DeclareProducerOutcomeCommand(
    ActorId ActorId, EventId? CausationId, string CorrelationId, RunId RunId,
    ProducerOutcome Declaration) : LedgerCommand(ActorId, CausationId, CorrelationId);

public sealed record ProducerOutcomeDeclared(RunId RunId, ProducerOutcome Declaration) : LedgerEventData;
