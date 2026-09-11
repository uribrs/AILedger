namespace AILedger.Core.Contracts;

/// <summary>
/// What <see cref="RunCompleted"/> puts on the run it closes, in one place, for every reader of the
/// log.
/// </summary>
/// <remarks>
/// This exists because the mapping was written twice and the copies drifted. The canonical reducer
/// projected fifteen fields; <c>HistoricalLedgerProjector</c> projected five, and the ten it dropped
/// were every field added to <c>run.completed</c> since the manifest pair — including the launch
/// limit and the provider's terminal reason (VC3, VE7). Two replays of one log then disagreed about
/// a persisted contract, and the memory projection reported values the log actually held as absent.
///
/// The reason it went unnoticed is worth stating, because it is not the failure mode either reader
/// was guarded against. A *new event type* fails loudly: the projector's switch throws on data it
/// has no arm for. A *new field on an event type whose arm already exists* is dropped in silence —
/// no exception, no failing test, and a reader that cannot tell a value the log never carried from
/// one the projector declined to copy. A checklist that says "give all three readers their arm"
/// does not catch it; only sharing the mapping does.
///
/// So the mapping is not duplicated any more. A field added to <see cref="RunCompleted"/> is
/// projected here once and reaches every reader, or it is projected nowhere and reaches none — and
/// the second is a visible omission rather than a silent divergence.
///
/// What is deliberately *not* here: what each reader does to the run's work item. The canonical
/// reducer excludes an abandoned item from the pause and the memory projector does not, and that is
/// a difference between the two readers rather than a drift in this mapping.
/// </remarks>
public static class RunCompletionProjection
{
    /// <summary>Applies a <c>run.completed</c> payload to the run it closes.</summary>
    public static AgentRun Apply(AgentRun run, RunCompleted completed) => run with
    {
        Status = completed.Status,
        ProviderSessionId = completed.ProviderSessionId,
        EndedAt = completed.EndedAt,
        // Absent on every run completed before these fields existed, and absent on a launch that
        // failed before its manifest was built. Both read as "no brief recorded", correctly.
        ManifestHash = completed.ManifestHash,
        ManifestArtifactCount = completed.ManifestArtifactCount,
        // Absent on every run completed before these existed, and absent on a run whose provider
        // stream produced no terminal event to read them from. Both read as "nobody measured this",
        // correctly. Turns is additionally absent for a provider that states no turn count of its
        // own, which is codex, and there the absence is the measurement (D4, IC1).
        Turns = completed.Turns,
        OutputTokens = completed.OutputTokens,
        MillisecondsToFirstLedgerWrite = completed.MillisecondsToFirstLedgerWrite,
        TokensInUncached = completed.TokensInUncached,
        TokensInCacheWrite = completed.TokensInCacheWrite,
        TokensInCacheRead = completed.TokensInCacheRead,
        // Absent on every run completed before this field existed and on any completion the launcher
        // did not issue. Zero, which is what an ordinary launch records, means the stream was watched
        // and no line was cut — a different fact, and the one that makes a nonzero count readable as
        // a degraded stream rather than as a missing measurement.
        TruncatedLines = completed.TruncatedLines,
        // Absent on every run completed before these two existed, on a run started by hand, and —
        // for the reason — on a run whose provider reported no failure at all. AgentRun states what
        // each absence means; the one worth restating here is that the reason is the provider's own
        // and is never derived from the status beside it (C8).
        LaunchTimeoutSeconds = completed.LaunchTimeoutSeconds,
        TerminalFailureReason = completed.TerminalFailureReason,
        // Which condition ended the run, as the launcher observed it rather than as anything infers
        // it. Absent means nobody observed it, and measure 11 leaves such a row unjudged rather than
        // attributing it (VC4, VC6).
        EndedAtTheLaunchTimeout = completed.EndedAtTheLaunchTimeout,
        // The served model wins over the requested one, because the record should say which cognition
        // ran rather than which was asked for. Absent leaves run.started's value standing rather than
        // erasing it.
        Model = completed.Model ?? run.Model
    };
}
