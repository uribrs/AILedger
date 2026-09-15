namespace AILedger.Core.Contracts;

public sealed record ArtifactRecorded(GovernedArtifact Artifact) : LedgerEventData;
