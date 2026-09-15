namespace AILedger.Core.Contracts;

// Which way the verify command has to come out for the lesson to still hold. A verify is required
// to be runnable and was never required to be able to fail, so a grep for a symbol present in both
// the defective and the repaired state re-establishes nothing: it passes either way. The direction
// is what makes the check falsifiable.
public enum VerifyExpectation
{
    Present,
    Absent
}
