using System.Text;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
using AILedger.Providers.Process;
using AILedger.Tests.Support;

namespace AILedger.Tests.Providers;

// C1: one provider line above the 1 MiB per-line cap used to throw InvalidDataException out of the
// drain and end the run as ProtocolError, while a line the same kernel could not *parse* was
// tolerated and the run continued. Four runs across three tasks and both providers died that way,
// one of them at seven and a half minutes having written nothing to the ledger.
//
// D1: the line is cut to the cap, counted, and the run reaches its normal terminal state. These
// tests are the five attention items of the orchestration plan, one group each.
public sealed class OverlongLineTests
{
    private const int Cap = ProviderOutputLimits.MaximumCharactersPerLine;

    // R1: the truncation is counted. A cut stream reported as whole is a partial measurement
    // presented as complete, which is the defect a whole work item was spent repairing elsewhere.
    [Fact]
    public async Task AnOverlongLineIsCutToTheCapAndCountedInsteadOfEndingTheStream()
    {
        var lines = new List<string>();

        var truncated = await DrainAsync($"{new string('x', Cap + 500)}\n", lines);

        Assert.Equal(1, truncated);
        Assert.Single(lines);
        Assert.Equal(Cap, lines[0].Length);
        Assert.Equal(new string('x', Cap), lines[0]);
    }

    // R1: a stream nothing had to cut reports zero, not null. Zero is the measurement that makes a
    // nonzero count readable; if an unwatched stream and a clean one both read as absent, the count
    // says nothing.
    [Fact]
    public async Task AStreamWithNoOverlongLineCountsZero()
    {
        var lines = new List<string>();

        var truncated = await DrainAsync("{\"type\":\"system\"}\n{\"type\":\"result\"}\n", lines);

        Assert.Equal(0, truncated);
        Assert.Equal(new[] { "{\"type\":\"system\"}", "{\"type\":\"result\"}" }, lines);
    }

    // R3: the whole point of the change is that the lines before and after an oversized one stay
    // trustworthy. Before D1 the first of these two events was the last thing the run ever saw.
    [Fact]
    public async Task EventsAfterAnOverlongLineAreStillRead()
    {
        var lines = new List<string>();
        var stream =
            "{\"type\":\"system\",\"session_id\":\"session-1\"}\n" +
            $"{new string('y', Cap * 2)}\n" +
            "{\"type\":\"result\",\"session_id\":\"session-1\"}\n";

        var truncated = await DrainAsync(stream, lines);

        Assert.Equal(1, truncated);
        Assert.Equal(3, lines.Count);
        Assert.Equal("{\"type\":\"system\",\"session_id\":\"session-1\"}", lines[0]);
        Assert.Equal("{\"type\":\"result\",\"session_id\":\"session-1\"}", lines[2]);
    }

    // R3, and the half of it that is easy to get wrong: reading has to resume at the next newline
    // and never mid-line. A drain that carried on appending after the cap would hand the receiver
    // the tail of an oversized line as if it were a record of its own, which is worse than the
    // truncation it came from.
    [Fact]
    public async Task TheRemainderOfAnOverlongLineIsDiscardedRatherThanEmittedAsItsOwnRecord()
    {
        var lines = new List<string>();
        var overlong = new string('z', Cap) + "TAIL-THAT-MUST-NOT-BE-A-RECORD";

        var truncated = await DrainAsync($"{overlong}\n{{\"type\":\"result\"}}\n", lines);

        Assert.Equal(1, truncated);
        Assert.Equal(2, lines.Count);
        Assert.DoesNotContain(lines, line => line.Contains("TAIL-THAT-MUST-NOT-BE-A-RECORD", StringComparison.Ordinal));
        Assert.Equal("{\"type\":\"result\"}", lines[1]);
    }

    // R1: every cut line is counted, not just the first. A count that saturated at one would still
    // let a reader know the stream was degraded but not how badly.
    [Fact]
    public async Task EveryOverlongLineIsCounted()
    {
        var lines = new List<string>();
        var overlong = new string('x', Cap + 1);

        var truncated = await DrainAsync($"{overlong}\nshort\n{overlong}\n", lines);

        Assert.Equal(2, truncated);
        Assert.Equal(3, lines.Count);
        Assert.Equal("short", lines[1]);
    }

    // An overlong final line with no trailing newline is still emitted and still counted. The drain
    // flushes what it holds at end of stream, and that path is separate from the newline path.
    [Fact]
    public async Task AnOverlongFinalLineWithNoNewlineIsStillEmittedAndCounted()
    {
        var lines = new List<string>();

        var truncated = await DrainAsync(new string('x', Cap + 10), lines);

        Assert.Equal(1, truncated);
        Assert.Single(lines);
        Assert.Equal(Cap, lines[0].Length);
    }

    // R5, and the limit of this whole change: a stream this kernel cannot follow at all is still a
    // real protocol failure and must still terminate the run. "No newline in megabytes" is exactly
    // that, and it now dies at the per-stream cap rather than the per-line one — the producer is cut
    // at the first megabyte, kept being read, and refused when the whole stream crosses 8 MiB.
    [Fact]
    public async Task AProducerThatNeverEmitsANewlineStillEndsTheStream()
    {
        var lines = new List<string>();
        var endless = new string('x', ProviderOutputLimits.MaximumCharactersPerStream + 16);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => DrainAsync(endless, lines));

        Assert.Contains("per-stream limit", exception.Message, StringComparison.Ordinal);
    }

    // R1 across the boundary: the count is useless inside the drain. It has to leave the adapter on
    // AgentRunResult, which is the only thing the launcher sees, and the run has to end normally.
    [Fact]
    public async Task TheAdapterCarriesTheTruncationCountOntoACompletedResult()
    {
        var result = await RunClaudeAsync(truncatedLines: 3);

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Null(result.Failure);
        Assert.Equal(3, result.TruncatedLines);
    }

    // The zero case over the same path, so the field is not merely non-null when something was cut.
    [Fact]
    public async Task TheAdapterReportsZeroForAStreamNothingWasCutFrom()
    {
        var result = await RunClaudeAsync(truncatedLines: 0);

        Assert.Equal(AgentRunStatus.Completed, result.Status);
        Assert.Equal(0, result.TruncatedLines);
    }

    // R2: the cut line may be the provider's own terminal event, and then cost must read as
    // unmeasured rather than as whatever numbers survived the cut. This holds by construction and
    // the test is here to keep it holding: ParseClaude opens the line with JsonDocument.Parse, a cut
    // line is not valid JSON, so the event never enters the list and RunCostReader finds no terminal
    // event to read. Partial numbers would be worse than none — they would look measured.
    [Fact]
    public async Task ATruncatedTerminalEventMeasuresNothingRatherThanPartialCost()
    {
        // A claude result event cut mid-usage: the turn count and one token bucket are intact in the
        // text, which is exactly what a lenient reader would happily believe.
        var cutTerminal =
            "{\"type\":\"result\",\"session_id\":\"session-1\",\"num_turns\":125," +
            "\"usage\":{\"output_tokens\":33110,\"cache_read_input_tok";
        var runner = ClaudeRunner(invocation => new ScriptedProcessResult(
            0,
            [$"{{\"type\":\"system\",\"session_id\":\"{ProviderProtocolTests.ValueAfter(invocation.Arguments, "--session-id")}\"}}",
             cutTerminal],
            [],
            TruncatedLines: 1));

        var result = await new ClaudeAgentAdapter(runner).RunAsync(
            ProviderProtocolTests.Request("claude", AgentLaunchMode.New, null), CancellationToken.None);
        var cost = RunCostReader.Read(result.Events);

        Assert.Equal(AgentRunStatus.ProtocolError, result.Status);
        Assert.Contains("Malformed provider JSONL", result.Failure, StringComparison.Ordinal);
        Assert.Equal(RunCost.Unmeasured, cost);
        // The count still travels, and it is the only thing that says why the cost is missing.
        Assert.Equal(1, result.TruncatedLines);
    }

    // CC1: the cap bounds a line's content, and a CRLF terminator is not content. Appending the
    // carriage return before testing the cap made a line of exactly the cap length cross it on its
    // own terminator — the content was emitted whole and the count said it had been cut, which is a
    // degraded measurement reported for a stream nothing happened to.
    [Fact]
    public async Task ACarriageReturnTerminatedLineOfExactlyTheCapIsWholeAndCountedZero()
    {
        var lines = new List<string>();

        var truncated = await DrainAsync($"{new string('x', Cap)}\r\n{{\"type\":\"result\"}}\r\n", lines);

        Assert.Equal(0, truncated);
        Assert.Equal(2, lines.Count);
        Assert.Equal(Cap, lines[0].Length);
        Assert.Equal("{\"type\":\"result\"}", lines[1]);
    }

    // The same boundary with the other terminator, which was never wrong and has to stay right.
    [Fact]
    public async Task ALineOfExactlyTheCapIsWholeAndCountedZero()
    {
        var lines = new List<string>();

        var truncated = await DrainAsync($"{new string('x', Cap)}\n", lines);

        Assert.Equal(0, truncated);
        Assert.Single(lines);
        Assert.Equal(Cap, lines[0].Length);
    }

    // One character of content above the cap, terminated by CRLF, is still cut and still counted.
    // The repair moves where the terminator is accounted for; it does not relax the cap.
    [Fact]
    public async Task ACarriageReturnTerminatedLineAboveTheCapIsStillCutAndCounted()
    {
        var lines = new List<string>();

        var truncated = await DrainAsync($"{new string('x', Cap + 1)}\r\n", lines);

        Assert.Equal(1, truncated);
        Assert.Single(lines);
        Assert.Equal(Cap, lines[0].Length);
    }

    // A carriage return no newline follows is content. Holding it back is how the terminator stops
    // spending the budget; dropping it would corrupt a line that legitimately contains one.
    [Fact]
    public async Task ACarriageReturnInsideALineIsKeptAsContent()
    {
        var lines = new List<string>();

        var truncated = await DrainAsync("before\rafter\nsecond\n", lines);

        Assert.Equal(0, truncated);
        Assert.Equal(new[] { "before\rafter", "second" }, lines);
    }

    // The held carriage return can be the character that crosses the cap, and then the line is cut
    // at the cap and its remainder discarded like any other overlong line — reading resumes at the
    // next newline and never mid-line (R3).
    [Fact]
    public async Task AHeldCarriageReturnThatCrossesTheCapCutsTheLineAndDiscardsTheRest()
    {
        var lines = new List<string>();

        var truncated = await DrainAsync($"{new string('x', Cap)}\rTAIL\n{{\"type\":\"result\"}}\n", lines);

        Assert.Equal(1, truncated);
        Assert.Equal(2, lines.Count);
        Assert.Equal(new string('x', Cap), lines[0]);
        Assert.Equal("{\"type\":\"result\"}", lines[1]);
    }

    // CC2: the count is worth the most on the run that was refused, and that is the one run whose
    // drain never returns — the receiver's exception faults the drain task before its return value
    // exists. The tally is where the count is left instead, and this drives the real drain.
    [Fact]
    public async Task TheTallyHoldsWhatWasCutWhenAReceiverEndsTheDrain()
    {
        var tally = new TruncatedLineTally();
        var delivered = 0;
        var overlong = new string('x', Cap + 1);

        await Assert.ThrowsAsync<InvalidDataException>(() => DrainAsync(
            $"{overlong}\n{overlong}\n{overlong}\n",
            (_, _) => ++delivered == 3
                ? throw new InvalidDataException("Provider output exceeded the retained-output limit.")
                : ValueTask.CompletedTask,
            tally));

        Assert.Equal(3, delivered);
        Assert.Equal(3, tally.Observed);
    }

    // Null, not zero, when nothing was cut. On a failure path the count has to say "nobody counted":
    // a run refused part-way through never finished watching the stream, so it cannot claim a clean
    // one. Zero is reserved for a drain that ran to the end.
    [Fact]
    public async Task ATallyNothingWasCutFromReportsNoCountRatherThanZero()
    {
        var tally = new TruncatedLineTally();

        var truncated = await DrainAsync(
            "{\"type\":\"result\"}\n",
            static (_, _) => ValueTask.CompletedTask,
            tally);

        Assert.Equal(0, truncated);
        Assert.Null(tally.Observed);
    }

    // CC2 across the boundary the whole change stops at, driven through the production drain. Eight
    // cut lines cross the 8 MiB retention budget, because each is charged the cap plus a newline,
    // and the run still ends as a protocol failure — BC1's honest limit. What must not happen again
    // is the record of the run that was cut the most saying nothing was cut.
    //
    // Four lines per pipe, because that is the only shape in which this fault is reachable at all:
    // the per-stream cap is also 8 MiB and is counted per pipe over every character read, including
    // the discarded remainders, so eight cut lines down one pipe end the run at the per-stream cap
    // first. Retention is the shared budget, and it is crossed by output split across both (DC6).
    [Fact]
    public async Task AProtocolErrorFromTheRetainedOutputCapStillReportsWhatWasCut()
    {
        var overlong = new string('x', Cap + 1);
        var runner = ClaudeRunner(_ => new ScriptedProcessResult(
            0,
            Enumerable.Repeat(overlong, 4).ToArray(),
            Enumerable.Repeat(overlong, 4).ToArray(),
            DrainRealStreams: true));

        var result = await new ClaudeAgentAdapter(runner).RunAsync(
            ProviderProtocolTests.Request("claude", AgentLaunchMode.New, null), CancellationToken.None);

        Assert.Equal(AgentRunStatus.ProtocolError, result.Status);
        Assert.Contains("retained-output limit", result.Failure, StringComparison.Ordinal);
        Assert.Equal(8, result.TruncatedLines);
    }

    // The three failure paths in the adapter never receive a ProcessExit, so they read the count off
    // the tally. Null there is honest — nobody counted — and must not be filled in as zero.
    [Fact]
    public async Task ACancelledRunReportsNoCountRatherThanZero()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = ClaudeRunner(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        var result = await new ClaudeAgentAdapter(runner).RunAsync(
            ProviderProtocolTests.Request("claude", AgentLaunchMode.New, null), cancellation.Token);

        Assert.Equal(AgentRunStatus.Cancelled, result.Status);
        Assert.Null(result.TruncatedLines);
    }

    private static Task<int> DrainAsync(string payload, ICollection<string> lines) =>
        DrainAsync(
            payload,
            (line, _) =>
            {
                lines.Add(line);
                return ValueTask.CompletedTask;
            },
            null);

    private static async Task<int> DrainAsync(
        string payload,
        Func<string, CancellationToken, ValueTask> receiver,
        TruncatedLineTally? tally)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        using var reader = new StreamReader(stream);
        return await SystemProcessRunner.DrainAsync(reader, receiver, tally, CancellationToken.None);
    }

    private static async Task<AgentRunResult> RunClaudeAsync(int? truncatedLines)
    {
        var runner = ClaudeRunner(invocation =>
        {
            var session = ProviderProtocolTests.ValueAfter(invocation.Arguments, "--session-id");
            return new ScriptedProcessResult(
                0,
                [$"{{\"type\":\"system\",\"session_id\":\"{session}\"}}",
                 $"{{\"type\":\"result\",\"session_id\":\"{session}\",\"result\":\"done\"}}"],
                [],
                truncatedLines);
        });

        return await new ClaudeAgentAdapter(runner).RunAsync(
            ProviderProtocolTests.Request("claude", AgentLaunchMode.New, null), CancellationToken.None);
    }

    private static ScriptedProcessRunner ClaudeRunner(Func<ProcessInvocation, ScriptedProcessResult> run) =>
        new ScriptedProcessRunner()
            .Enqueue(0, ["2.0.0 (Claude Code)"])
            .Enqueue(0, ["--print --output-format --session-id --resume --permission-prompts --settings --strict-mcp-config --disable-slash-commands"])
            .Enqueue(run);
}
