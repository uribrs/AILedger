using AILedger.Core.Contracts;

namespace AILedger.Core.Application;

/// <summary>
/// What a lesson's verify command means. It lives here rather than in the CLI because two places
/// have to agree about it: the command-time rule that refuses a fabricated verify, and the recheck
/// command that runs one and judges the result. Two copies of this would drift, and a drifted copy
/// would report a lesson as holding on a reading the mark rule never accepted.
/// </summary>
public static class LessonVerification
{
    /// <summary>
    /// Whether the verify is the admission that nothing can be run — "none" followed by a
    /// separator and a reason. Nothing can be re-established from it, so a recheck skips it
    /// instead of reporting a lesson as no longer holding because a non-command did not run.
    /// </summary>
    public static bool ClaimsNothingToRun(string? verify)
    {
        if (string.IsNullOrWhiteSpace(verify))
        {
            return false;
        }

        var trimmed = verify.Trim();
        return trimmed.StartsWith("none", StringComparison.OrdinalIgnoreCase) &&
            (trimmed.Length == 4 || !(char.IsLetterOrDigit(trimmed[4]) || trimmed[4] == '_'));
    }

    /// <summary>
    /// Whether a lesson still holds, given the direction it recorded and what running its verify
    /// found. "Present" is the command succeeding: a grep that matched, a path that exists, a test
    /// that passed. A lesson whose defect has since been repaired records Absent, so the same grep
    /// still resolving is the evidence that the lesson is stale.
    /// </summary>
    public static bool Holds(VerifyExpectation expects, bool present) =>
        expects == VerifyExpectation.Present ? present : !present;
}
