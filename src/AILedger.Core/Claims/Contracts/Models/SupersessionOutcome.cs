namespace AILedger.Core.Contracts;

// Superseding is not one act. A refinement sharpens a claim and its dependents stay
// valid; a correction narrows or contradicts it and they do not. The kernel derives
// which from state, never from a flag set by the actor doing the superseding.
public enum SupersessionOutcome
{
    Correction,
    Refinement
}
