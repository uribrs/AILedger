using AILedger.Core.Application;
using AILedger.Core.Contracts;

namespace AILedger.Tests.Core;

// D4: what a run cost is read off the stream the provider already emits, not instrumented. The
// fixtures below are the shapes E4 and E5 captured from real runs on 2026-09-08, and IE2 confirmed
// the codex half a second time against the codex binary's own serde field table.
//
// Nothing here asserts that a field is null on a stream that never carried it. That shape passes
// with every read path dead, which is the failure kernel-version-stamp:LC20 records four times over.
// Every absence asserted below is paired with a positive read of the same field.
public sealed class RunCostReaderTests
{
    // A claude result event, trimmed to the properties the reader looks at. num_turns 125 and the
    // usage object are R13's, from E4 and E5. Internal rather than private because
    // RunCostRecordTests drives the same fixture through the completion command, and E4's numbers
    // should exist once rather than in two copies that can drift apart.
    internal const string ClaudeResult =
        """
        {"type":"result","subtype":"success","is_error":false,"num_turns":125,"duration_ms":1093960,
         "session_id":"session-1","usage":{"input_tokens":2,"cache_creation_input_tokens":41368,
         "cache_read_input_tokens":16285,"output_tokens":33110}}
        """;

    // A codex turn.completed event. The usage numbers are R8's, from E4 and E5;
    // reasoning_output_tokens is on the event per IE2 and the reader deliberately ignores it.
    private const string CodexTurnCompleted =
        """
        {"type":"turn.completed","usage":{"input_tokens":8796519,"cached_input_tokens":8639744,
         "cache_write_input_tokens":151552,"output_tokens":28399,"reasoning_output_tokens":21376}}
        """;

    // Codex's input_tokens contains its cached_input_tokens (C6), so the uncached bucket is the
    // difference. Spelled out here rather than written as the subtraction, so that the test states
    // the number it expects instead of repeating the code under test.
    private const long CodexUncached = 156775;

    [Fact]
    public void AClaudeStreamRecordsTheTurnCountTheProviderStates()
    {
        var cost = RunCostReader.Read(ClaudeStream());

        Assert.Equal(125, cost.Turns);
    }

    // D4: Turns holds a provider's own reported turn count and nothing else, so codex records none.
    // The absence is the measurement, on the same terms as ManifestHash on an unbriefed run.
    //
    // Never derived by counting turn.completed: AgentAdapterBase refuses any stream carrying more
    // than one terminal event and turn.completed is codex's, so that count is one for every codex
    // run that finished, against claude's 125 above. Ranking those two against each other is the
    // two-populations defect ALT1 and ALT3 were rejected for. IC1 carries the evidence.
    [Fact]
    public void ACodexStreamRecordsNoTurnCountBecauseTheProviderStatesNone()
    {
        var codex = RunCostReader.Read(CodexStream());

        Assert.Null(codex.Turns);
        // The absence above means something only because the same field reads a number from the
        // stream beside it, and specifically not the 1 that counting terminal events would give.
        Assert.Equal(125, RunCostReader.Read(ClaudeStream()).Turns);
        Assert.NotEqual(1, RunCostReader.Read(ClaudeStream()).Turns);
    }

    [Fact]
    public void AClaudeStreamRecordsTheOutputTokensFromItsUsageObject()
    {
        var cost = RunCostReader.Read(ClaudeStream());

        Assert.Equal(33110, cost.OutputTokens);
    }

    [Fact]
    public void ACodexStreamRecordsTheOutputTokensFromItsUsageObject()
    {
        var cost = RunCostReader.Read(CodexStream());

        Assert.Equal(28399, cost.OutputTokens);
    }

    // C9: codex's reasoning_output_tokens is a subset breakdown of output_tokens, not an addend, and
    // both providers already count every generated token in output_tokens and bill it at the output
    // rate. Adding the two is the double count langfuse shipped (E13, E14).
    [Fact]
    public void ACodexStreamDoesNotFoldReasoningTokensIntoTheOutputCount()
    {
        var cost = RunCostReader.Read(CodexStream());

        Assert.Equal(28399, cost.OutputTokens);
        Assert.NotEqual(28399 + 21376, cost.OutputTokens);
    }

    // C6, the claude half: input_tokens counts uncached tokens only, with both cache figures
    // additive on top, so all three buckets are read straight off the usage object.
    [Fact]
    public void AClaudeStreamRecordsTheThreeInputBucketsAsAnthropicReportsThem()
    {
        var cost = RunCostReader.Read(ClaudeStream());

        Assert.Equal(2, cost.TokensInUncached);
        Assert.Equal(41368, cost.TokensInCacheWrite);
        Assert.Equal(16285, cost.TokensInCacheRead);
    }

    // The inverse of the codex mistake below, and the reason the mapping is per-provider rather than
    // one rule applied twice. Subtracting claude's cache reads out of its input_tokens would take
    // 16285 off a total that never contained them — here it would go negative.
    [Fact]
    public void AClaudeStreamDoesNotSubtractItsCacheFiguresOutOfTheUncachedBucket()
    {
        var cost = RunCostReader.Read(ClaudeStream());

        Assert.Equal(2, cost.TokensInUncached);
        Assert.True(cost.TokensInUncached > 0);
    }

    // C6, the codex half: input_tokens is every input token with cached_input_tokens already inside
    // it, so the uncached bucket is the difference and the cache read bucket is cached_input_tokens
    // itself.
    [Fact]
    public void ACodexStreamRecordsTheThreeInputBucketsWithItsCachedReadsSubtractedOut()
    {
        var cost = RunCostReader.Read(CodexStream());

        Assert.Equal(CodexUncached, cost.TokensInUncached);
        Assert.Equal(151552, cost.TokensInCacheWrite);
        Assert.Equal(8639744, cost.TokensInCacheRead);
    }

    // The defect promptfoo shipped: leaving cached_input_tokens inside the uncached bucket bills
    // 8639744 cache reads at the uncached rate, which C7 measured at roughly ten times the cache
    // read rate. The wrong number here is 56 times the right one.
    [Fact]
    public void ACodexStreamDoesNotLeaveItsCachedReadsInsideTheUncachedBucket()
    {
        var cost = RunCostReader.Read(CodexStream());

        Assert.NotEqual(8796519, cost.TokensInUncached);
        Assert.Equal(CodexUncached, cost.TokensInUncached);
    }

    // A codex turn with nothing served from cache. The subtraction has nothing to take, so the whole
    // input total is uncached — not null, which would lose a real measurement.
    [Fact]
    public void ACodexStreamThatCachedNothingRecordsItsWholeInputTotalAsUncached()
    {
        var cost = RunCostReader.Read([
            new ProviderEvent(
                1, "turn.completed",
                """{"type":"turn.completed","usage":{"input_tokens":4096,"output_tokens":12}}""",
                null, true, false)
        ]);

        Assert.Equal(4096, cost.TokensInUncached);
        Assert.Null(cost.TokensInCacheRead);
        Assert.Equal(12, cost.OutputTokens);
    }

    // A usage object whose cached count exceeds the total it is reported inside describes no run
    // this kernel can measure. It records no uncached bucket rather than a negative one, which the
    // completion rule would refuse on the way in — and the two buckets it can still read, it reads.
    [Fact]
    public void ACodexUsageWhoseCachedCountExceedsItsInputTotalRecordsNoUncachedBucket()
    {
        var cost = RunCostReader.Read([
            new ProviderEvent(
                1, "turn.completed",
                """
                {"type":"turn.completed","usage":{"input_tokens":100,"cached_input_tokens":500,
                 "cache_write_input_tokens":7,"output_tokens":9}}
                """,
                null, true, false)
        ]);

        Assert.Null(cost.TokensInUncached);
        Assert.Equal(7, cost.TokensInCacheWrite);
        Assert.Equal(500, cost.TokensInCacheRead);
        Assert.Equal(9, cost.OutputTokens);
    }

    // RC3, the third symptom of C15: a count the provider states as negative was read straight
    // through, and the completion rule then refused it inside the launcher's close path, so
    // malformed optional telemetry stranded a finished run. It is not a measurement, and the reader
    // is where it stops.
    //
    // One row per field, because each of the five is read by a different path — the turn count off
    // the root, the other four off the usage object, one of them through the subtraction. Every row
    // reads the four siblings back at E4's real values, so no row can pass with the read path dead
    // and no row can pass by the whole event failing to parse.
    [Theory]
    [InlineData("num_turns", null, 33110L, 2L, 41368L, 16285L)]
    [InlineData("output_tokens", 125, null, 2L, 41368L, 16285L)]
    [InlineData("input_tokens", 125, 33110L, null, 41368L, 16285L)]
    [InlineData("cache_creation_input_tokens", 125, 33110L, 2L, null, 16285L)]
    [InlineData("cache_read_input_tokens", 125, 33110L, 2L, 41368L, null)]
    public void ANegativeCountIsNotAMeasurementAndItsSiblingsStillAre(
        string negated,
        int? turns,
        long? outputTokens,
        long? uncached,
        long? cacheWrite,
        long? cacheRead)
    {
        var cost = RunCostReader.Read([
            new ProviderEvent(1, "result", ClaudeResultWithNegative(negated), "s", true, false)
        ]);

        Assert.Equal(turns, cost.Turns);
        Assert.Equal(outputTokens, cost.OutputTokens);
        Assert.Equal(uncached, cost.TokensInUncached);
        Assert.Equal(cacheWrite, cost.TokensInCacheWrite);
        Assert.Equal(cacheRead, cost.TokensInCacheRead);
    }

    // The codex half, and the case the guard above creates rather than closes. cached_input_tokens
    // is refused, so how much of input_tokens was served from cache is unknown — and the whole
    // total must not then be recorded as uncached, which would bill 8639744 cache reads at the
    // uncached rate, the promptfoo defect in E10 reached through a guard instead of past one. The
    // two buckets the event still states are still read, and the test above this one shows a codex
    // turn that states no cache figure at all does record its whole total, so the distinction here
    // is between an unknown subtraction and an absent one.
    [Fact]
    public void ACodexUsageWhoseCachedCountIsNegativeRecordsNoUncachedBucketEither()
    {
        var cost = RunCostReader.Read([
            new ProviderEvent(
                1, "turn.completed",
                """
                {"type":"turn.completed","usage":{"input_tokens":8796519,"cached_input_tokens":-1,
                 "cache_write_input_tokens":151552,"output_tokens":28399}}
                """,
                null, true, false)
        ]);

        Assert.Null(cost.TokensInCacheRead);
        Assert.Null(cost.TokensInUncached);
        Assert.NotEqual(8796519, cost.TokensInUncached);
        Assert.Equal(151552, cost.TokensInCacheWrite);
        Assert.Equal(28399, cost.OutputTokens);
    }

    // Zero is a measurement and stays one, on both sides of the split: a turn that generated no
    // output tokens reported zero of them, which is a different fact from a provider reporting
    // none. The non-negative guard must refuse below zero and not at it.
    [Fact]
    public void ACountTheProviderStatesAsZeroIsAMeasurementAndNotAnAbsence()
    {
        var cost = RunCostReader.Read([
            new ProviderEvent(
                1, "result",
                """
                {"type":"result","num_turns":0,
                 "usage":{"input_tokens":0,"cache_creation_input_tokens":0,
                 "cache_read_input_tokens":0,"output_tokens":0}}
                """,
                "s", true, false)
        ]);

        Assert.Equal(0, cost.Turns);
        Assert.Equal(0, cost.OutputTokens);
        Assert.Equal(0, cost.TokensInUncached);
        Assert.Equal(0, cost.TokensInCacheWrite);
        Assert.Equal(0, cost.TokensInCacheRead);
        Assert.NotEqual(RunCost.Unmeasured, cost);
    }

    // The observability hole this work exists to close: a run that died mid-flight. It has no
    // terminal event, so it measured nothing, and nothing must read as zero.
    [Fact]
    public void AStreamThatNeverReachedATerminalEventMeasuredNothing()
    {
        var cost = RunCostReader.Read([
            new ProviderEvent(1, "thread.started", """{"type":"thread.started","thread_id":"t"}""", "t", false, false)
        ]);

        Assert.Equal(RunCost.Unmeasured, cost);
    }

    [Fact]
    public void AnEmptyStreamMeasuredNothing()
    {
        Assert.Equal(RunCost.Unmeasured, RunCostReader.Read([]));
    }

    [Fact]
    public void NothingMeasuredIsFiveAbsencesAndNotFiveZeroes()
    {
        Assert.Null(RunCost.Unmeasured.Turns);
        Assert.Null(RunCost.Unmeasured.OutputTokens);
        Assert.Null(RunCost.Unmeasured.TokensInUncached);
        Assert.Null(RunCost.Unmeasured.TokensInCacheWrite);
        Assert.Null(RunCost.Unmeasured.TokensInCacheRead);
    }

    // A terminal event whose body is not the shape the reader expects yields no measurement rather
    // than a wrong one. The raw text survives beside the log either way.
    [Fact]
    public void ATerminalEventMissingItsUsageObjectMeasuresOnlyWhatItStates()
    {
        var cost = RunCostReader.Read([
            new ProviderEvent(1, "result", """{"type":"result","num_turns":7}""", "s", true, false)
        ]);

        Assert.Equal(7, cost.Turns);
        Assert.Null(cost.OutputTokens);
        Assert.Null(cost.TokensInUncached);
        Assert.Null(cost.TokensInCacheWrite);
        Assert.Null(cost.TokensInCacheRead);
    }

    [Fact]
    public void AMalformedTerminalEventIsNotAMeasurement()
    {
        var cost = RunCostReader.Read([
            new ProviderEvent(1, "result", "{not json", "s", true, false)
        ]);

        Assert.Equal(RunCost.Unmeasured, cost);
    }

    // RC1: a terminal event body is a JSON object by the provider's convention, not by guarantee,
    // and JsonElement.TryGetProperty throws rather than returning false when asked of anything else.
    // Valid JSON that is not an object therefore used to escape the reader as an
    // InvalidOperationException, past the JsonException catch, and abort the completion path over
    // optional telemetry. Both provider paths are driven, because the claude one reads a property off
    // the root and the codex one only off the usage object.
    [Theory]
    [InlineData("result", "[]")]
    [InlineData("result", "null")]
    [InlineData("result", "\"result\"")]
    [InlineData("turn.completed", "[]")]
    [InlineData("turn.completed", "null")]
    [InlineData("turn.completed", "\"turn.completed\"")]
    public void ATerminalEventThatIsValidJsonButNotAnObjectIsNotAMeasurement(string type, string rawJson)
    {
        var cost = RunCostReader.Read([new ProviderEvent(1, type, rawJson, "s", true, false)]);

        Assert.Equal(RunCost.Unmeasured, cost);
    }

    // The same check one level in: a usage property the provider filled with something other than an
    // object. The turn count beside it is still read, so this is a degraded measurement rather than
    // no measurement, and the assertion on it is what shows the read path is live.
    [Fact]
    public void ATerminalEventWhoseUsagePropertyIsNotAnObjectMeasuresOnlyWhatSitsBesideIt()
    {
        var cost = RunCostReader.Read([
            new ProviderEvent(1, "result", """{"type":"result","num_turns":7,"usage":[]}""", "s", true, false)
        ]);

        Assert.Equal(7, cost.Turns);
        Assert.Null(cost.OutputTokens);
        Assert.Null(cost.TokensInUncached);
        Assert.Null(cost.TokensInCacheWrite);
        Assert.Null(cost.TokensInCacheRead);
    }

    // RC2: the token counters are 64-bit, and this is the test that pins it. Every number below is
    // above int.MaxValue, so a 32-bit read returns null for all four — and the runs that cross that
    // limit are the expensive ones, which is exactly where a silent absence biases the record down.
    // The codex path is used because its uncached bucket is a subtraction, so it pins the arithmetic
    // at 64 bits too and not only the parse.
    [Fact]
    public void TokenCountsAboveTheThirtyTwoBitLimitAreRecordedRatherThanLost()
    {
        var cost = RunCostReader.Read([
            new ProviderEvent(
                1, "turn.completed",
                """
                {"type":"turn.completed","usage":{"input_tokens":4294967296,
                 "cached_input_tokens":2147483648,"cache_write_input_tokens":2147483649,
                 "output_tokens":3000000000}}
                """,
                null, true, false)
        ]);

        Assert.Equal(3000000000L, cost.OutputTokens);
        Assert.Equal(2147483648L, cost.TokensInUncached);
        Assert.Equal(2147483649L, cost.TokensInCacheWrite);
        Assert.Equal(2147483648L, cost.TokensInCacheRead);
    }

    // The other side of the split K11 draws: a turn count stays 32-bit, because it is a provider's
    // own iteration count and nothing aggregates it. A value outside that range records nothing
    // rather than a truncated number, on the same terms as every other absence here — and the token
    // count on the same event, above the same limit, is still read.
    [Fact]
    public void ATurnCountOutsideTheThirtyTwoBitRangeRecordsNothingRatherThanATruncatedOne()
    {
        var cost = RunCostReader.Read([
            new ProviderEvent(
                1, "result",
                """
                {"type":"result","num_turns":2147483648,
                 "usage":{"input_tokens":4294967296,"output_tokens":9}}
                """,
                "s", true, false)
        ]);

        Assert.Null(cost.Turns);
        Assert.Equal(4294967296L, cost.TokensInUncached);
    }

    // A provider this kernel does not know reports nothing rather than something guessed. Its usage
    // object is shaped like one the reader could parse, and it is still not read: which properties
    // mean what is a per-provider fact (C6), so an unrecognised provider has no mapping.
    [Fact]
    public void AnUnknownTerminalEventTypeIsNotAMeasurement()
    {
        var cost = RunCostReader.Read([
            new ProviderEvent(
                1, "some.other.ending",
                """{"usage":{"input_tokens":11,"cached_input_tokens":3,"output_tokens":9}}""",
                "s", true, false)
        ]);

        Assert.Equal(RunCost.Unmeasured, cost);
    }

    // The five numbers ClaudeResult states, addressable by property name, so the negative theory
    // can replace exactly one of them and leave the rest as E4 captured them. A name that does not
    // match a stated number throws here rather than yielding an event with nothing negative in it,
    // which would make the row assert against the good values and read as a code failure.
    private static readonly (string Property, long Value)[] ClaudeReported =
    [
        ("num_turns", 125), ("input_tokens", 2), ("cache_creation_input_tokens", 41368),
        ("cache_read_input_tokens", 16285), ("output_tokens", 33110)
    ];

    private static string ClaudeResultWithNegative(string property)
    {
        var stated = ClaudeReported.Single(reported => reported.Property == property);
        return ClaudeResult.Replace(
            $"\"{property}\":{stated.Value}", $"\"{property}\":-{stated.Value}", StringComparison.Ordinal);
    }

    private static IReadOnlyList<ProviderEvent> ClaudeStream() =>
    [
        new(1, "system", """{"type":"system","session_id":"session-1"}""", "session-1", false, false),
        new(2, "assistant", """{"type":"assistant","session_id":"session-1"}""", "session-1", false, false),
        new(3, "result", ClaudeResult, "session-1", true, false)
    ];

    private static IReadOnlyList<ProviderEvent> CodexStream() =>
    [
        new(1, "thread.started", """{"type":"thread.started","thread_id":"session-2"}""", "session-2", false, false),
        new(2, "turn.started", """{"type":"turn.started"}""", null, false, false),
        new(3, "item.completed", """{"type":"item.completed","item":{"item_type":"agent_message"}}""", null, false, false),
        new(4, "turn.completed", CodexTurnCompleted, null, true, false)
    ];
}
