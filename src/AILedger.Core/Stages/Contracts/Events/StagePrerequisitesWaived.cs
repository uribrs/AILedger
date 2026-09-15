namespace AILedger.Core.Contracts;

public sealed record StagePrerequisitesWaived(
    TaskStage TargetStage,
    string Reason,
    WaiverProvenance? Provenance = null) : LedgerEventData;
