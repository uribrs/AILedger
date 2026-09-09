using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Providers.Adapters;
using AILedger.Storage;
using AILedger.Tests.Core;
using AILedger.Tests.Support;
using System.Text.Json;

namespace AILedger.Tests.Cli;

// W1 put the six cost fields on run.completed and RunCostReader beside them, and nothing populated
// either: every real run still recorded null, which is IC4 and attention item R14. These tests are
// the other half — the launcher — and they run through the production launch path, because that is
// the only place the fields are filled. RunCostReaderTests pins what a stream says; these pin what
// the ledger ends up holding.
[Collection(StandardInput.Collection)]
public sealed class RunCostLaunchTests
{
    // A run id the current alphabet refuses and an older CLI accepted, which is what makes it the
    // subject of RC4. It is the space that does it: the shell-safety guard exists because this value
    // is interpolated into the child's command line, and a run recorded before that guard existed
    // was never going to be launched again — only completed, and reviewed.
    private const string LegacyRunId = "R 1";

    // E4 and E5's real codex numbers, the same fixture RunCostReaderTests reads, so the two files
    // cannot drift about what a provider said.
    private const string CodexTurnCompleted =
        """
        {"type":"turn.completed","usage":{"input_tokens":8796519,"cached_input_tokens":8639744,
         "cache_write_input_tokens":151552,"output_tokens":28399,"reasoning_output_tokens":21376}}
        """;

    private const string CodexThreadStarted = """{"type":"thread.started","thread_id":"session-1"}""";

    [Fact]
    public async Task ALaunchRecordsWhatTheProviderStatedTheRunCost()
    {
        using var root = new TemporaryDirectory();
        var adapter = ClaudeAdapter(RunCostReaderTests.ClaudeResult);

        var run = await LaunchAsync(root.Path, adapter);

        // E4's numbers, from the same claude result event RunCostReaderTests reads. Asserted on the
        // replayed run rather than on the reader: the defect this item repairs is a populated reader
        // whose numbers never reached the record.
        Assert.Equal(125, run.Turns);
        Assert.Equal(33110, run.OutputTokens);
        Assert.Equal(2, run.TokensInUncached);
        Assert.Equal(41368, run.TokensInCacheWrite);
        Assert.Equal(16285, run.TokensInCacheRead);
    }

    // D4: codex states no turn count, and that absence is the measurement. It means something here
    // only because the same launch records the numbers codex does state.
    [Fact]
    public async Task ACodexLaunchRecordsItsTokensAndNoTurnCount()
    {
        using var root = new TemporaryDirectory();
        var adapter = CodexAdapter(CodexThreadStarted, CodexTurnCompleted);

        var run = await LaunchAsync(root.Path, adapter);

        Assert.Null(run.Turns);
        Assert.Equal(28399, run.OutputTokens);
        // input_tokens minus cached_input_tokens (C6), spelled out rather than computed here.
        Assert.Equal(156775, run.TokensInUncached);
        Assert.Equal(151552, run.TokensInCacheWrite);
        Assert.Equal(8639744, run.TokensInCacheRead);
    }

    // C15's class, driven through the production launch rather than through the completion command:
    // every value the reader declines has to leave the run closed. Read is called inside the path
    // that closes the run, so a value the completion rule refuses would throw there and leave a
    // finished run active — which blocks the next run on its work item and blocks Archive.
    //
    // The assertion is the invariant itself rather than a per-shape expected value, because the
    // shapes kept arriving one at a time: a non-object payload, an inverted subtraction, a reported
    // negative, and a refused cache count read as an unreported one, the last found inside the
    // repair for the third (C16). What every one of them has to satisfy is that the run closed and
    // nothing the rule refuses reached the record. It passes vacuously against a dead read path, so
    // it is paired with the two tests above, which read real numbers off real streams.
    [Theory]
    // A directly reported negative, which the completion rule refuses by name (RC3).
    [InlineData("""{"type":"turn.completed","usage":{"output_tokens":-1,"input_tokens":10}}""")]
    // A cached count larger than the total it is reported inside, so the subtraction inverts (IC6).
    [InlineData("""{"type":"turn.completed","usage":{"input_tokens":10,"cached_input_tokens":560}}""")]
    // A cache figure this reader refused, leaving the size of the subtraction unknown (IC11).
    [InlineData("""{"type":"turn.completed","usage":{"input_tokens":10,"cached_input_tokens":-5}}""")]
    // A number outside 64 bits, which cannot be recorded and must not be truncated (RC2).
    [InlineData("""{"type":"turn.completed","usage":{"output_tokens":184467440737095516150}}""")]
    // Not JSON at all, which is the stream of a provider version this kernel does not know.
    [InlineData("turn.completed")]
    public async Task AProviderStatingNumbersTheKernelCannotRecordStillClosesItsRun(string terminalEvent)
    {
        using var root = new TemporaryDirectory();
        var adapter = new StreamingAdapter("codex", [Terminal(terminalEvent)]);

        var run = await LaunchAsync(root.Path, adapter, expectedExit: null);

        Assert.NotEqual(AgentRunStatus.Active, run.Status);
        Assert.True(run.Turns is null or >= 0);
        foreach (var recorded in new[]
                 {
                     run.OutputTokens, run.TokensInUncached, run.TokensInCacheWrite,
                     run.TokensInCacheRead, run.MillisecondsToFirstLedgerWrite
                 })
        {
            Assert.True(recorded is null or >= 0);
        }
    }

    // The RC1 shape, which cannot be reached through the parser: valid JSON that is not an object.
    // ProviderProtocol.ParseCodex throws InvalidOperationException on it rather than returning an
    // event (IC15), so this drives the reader's own guard with the event a future parser would hand
    // it, and pins that the guard is what keeps the throw out of the completion path.
    [Fact]
    public async Task ATerminalPayloadThatIsNotAJsonObjectStillClosesItsRun()
    {
        using var root = new TemporaryDirectory();
        var adapter = new StreamingAdapter(
            "codex", [new ProviderEvent(0, "turn.completed", """["turn.completed"]""", null, true, false)]);

        var run = await LaunchAsync(root.Path, adapter, expectedExit: null);

        Assert.NotEqual(AgentRunStatus.Active, run.Status);
        Assert.Null(run.OutputTokens);
        Assert.Null(run.Model);
    }

    // K14: the served model is earned, not guessed. Claude states the models that served as the keys
    // of modelUsage on its terminal event (IC14, IE21, IE22), so a stream naming exactly one names
    // the model that served.
    [Fact]
    public async Task ALaunchRecordsTheServedModelWhenTheTerminalEventNamesExactlyOne()
    {
        using var root = new TemporaryDirectory();
        var adapter = ClaudeAdapter(
            """
            {"type":"result","subtype":"success","is_error":false,"num_turns":3,"session_id":"session-1",
             "usage":{"input_tokens":2,"output_tokens":9},
             "modelUsage":{"claude-opus-4-7":{"inputTokens":2,"outputTokens":9}}}
            """);

        var run = await LaunchAsync(root.Path, adapter);

        Assert.Equal("claude-opus-4-7", run.Model);
    }

    // The absences the same read has to produce. Two served models have no single served model, and
    // codex states none at all (IC13, IE20) — so a value in either case could only have come from
    // the request, which is what K14 forbids. Paired with the positive read above.
    [Fact]
    public async Task ARunWithNoOneServedModelRecordsNone()
    {
        using var twoModels = new TemporaryDirectory();
        using var codex = new TemporaryDirectory();
        var twoModelAdapter = ClaudeAdapter(
            """
            {"type":"result","subtype":"success","is_error":false,"num_turns":3,"session_id":"session-1",
             "modelUsage":{"claude-opus-4-7":{"outputTokens":9},"claude-haiku-4-5":{"outputTokens":1}}}
            """);

        var served = await LaunchAsync(twoModels.Path, twoModelAdapter);
        var codexRun = await LaunchAsync(codex.Path, CodexAdapter(CodexThreadStarted, CodexTurnCompleted));

        Assert.Null(served.Model);
        Assert.Null(codexRun.Model);
    }

    // D5: the child's writes are attributable because the launcher put the run id on the command
    // line the briefing hands over, and CommandLine.Parse takes an option wherever it appears (C21,
    // E27). Both halves are asserted: the flag the child is given, and the measurement it produces.
    [Fact]
    public async Task TheChildsFirstLedgerWriteIsMeasuredFromTheRunIdOnItsOwnCommandLine()
    {
        using var root = new TemporaryDirectory();
        CliApplication? application = null;
        var adapter = CodexAdapter(CodexThreadStarted, CodexTurnCompleted);
        // What a briefed agent does first: record a claim, using the command line it was handed.
        adapter.WhileRunning = async request =>
        {
            var correlation = CorrelationFrom(request.LedgerCommandLine);
            await application!.RunAsync(
                ["claim", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
                 "--id", "IC1", "--statement", "The child reached the ledger",
                 "--consequence", "Nothing would measure the reach",
                 "--correlation", correlation],
                CancellationToken.None);
        };
        application = Application(adapter);

        var run = await LaunchAsync(root.Path, adapter, application);

        // The mechanism: the flag the child is handed, quoted like the paths beside it.
        Assert.Contains("--correlation \"R1\"", adapter.Request!.LedgerCommandLine, StringComparison.Ordinal);
        // The measurement. Zero is legal — a child that reached the ledger inside the first
        // millisecond measured zero — so the assertion is on the presence and the sign, not a value.
        Assert.NotNull(run.MillisecondsToFirstLedgerWrite);
        Assert.True(run.MillisecondsToFirstLedgerWrite >= 0);
    }

    // The paired absence. A child that never wrote has no first write, and that is a different fact
    // from a fast one: it is the run that burned tokens and recorded nothing, which is the failure
    // this field exists to name.
    [Fact]
    public async Task ARunWhoseChildNeverWroteRecordsNoFirstLedgerWrite()
    {
        using var root = new TemporaryDirectory();

        var run = await LaunchAsync(root.Path, CodexAdapter(CodexThreadStarted, CodexTurnCompleted));

        Assert.Null(run.MillisecondsToFirstLedgerWrite);
    }

    // RC6: a correlation id is whatever its caller passed, and a run id is composed by hand before
    // the run exists, so an earlier command can already carry the one a run will use. Taken as the
    // earliest correlated event, it made the interval negative, the floor recorded nothing, and a run
    // that did reach the ledger was filed as one that never did.
    [Fact]
    public async Task AnEventCorrelatedToTheRunIdBeforeItStartedIsNotItsFirstWrite()
    {
        using var root = new TemporaryDirectory();
        CliApplication? application = null;
        var adapter = CodexAdapter(CodexThreadStarted, CodexTurnCompleted);
        adapter.WhileRunning = async request =>
            await application!.RunAsync(
                ["claim", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
                 "--id", "IC2", "--statement", "The child reached the ledger",
                 "--consequence", "Nothing would measure the reach",
                 "--correlation", CorrelationFrom(request.LedgerCommandLine)],
                CancellationToken.None);
        application = Application(adapter);
        await OpenTaskAsync(application, root.Path);
        // The operator's own write, before the run exists, already carrying the id the run will use.
        var seeded = await application.RunAsync(
            ["claim", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "IC1", "--statement", "The run id was composed before the run started",
             "--consequence", "The earliest correlated event is not the child's first write",
             "--correlation", "R1"],
            CancellationToken.None);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", WorkingDirectory(), "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        Assert.Equal(0, seeded);
        Assert.Equal(0, exit);
        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var run = state!.Runs[new RunId("R1")];
        // The child's write, measured from the run. Zero is legal, so the assertion is on the
        // presence and the sign — but absence here is the defect, not a fast child.
        Assert.NotNull(run.MillisecondsToFirstLedgerWrite);
        Assert.True(run.MillisecondsToFirstLedgerWrite >= 0);
    }

    // C22: the first commands a briefed agent issues are reads, and they declare no correlation
    // option of their own. Without '--correlation' in GlobalOptions the launcher's own flag would
    // make the child's first act fail — a broken brief rather than a lost measurement.
    [Fact]
    public async Task TheReadCommandsAcceptTheCorrelationTheLauncherHandsTheChild()
    {
        using var root = new TemporaryDirectory();
        var application = Application(CodexAdapter(CodexThreadStarted, CodexTurnCompleted));
        await OpenTaskAsync(application, root.Path);

        var status = await application.RunAsync(
            ["status", "--root", root.Path, "--task", "T1", "--actor", "operator", "--correlation", "R1"],
            CancellationToken.None);
        var context = await application.RunAsync(
            ["context", "build", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--cognitive-root", FindCognitiveRoot(), "--correlation", "R1"],
            CancellationToken.None);

        Assert.Equal(0, status);
        Assert.Equal(0, context);
    }

    // D6: the launcher held the whole stream and dropped it (C3, E4). It is kept beside the log and
    // not in it, because replay byte-compares what it reads and nothing replays this file (ALT2).
    [Fact]
    public async Task TheProviderResultIsKeptBesideTheEventLog()
    {
        using var root = new TemporaryDirectory();
        var adapter = CodexAdapter(CodexThreadStarted, CodexTurnCompleted);

        await LaunchAsync(root.Path, adapter);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(SidecarPath(root.Path, "R1")));
        var kept = document.RootElement;
        Assert.Equal("R1", kept.GetProperty("runId").GetString());
        // The whole stream, verbatim, including the event the reader never looks at. The point of the
        // file is that nothing in it is interpreted first.
        var events = kept.GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal(2, events.Length);
        Assert.Contains("reasoning_output_tokens", events[1].GetProperty("rawJson").GetString()!, StringComparison.Ordinal);
    }

    // A run that dies is the run whose stream is most worth reading, so the failure path is not an
    // afterthought (D6). The cost is recorded on the same terms: what a failed run spent is what it
    // spent.
    [Fact]
    public async Task AFailedRunKeepsItsProviderResultAndItsCost()
    {
        using var root = new TemporaryDirectory();
        var adapter = CodexAdapter(CodexThreadStarted, CodexTurnCompleted);
        adapter.Status = AgentRunStatus.Failed;

        var run = await LaunchAsync(root.Path, adapter, expectedExit: null);

        Assert.Equal(AgentRunStatus.Failed, run.Status);
        Assert.Equal(28399, run.OutputTokens);
        Assert.True(File.Exists(SidecarPath(root.Path, "R1")));
    }

    // VC6: the run id is interpolated into '--correlation "<run-id>"' on the command line the
    // briefing hands the child, so an embedded double quote closes that argument, changes the
    // correlation the child would record and appends shell text the child would run. It is refused
    // where it enters, before anything is recorded and before the adapter is called — so the
    // assertion is that no run exists and the provider was never launched, not that the dangerous
    // string was escaped somewhere downstream.
    [Theory]
    // The injection itself: the quote closes the argument and the rest is a new command.
    [InlineData("""R1"; touch pwned; #""")]
    // A quote alone, which needs no appended command to change the correlation the child records.
    [InlineData("""R"1""")]
    // The separator that walked the sidecar out of the runs directory (IC17).
    [InlineData("../R1")]
    // A space, which splits the quoted argument's neighbours apart in a shell that re-splits it.
    [InlineData("R 1")]
    // Substitution, which needs no quote at all in a shell that expands double-quoted text.
    [InlineData("R$(id)")]
    public async Task ARunIdThatIsNotSafeInTheChildsShellIsRefusedBeforeAnythingIsRecorded(string runId)
    {
        using var root = new TemporaryDirectory();
        var errors = new StringWriter();
        var adapter = CodexAdapter(CodexThreadStarted, CodexTurnCompleted);
        var application = new CliApplication(
            TextWriter.Null, errors, Service, _ => adapter, new ContextAssembler());
        await OpenTaskAsync(application, root.Path);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", runId, "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", WorkingDirectory(), "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        Assert.NotEqual(0, exit);
        // No command line was ever composed, because the provider was never reached.
        Assert.Null(adapter.Request);
        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Empty(state!.Runs);
        Assert.Contains("is not allowed", errors.ToString(), StringComparison.Ordinal);
    }

    // IC22: the alphabet admits a hyphen anywhere, so it admitted a run id that begins with one —
    // and the briefing emits the run id as the value of '--correlation' on the command line the child
    // copies. CommandLine.ReadFollowingValue refuses a value beginning with '--' as a missing one, so
    // every ledger command the child issued failed at parse. That is a child that cannot record
    // anything, not a run whose cost went unmeasured.
    //
    // Given as '--run=<value>' because '--run <value>' cannot carry it: this parser refuses it on the
    // parent's own command line for exactly the reason the child's would fail, which is why the guard
    // reads the value rather than the shape of the argument that delivered it.
    [Fact]
    public async Task ARunIdThatWouldParseAsAnOptionOnTheChildsCommandLineIsRefused()
    {
        using var root = new TemporaryDirectory();
        var errors = new StringWriter();
        var adapter = CodexAdapter(CodexThreadStarted, CodexTurnCompleted);
        var application = new CliApplication(
            TextWriter.Null, errors, Service, _ => adapter, new ContextAssembler());
        await OpenTaskAsync(application, root.Path);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run=--R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", WorkingDirectory(), "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        Assert.NotEqual(0, exit);
        Assert.Null(adapter.Request);
        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Empty(state!.Runs);
        Assert.Contains("is not allowed", errors.ToString(), StringComparison.Ordinal);

        // What the refusal prevents, through the parser the child uses: the flag the briefing would
        // have handed it, in front of the read a briefed agent issues first.
        var child = await application.RunAsync(
            ["--correlation", "--R1", "status", "--root", root.Path, "--task", "T1",
             "--actor", "operator"],
            CancellationToken.None);

        Assert.NotEqual(0, child);
    }

    // The other half of the guard: the alphabet has to still admit the ids this ledger actually
    // uses — 'R-research-2', 'RA1' — and the correlation the child is handed has to be the run id
    // byte for byte, or the measurement it feeds attributes the writes to nothing.
    [Fact]
    public async Task AnAcceptedRunIdReachesTheChildsCommandLineUnchanged()
    {
        using var root = new TemporaryDirectory();
        var adapter = CodexAdapter(CodexThreadStarted, CodexTurnCompleted);
        var application = Application(adapter);
        await OpenTaskAsync(application, root.Path);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", "R-research_2.1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", WorkingDirectory(), "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(
            "--correlation \"R-research_2.1\"", adapter.Request!.LedgerCommandLine, StringComparison.Ordinal);
        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.True(state!.Runs.ContainsKey(new RunId("R-research_2.1")));
    }

    // RC5: the run being closed is the one this process started, and the sidecar was named after the
    // one the result carried. The production adapters echo the request's id and the launcher must not
    // assume it, so a result naming a different run wrote that run's stream under its name — over a
    // genuine file, in the safe case — while closing this run from a stream that is not its own.
    //
    // Both rows are mismatches, which is now the one thing that matters about them: the escaping id
    // is refused for the same reason 'R2' is, rather than for the separators it carries. The file the
    // write would have landed on exists first in both, so the assertion is that it still holds what
    // it held, not merely that nothing new appeared.
    [Theory]
    // A filename-safe id belonging to another run, which every guard downstream accepts (RC5).
    [InlineData("R2")]
    // The separator that walked the sidecar out of the runs directory (IC17), now refused earlier.
    [InlineData("../R1")]
    public async Task AProviderResultNamingAnotherRunKeepsNoFileAndClosesTheRunAsFailed(string reported)
    {
        using var root = new TemporaryDirectory();
        var errors = new StringWriter();
        var adapter = CodexAdapter(CodexThreadStarted, CodexTurnCompleted);
        adapter.ReportedRunId = new RunId(reported);
        var application = new CliApplication(
            TextWriter.Null, errors, Service, _ => adapter, new ContextAssembler());
        await OpenTaskAsync(application, root.Path);
        // Whatever is already at the path the mismatched write would target. 'runs/../R1.json'
        // normalises one directory up, into the task workspace beside events.jsonl, so the path is
        // resolved rather than assumed.
        var targeted = Path.GetFullPath(Path.Combine(root.Path, "T1", "runs", reported + ".json"));
        Directory.CreateDirectory(Path.GetDirectoryName(targeted)!);
        await File.WriteAllTextAsync(targeted, "the other run's stream");

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", WorkingDirectory(), "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        // First, because the loss is the finding: the file the other run's stream was in.
        Assert.Equal("the other run's stream", await File.ReadAllTextAsync(targeted));
        Assert.NotEqual(0, exit);
        // Nothing was persisted for the launched run either: there is no result this launcher can
        // attribute to it, and a stream from another run is not one.
        Assert.False(File.Exists(SidecarPath(root.Path, "R1")));
        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var run = state!.Runs[new RunId("R1")];
        // Closed, not stranded: an active run blocks the next run on its work item and blocks
        // Archive, so a protocol fault must still end the run it opened.
        Assert.Equal(AgentRunStatus.Failed, run.Status);
        // And closed with nothing read off the foreign stream, rather than with its numbers.
        Assert.Null(run.OutputTokens);
        Assert.Null(run.Turns);
        Assert.Null(run.Model);
        Assert.Contains($"returned a result for run '{reported}'", errors.ToString(), StringComparison.Ordinal);
    }

    // RC4: the alphabet is a command-time tightening, and a tightening reaches only what it creates.
    // Applied where '--run' names a run that already exists, it left a run recorded before the guard
    // existed impossible to complete and impossible to file its mandatory review artifact against —
    // an active run nothing can end, which blocks the next run on its work item and blocks Archive.
    // Seeded through the service, because the CLI is now the one thing that cannot create it.
    [Fact]
    public async Task ARunIdRecordedBeforeTheGuardExistedCanStillBeCompletedAndReviewed()
    {
        using var root = new TemporaryDirectory();
        var errors = new StringWriter();
        var application = new CliApplication(
            TextWriter.Null, errors, Service,
            _ => throw new InvalidOperationException("No provider is launched by this test."),
            new ContextAssembler());
        await OpenTaskAsync(application, root.Path);
        await Service(root.Path).ExecuteAsync(
            new TaskId("T1"),
            new StartRunCommand(
                new ActorId("operator"), null, "legacy", new RunId(LegacyRunId), null,
                "codex", "legacy-session"),
            CancellationToken.None);

        int artifactExit;
        using (new StandardInput("# Contract\n"))
        {
            artifactExit = await application.RunAsync(
                ["artifact", "record", "--root", root.Path, "--task", "T1", "--actor", "operator",
                 "--id", "A1", "--kind", "prompt-contract", "--title", "Contract",
                 "--run", LegacyRunId, "--body-stdin"],
                CancellationToken.None);
        }

        var completeExit = await application.RunAsync(
            ["run", "complete", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", LegacyRunId, "--status", "completed", "--session", "legacy-session"],
            CancellationToken.None);

        Assert.Equal(0, artifactExit);
        Assert.Equal(0, completeExit);
        Assert.DoesNotContain("is not allowed", errors.ToString(), StringComparison.Ordinal);
        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(AgentRunStatus.Completed, state!.Runs[new RunId(LegacyRunId)].Status);
        // The artifact names the run, which is what the completion gate reads: an id the CLI refuses
        // to associate is a review that cannot be filed.
        Assert.Equal(new RunId(LegacyRunId), state.Artifacts[new ArtifactId("A1")].ProducerRunId);
    }

    private static string SidecarPath(string root, string runId) =>
        Path.Combine(root, "T1", "runs", runId + ".json");

    // The correlation the child was handed, read back off the command line the way a child that
    // copies it would use it.
    private static string CorrelationFrom(string ledgerCommandLine)
    {
        var marker = "--correlation \"";
        var start = ledgerCommandLine.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return ledgerCommandLine[start..ledgerCommandLine.IndexOf('"', start)];
    }

    // The production launch, replayed from disk. Nothing here reads the state the command returned:
    // the fields have to survive the log, and the reducer is the only thing that proves it.
    private static async Task<AgentRun> LaunchAsync(
        string root,
        StreamingAdapter adapter,
        CliApplication? application = null,
        int? expectedExit = 0)
    {
        application ??= Application(adapter);
        await OpenTaskAsync(application, root);
        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root, "--task", "T1", "--actor", "operator",
             "--run", "R1", "--provider", adapter.Provider, "--executable", "/usr/bin/true",
             "--working-directory", WorkingDirectory(), "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);
        if (expectedExit is { } expected)
        {
            Assert.Equal(expected, exit);
        }

        var state = await Service(root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        return state!.Runs[new RunId("R1")];
    }

    private static async Task OpenTaskAsync(CliApplication application, string root) =>
        await application.RunAsync(
            ["task", "open", "--root", root, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None);

    // A codex terminal event whose payload the parser would still hand over as text: ParseCodex
    // reads only 'type', so a line that is not JSON never becomes an event at all, and one that is
    // an object with nothing in it does.
    private static ProviderEvent Terminal(string rawJson) =>
        new(0, "turn.completed", rawJson, null, true, false);

    private static StreamingAdapter ClaudeAdapter(params string[] events) =>
        new("claude", events.Select((json, index) => ProviderProtocol.ParseClaude(index, Compact(json))).ToArray());

    private static StreamingAdapter CodexAdapter(params string[] events) =>
        new("codex", events.Select((json, index) => ProviderProtocol.ParseCodex(index, Compact(json))).ToArray());

    // The fixtures are written across lines to stay readable; a provider emits one line per event.
    private static string Compact(string json) =>
        json.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);

    private static CliApplication Application(StreamingAdapter adapter) => new(
        TextWriter.Null, TextWriter.Null, Service, _ => adapter, new ContextAssembler());

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }

    // Outside the ledger root, which the launcher requires of a provider working directory.
    private static string WorkingDirectory() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ailedger-run-cost", Guid.NewGuid().ToString("N")))
            .FullName;

    private static string FindCognitiveRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "cognitive");
            if (File.Exists(Path.Combine(candidate, "manifest.json")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Test could not locate the cognitive root.");
    }

    // An adapter that returns a stream the test wrote, and optionally writes to the ledger while it
    // is running — which is what a briefed agent does, and the only way the first-write measurement
    // can be driven through the production path.
    private sealed class StreamingAdapter(string provider, IReadOnlyList<ProviderEvent> events) : IAgentAdapter
    {
        public AgentLaunchRequest? Request { get; private set; }

        public AgentRunStatus Status { get; set; } = AgentRunStatus.Completed;

        // The production adapters echo the request's run id onto the result (AgentAdapterBase:169);
        // this is how a test drives the case where one does not.
        public RunId? ReportedRunId { get; set; }

        public Func<AgentLaunchRequest, Task>? WhileRunning { get; set; }

        public string Provider => provider;

        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult("test");

        public async Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            if (WhileRunning is { } write)
            {
                await write(request).ConfigureAwait(false);
            }

            return new AgentRunResult(
                ReportedRunId ?? request.RunId, Provider, "session-1", Status,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1),
                0, null, events, string.Empty, "test", [], false, null);
        }
    }
}
