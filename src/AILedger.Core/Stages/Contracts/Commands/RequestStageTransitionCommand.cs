namespace AILedger.Core.Contracts;

public sealed record RequestStageTransitionCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    TaskStage TargetStage,
    // Why the operator is advancing without satisfying the target stage's command-time arm.
    // The handler emits this as its own event so the override remains visible in the log.
    string? WithoutPrerequisitesReason = null,
    // Why the stage moved at all, carried onto StageTransitioned.
    //
    // Hazard: this record ends in two nullable strings that mean opposite things — one excuses a
    // refusal, the other narrates a move that was allowed. A positional call passing one string
    // silently binds it to WithoutPrerequisitesReason. Pass either of them by name.
    string? Reason = null,
    // Which recorded alternative says why an execution that could have been dispatched concurrently
    // was run serially instead. Carried onto StageTransitioned.
    //
    // This third trailing parameter is outside the hazard above, and its type is why. A caller
    // cannot bind a string to it or an AlternativeId to either string: the compiler refuses both
    // directions, so no positional call can put this value in a reason's slot or a reason's text in
    // this one. A third nullable string would have widened the hazard; a distinct type closes it.
    AlternativeId? SerialJustification = null) : LedgerCommand(ActorId, CausationId, CorrelationId);
