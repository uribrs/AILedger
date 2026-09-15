using AILedger.Core.Application;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Runs;

// Validates the manifest and cost measurements recorded when a run completes.
internal static class RunCompletionRecordRules
{
    // Mirrors TaskTransitionValidator.ValidateManifestRecord. Deliberately duplicated, not shared:
    // D13 holds that the two copies must agree, not that they must be one function.
    //
    // The pair is all-or-nothing because a hash with no count says a brief was handed over but not
    // how large it was, and a count with no hash cannot be matched to any manifest. Neither half
    // answers the question the fields exist for.
    internal const int ManifestHashLength = 64;

    internal static void EnsureManifestIsWellFormed(string? manifestHash, int? artifactCount)
    {
        var hash = TrimOrNull(manifestHash);
        if (hash is null && artifactCount is null)
        {
            return;
        }

        if (hash is null || artifactCount is null)
        {
            throw new GovernanceException(
                "A run's manifest hash and manifest artifact count must be recorded together.");
        }

        if (hash.Length != ManifestHashLength || !hash.All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new GovernanceException(
                $"A run's manifest hash must be {ManifestHashLength} lowercase hexadecimal characters.");
        }

        if (artifactCount < 0)
        {
            throw new GovernanceException("A run's manifest artifact count cannot be negative.");
        }
    }

    // Mirrors TaskTransitionValidator.ValidateCostRecord, on the same terms as the manifest pair.
    //
    // The six are independent, unlike the manifest pair: a run can report turns and output tokens
    // and still never reach the ledger, a provider that states no turn count reports none (D4), and
    // each absence means something on its own. So there is no all-or-nothing rule here, only the one
    // thing a count cannot be.
    internal static void EnsureCostIsWellFormed(
        int? turns,
        long? outputTokens,
        long? millisecondsToFirstLedgerWrite,
        long? tokensInUncached,
        long? tokensInCacheWrite,
        long? tokensInCacheRead)
    {
        if (turns < 0)
        {
            throw new GovernanceException("A run's turn count cannot be negative.");
        }

        if (outputTokens < 0)
        {
            throw new GovernanceException("A run's output token count cannot be negative.");
        }

        // Zero stays legal: a run that reached the ledger inside the first millisecond measured
        // zero, and that is a different fact from never having reached it, which is null.
        if (millisecondsToFirstLedgerWrite < 0)
        {
            throw new GovernanceException("A run's time to first ledger write cannot be negative.");
        }

        // The uncached bucket is the one a mapping can drive negative, because for codex it is a
        // subtraction (C6). None of the six checks here is relaxed for a provider that reports
        // nonsense: C15 is that the reader's accepted set has to be the subset, so RunCostReader
        // returns nothing rather than any value this rule refuses, and a negative arriving here is
        // a caller that did its own arithmetic. A direct or forged caller stays guarded.
        if (tokensInUncached < 0)
        {
            throw new GovernanceException("A run's uncached input token count cannot be negative.");
        }

        if (tokensInCacheWrite < 0)
        {
            throw new GovernanceException("A run's cache write input token count cannot be negative.");
        }

        if (tokensInCacheRead < 0)
        {
            throw new GovernanceException("A run's cache read input token count cannot be negative.");
        }
    }
}
