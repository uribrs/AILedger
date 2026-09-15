namespace AILedger.Core.Contracts;

// Why the stage moved, and it trails the two stages it is about because every stage.transitioned
// already on disk carries none, and replay must keep reading those. LedgerJson writes null fields
// not at all, so a transition with no reason serialises exactly as it always did.
//
// The alternative naming why an execution that could have been dispatched concurrently was run
// serially instead. It trails the reason for the same reason the reason trails the stages: every
// stage.transitioned already on disk carries none, LedgerJson omits null fields
// (LedgerJson.cs, DefaultIgnoreCondition = WhenWritingNull), so an event without one serialises
// byte-identically to the ones already written. No converter and no change to any existing shape.
//
// An id and not free text, for the reason WorkItem.NotSplitJustification is one: the kernel cannot
// judge whether serial execution was right, but it can refuse to let the choice go unrecorded and
// unattributed, and only a recorded alternative attributes it.
public sealed record StageTransitioned(
    TaskStage Previous,
    TaskStage Current,
    string? Reason = null,
    AlternativeId? SerialJustification = null) : LedgerEventData;
