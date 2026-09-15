namespace AILedger.Core.Contracts;

internal sealed record PendingBriefWaiver(
    EventId EventId,
    ActorId ActorId,
    string? OperatorReason,
    EvidenceId? StaleBriefEvidenceId,
    WaiverProvenance? Provenance);
