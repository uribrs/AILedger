namespace AILedger.Core.Contracts;

public enum LessonSourceKind
{
    ValidatedClaim,
    // A belief that was disproved, which is a different record from an approach that was
    // discarded. Only a rejected claim carries the evidence that refuted it, so it is the
    // record a Refuted lesson is minted from; RejectedAlternative carries a rationale for not
    // taking a path and no evidence at all. Conflating them loses the counter-evidence.
    RejectedClaim,
    RejectedAlternative,
    ResolvedEscalation,
    // A lesson carried in from the pre-kernel ledger, where the source was an assumption
    // disposition in a verifier's report rather than a record this kernel holds. No event ever
    // carries it: an imported lesson is written into the cross-repository store and read back by
    // recall, so it is never marked and never minted. 'lesson mark' must refuse it for that reason.
    Imported
}
