namespace AILedger.Core.Contracts;

// One command proceeded past the context gate without a current brief, and what carried it. Emitted
// before the event it accompanies, the way StagePrerequisitesWaived precedes its transition, so a
// later reader sees which gate was opened rather than only that work was added.
//
// Exactly one of the two justifications is set, because an absent brief and a stale one are
// different failures (C7). An absent brief can only be carried by an operator's decision — there is
// no evidence that an unread brief was read — and a stale one by an evidence record naming what
// changed. A waiver carrying both would say which was true of neither.
public sealed record ContextBriefWaived(
    string Action,
    string? OperatorReason = null,
    EvidenceId? StaleBriefEvidenceId = null,
    WaiverProvenance? Provenance = null) : LedgerEventData;
