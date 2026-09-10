using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Cli;

// TaskRetrospectiveTests pins what the projection computes; these pin what `retrospective build`
// actually prints. The two are not the same check, and the gap between them is the whole of IC3: a
// projection with no command surface is a projection no operator can run, and no kernel test can
// tell the difference. Every assertion here reads the parsed JSON rather than searching the text,
// because a substring match passes on output that is not the document it claims to be.
//
// The archive walk records artifacts through the CLI, which reads the body from Console.In, so this
// class shares the standard-input collection with every other class that does.
[Collection(StandardInput.Collection)]
public sealed class TaskRetrospectiveCliTests
{
    // Words a scoring agent would read as a judgement already made. ALT2 settled that the
    // projection makes none: the moment it does, the number is one an agent can move without doing
    // the work. So the guard is on the printed keys, at the surface an operator and an agent read.
    private static readonly string[] JudgementWords =
    [
        "score", "grade", "rating", "rank", "verdict", "overall", "quality", "assessment"
    ];

    [Fact]
    public async Task RetrospectiveBuildPrintsTheProjectionForATaskBuiltThroughTheCli()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var application = Create(output, error);
        var common = Common(root.Path);
        var exits = new List<int>
        {
            await application.RunAsync(
                ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None),
            await application.RunAsync(
                ["claim", "add", .. common, "--id", "C1", "--statement", "The adapter drops the session"],
                CancellationToken.None),
            await application.RunAsync(
                ["evidence", "add", .. common, "--id", "E1", "--source-type", "source-read",
                 "--citation", "AgentAdapterBase.cs:104", "--summary", "The catch block returns a null identity",
                 "--supports", "C1"], CancellationToken.None),
            await application.RunAsync(
                ["claim", "resolve", .. common, "--id", "C1", "--status", "validated", "--evidence", "E1"],
                CancellationToken.None),
            // Left open, and carrying nothing. It is what makes the epistemic counts below
            // distinguish an earned claim from an unearned one rather than reading the same.
            await application.RunAsync(
                ["claim", "add", .. common, "--id", "C2", "--statement", "An unearned assumption"],
                CancellationToken.None),
            await application.RunAsync(
                ["alternative", "record", .. common, "--id", "ALT1", "--statement", "Cache the manifest",
                 "--rejected-because", "The manifest is role-filtered per run"], CancellationToken.None)
        };

        var versionBefore = await VersionAsync(root.Path);
        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(["retrospective", "build", .. common], CancellationToken.None);

        Assert.All(exits, code => Assert.Equal(0, code));
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        var report = document.RootElement;
        Assert.Equal("T1", report.GetProperty("task").GetString());
        Assert.Equal("discovery", report.GetProperty("stage").GetString());
        Assert.Equal(versionBefore, report.GetProperty("version").GetInt64());

        var claims = report.GetProperty("epistemic").GetProperty("claims");
        Assert.Equal(1, claims.GetProperty("open").GetInt32());
        Assert.Equal(1, claims.GetProperty("validated").GetInt32());
        Assert.Equal(0, claims.GetProperty("rejected").GetInt32());
        Assert.Equal(0, report.GetProperty("epistemic").GetProperty("openClaimsWithSupportingEvidence").GetInt32());
        Assert.Equal(1, report.GetProperty("epistemic").GetProperty("alternatives").GetInt32());
        var evidence = report.GetProperty("epistemic").GetProperty("evidence");
        Assert.Equal(1, evidence.GetProperty("total").GetInt32());
        Assert.Equal(1, evidence.GetProperty("supportsOnly").GetInt32());

        // The event log, not state: the counts come from the history the command read, and two
        // claim.added rows are the two claims above.
        Assert.Equal(2, report.GetProperty("events").GetProperty("claim.added").GetInt32());
        Assert.Equal(1, report.GetProperty("events").GetProperty("claim.resolved").GetInt32());

        // The load-bearing half of the output. C1 says six of self-scoring's ten dimensions cannot
        // be answered from a task's record, so a projection that printed only what it could measure
        // would invite a scoring agent to fill the rest from prose.
        var notMeasured = report.GetProperty("notMeasured").EnumerateArray()
            .Select(entry => entry.GetString()).ToArray();
        Assert.NotEmpty(notMeasured);
        Assert.All(notMeasured, entry => Assert.False(string.IsNullOrWhiteSpace(entry)));

        // No run has been launched, so nothing measured this task's cost. Unmeasured has to stay
        // distinguishable from a measured zero (C4), which is what the count beside the total is.
        var cost = report.GetProperty("cost");
        Assert.Equal(0, cost.GetProperty("runsMeasured").GetInt32());
        Assert.Equal(0, cost.GetProperty("outputTokens").GetProperty("runsMeasured").GetInt32());
        Assert.False(cost.GetProperty("outputTokens").TryGetProperty("total", out _));

        var coordinatorCost = report.GetProperty("coordinatorLoop").GetProperty("tokenCost");
        var coordinatorAbsence = coordinatorCost.GetProperty("absence").GetString()!;
        Assert.Contains("noCoordinatorUsageSupplied", coordinatorAbsence, StringComparison.Ordinal);
        Assert.Contains("no harness transcript was named", coordinatorAbsence, StringComparison.Ordinal);
        Assert.DoesNotContain("a usage record was read", coordinatorAbsence, StringComparison.Ordinal);

        AssertNoJudgementKey(report);
    }

    // The second half of what a read has to be: it runs against a task nothing may touch again, and
    // leaves it exactly as it found it. An archived task is the case that matters, because it is the
    // one a scoring agent will actually be pointed at.
    [Fact]
    public async Task RetrospectiveBuildExitsZeroForAnArchivedTaskAndChangesNothing()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var application = Create(output, error);
        var common = Common(root.Path);
        var exits = await ArchiveATaskAsync(application, common);

        var versionBefore = await VersionAsync(root.Path);
        var logBefore = await File.ReadAllTextAsync(EventLogPath(root.Path));
        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(["retrospective", "build", .. common], CancellationToken.None);

        Assert.All(exits, code => Assert.Equal(0, code));
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        var report = document.RootElement;
        Assert.Equal("archive", report.GetProperty("stage").GetString());
        Assert.Equal(versionBefore, report.GetProperty("version").GetInt64());
        // The runs the walk closed are counted, and each carries the role its subject held.
        Assert.True(report.GetProperty("runs").GetProperty("total").GetInt32() > 0);
        Assert.Equal(1, report.GetProperty("runs").GetProperty("bySubjectRole").GetProperty("verifier").GetInt32());
        Assert.Equal(1, report.GetProperty("lessons").GetProperty("minted").GetInt32());
        var workItem = Assert.Single(report.GetProperty("workItems").EnumerateArray().ToArray());
        Assert.Equal("W1", workItem.GetProperty("id").GetString());
        AssertNoJudgementKey(report);

        // The read wrote nothing. Version and log are both checked: a command that appended an
        // event would move the first, and one that rewrote a row without changing the count would
        // move only the second.
        Assert.Equal(versionBefore, await VersionAsync(root.Path));
        Assert.Equal(logBefore, await File.ReadAllTextAsync(EventLogPath(root.Path)));
    }

    // Absent journal and empty journal are two different facts, and only the caller can tell them
    // apart — the projection cannot open the file, because RefusalJournal is in Storage and Core
    // cannot reference it. A caller that passed an empty list for a missing file would silently
    // convert an unmeasured task into a clean one.
    [Fact]
    public async Task RefusalsAreAbsentUntilOneIsJournalledAndAreCountedAfterwards()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var application = Create(output, TextWriter.Null);
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);

        output.GetStringBuilder().Clear();
        await application.RunAsync(["retrospective", "build", .. common], CancellationToken.None);
        using (var clean = JsonDocument.Parse(output.ToString()))
        {
            Assert.False(clean.RootElement.TryGetProperty("refusals", out _));
        }

        // Refused for the rule it breaks: resolving a claim to validated requires an evidence
        // record naming it by direction, and there is none. The refusal leaves through the one
        // journal write site in FileGovernedTaskService.
        await application.RunAsync(
            ["claim", "add", .. common, "--id", "C1", "--statement", "An unearned assumption"],
            CancellationToken.None);
        var refusedExit = await application.RunAsync(
            ["claim", "resolve", .. common, "--id", "C1", "--status", "validated"], CancellationToken.None);

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(["retrospective", "build", .. common], CancellationToken.None);

        Assert.Equal(1, refusedExit);
        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var refusals = document.RootElement.GetProperty("refusals");
        Assert.Equal(1, refusals.GetProperty("total").GetInt32());
        Assert.Equal(1, refusals.GetProperty("bySite").GetProperty(RefusalSite.Service).GetInt32());
        Assert.Equal(1, refusals.GetProperty("byActor").GetProperty("operator").GetInt32());
        Assert.Equal(
            1, refusals.GetProperty("byCommand").GetProperty(nameof(ResolveClaimCommand)).GetInt32());
    }

    // The third state, over the real file, because it is the reader and not the projection that can
    // see it (RC1). Storage permits the journal to be stale or truncated, so a row that will not
    // parse is a fact about this file that the count of parseable rows cannot express. A reader that
    // dropped it silently would print `total: 1` for a task that took two refusals and leave
    // `notMeasured` empty of the dimension, which reads as a complete measurement of fewer refusals.
    [Fact]
    public async Task AJournalWithARowThatWillNotParseIsCountedAsPartlyReadAndNotAsComplete()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var application = Create(output, TextWriter.Null);
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["claim", "add", .. common, "--id", "C1", "--statement", "An unearned assumption"],
            CancellationToken.None);
        // One real refusal, so the journal exists and holds a row the reader can read: what is under
        // test is a partly readable file, which an empty one could not be.
        var refusedExit = await application.RunAsync(
            ["claim", "resolve", .. common, "--id", "C1", "--status", "validated"], CancellationToken.None);

        // Appended rather than written over the row above, and truncated mid-object rather than made
        // up: this is the shape a journal takes when a write was cut short, which is the case Storage
        // says the file is allowed to be in.
        var journal = RefusalJournal.ResolvePath(
            new TaskWorkspacePathResolver(root.Path).Resolve(new TaskId("T1")));
        await File.AppendAllTextAsync(journal, "{\"recordedAt\":\"2026-09-09T10:10:00" + Environment.NewLine);

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(["retrospective", "build", .. common], CancellationToken.None);

        Assert.Equal(1, refusedExit);
        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var refusals = document.RootElement.GetProperty("refusals");
        Assert.Equal(1, refusals.GetProperty("total").GetInt32());
        Assert.Equal(1, refusals.GetProperty("unreadableRows").GetInt32());
        Assert.Contains(
            "refusals",
            document.RootElement.GetProperty("notMeasured").EnumerateArray().Select(entry => entry.GetString()));
    }

    // The whole file still reports as whole, which is what makes the assertion above mean anything:
    // a reader that counted every row as unreadable would satisfy it and measure nothing. A trailing
    // newline is present in every journal the writer produces and is not a lost row.
    [Fact]
    public async Task AJournalWhoseEveryRowParsesIsReportedAsFullyRead()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var application = Create(output, TextWriter.Null);
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["claim", "add", .. common, "--id", "C1", "--statement", "An unearned assumption"],
            CancellationToken.None);
        await application.RunAsync(
            ["claim", "resolve", .. common, "--id", "C1", "--status", "validated"], CancellationToken.None);

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(["retrospective", "build", .. common], CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var refusals = document.RootElement.GetProperty("refusals");
        Assert.Equal(1, refusals.GetProperty("total").GetInt32());
        Assert.Equal(0, refusals.GetProperty("unreadableRows").GetInt32());
        Assert.DoesNotContain(
            "refusals",
            document.RootElement.GetProperty("notMeasured").EnumerateArray().Select(entry => entry.GetString()));
    }

    // ValidateOptions refuses any command absent from AllowedOptions, so the entry is what makes the
    // command exist at all — and an entry with the wrong option set refuses a caller for an option
    // it should take, or accepts one it should not. Both directions are asserted, because the map
    // has one failure mode in each.
    [Fact]
    public async Task RetrospectiveBuildTakesTheReadOptionsAndRefusesAnythingElse()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        error.GetStringBuilder().Clear();

        // --correlation is global: a launched agent carries its run id there on every command it
        // issues, and a read that refused the flag would break the agent's first act.
        var accepted = await application.RunAsync(
            ["retrospective", "build", .. common, "--correlation", "R5"], CancellationToken.None);
        Assert.Equal(0, accepted);
        Assert.Equal(string.Empty, error.ToString());

        var refused = await application.RunAsync(
            ["retrospective", "build", .. common, "--follow"], CancellationToken.None);

        Assert.Equal(2, refused);
        Assert.Contains("follow", error.ToString(), StringComparison.Ordinal);
    }

    // R4 (transcript-identity-mismatch): usage from another harness session charged to the selected
    // coordinator session. The failure mode is the worst shape a measurement can take — valid JSON,
    // plausible numbers, the wrong conversation — so the transcript's own session identity is
    // checked against what the session recorded, and a mismatch is an absence with its reason named
    // rather than a total.
    [Fact]
    public async Task R4_rejects_mismatched_coordinator_transcript()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var harness = HarnessDirectory(root.Path);
        var application = Create(output, error, HarnessRoot(root.Path));
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["session", "start", .. common, "--id", "S1", "--harness", "claude-code",
             "--harness-session", "harness-1"], CancellationToken.None);
        // Inside the harness directory, so the containment check passes it and the identity check is
        // the control actually under test here.
        var foreign = Path.Combine(harness, "harness-9.jsonl");
        await File.WriteAllTextAsync(foreign, Transcript("harness-9", inputTokens: 11, outputTokens: 22));

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(
            ["retrospective", "build", .. common, "--coordinator-session", "S1",
             "--coordinator-transcript", foreign], CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var cost = document.RootElement.GetProperty("coordinatorLoop").GetProperty("tokenCost");
        Assert.Contains("transcriptSessionMismatch", cost.GetProperty("absence").GetString()!, StringComparison.Ordinal);
        // Not a zero, and not a partial total: the buckets are absent entirely.
        Assert.False(cost.TryGetProperty("outputTokens", out _));
        Assert.False(cost.TryGetProperty("tokensInCacheRead", out _));
        // And the top-level list still names it, so a reader of that list alone is not told the
        // coordinator's cost was measured.
        Assert.Contains(
            "coordinatorCost",
            document.RootElement.GetProperty("notMeasured").EnumerateArray().Select(entry => entry.GetString()));
    }

    // The positive case, because a check that only ever refuses is indistinguishable from one that
    // never reads anything. The four buckets are the ones AgentRun uses, so a coordinating session
    // and a dispatched run compare without conversion, and the source path is named on the
    // measurement.
    [Fact]
    public async Task RetrospectiveBuildReadsAMatchingCoordinatorTranscriptIntoTheFourAgentRunBuckets()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var harness = HarnessDirectory(root.Path);
        var application = Create(output, error, HarnessRoot(root.Path));
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["session", "start", .. common, "--id", "S1", "--harness", "claude-code",
             "--harness-session", "harness-1"], CancellationToken.None);
        var transcript = Path.Combine(harness, "harness-1.jsonl");
        await File.WriteAllTextAsync(transcript, Transcript("harness-1", inputTokens: 100, outputTokens: 200));

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(
            ["retrospective", "build", .. common, "--coordinator-session", "S1",
             "--coordinator-transcript", transcript], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        var cost = document.RootElement.GetProperty("coordinatorLoop").GetProperty("tokenCost");
        Assert.False(cost.TryGetProperty("absence", out _));
        Assert.Equal(transcript, cost.GetProperty("source").GetString());
        Assert.Equal("claude-opus-5", cost.GetProperty("model").GetString());
        Assert.Equal("S1", cost.GetProperty("session").GetString());
        // Two rows in the transcript, and the sidechain row excluded: those are a harness subagent's
        // tokens and a dispatched agent here is a governed run with its own cost record.
        Assert.Equal(2, cost.GetProperty("records").GetInt32());
        Assert.Equal(200, cost.GetProperty("tokensInUncached").GetInt64());
        Assert.Equal(400, cost.GetProperty("outputTokens").GetInt64());
        Assert.Equal(20, cost.GetProperty("tokensInCacheWrite").GetInt64());
        Assert.Equal(2_000, cost.GetProperty("tokensInCacheRead").GetInt64());
        Assert.DoesNotContain(
            "coordinatorCost",
            document.RootElement.GetProperty("notMeasured").EnumerateArray().Select(entry => entry.GetString()));
        // C7, and the repair the second verification round asked for. The four buckets are emitted
        // beside a statement of what admitted them, so a reader of a coordinator's cost learns from
        // the output alone that the control is provenance of location — the file sits where the
        // harness writes — and not authenticity. Without it the figure reads as audited, and what it
        // is worth is write access to one directory.
        var control = cost.GetProperty("control").GetString()!;
        Assert.Contains("harnessDirectoryProvenance", control, StringComparison.Ordinal);
        Assert.Contains(harness, control, StringComparison.Ordinal);
        Assert.Contains("not authenticity", control, StringComparison.Ordinal);
        Assert.Contains("write access", control, StringComparison.Ordinal);
    }

    // The other half of the same rule. An absence already names the control that refused it, so it
    // carries no second statement of provenance — a refusal that also described what the control
    // proves would be describing something that did not happen.
    [Fact]
    public async Task ARefusedTranscriptCarriesItsRefusalAndNoStatementOfControl()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var harness = HarnessDirectory(root.Path);
        var application = Create(output, TextWriter.Null, HarnessRoot(root.Path));
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["session", "start", .. common, "--id", "S1", "--harness", "claude-code",
             "--harness-session", "harness-1"], CancellationToken.None);

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(
            ["retrospective", "build", .. common, "--coordinator-session", "S1",
             "--coordinator-transcript", Path.Combine(harness, "nowhere.jsonl")], CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var cost = document.RootElement.GetProperty("coordinatorLoop").GetProperty("tokenCost");
        Assert.Contains("transcriptMissing", cost.GetProperty("absence").GetString()!, StringComparison.Ordinal);
        Assert.False(cost.TryGetProperty("control", out _));
    }

    // A transcript that is not there is an absence and not a zero, and the reason says which
    // absence it was. This is the case a codex-hosted coordinator is in permanently.
    [Fact]
    public async Task AMissingCoordinatorTranscriptIsReportedAsAnAbsenceAndNotAsZero()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var harness = HarnessDirectory(root.Path);
        var application = Create(output, TextWriter.Null, HarnessRoot(root.Path));
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["session", "start", .. common, "--id", "S1", "--harness", "claude-code",
             "--harness-session", "harness-1"], CancellationToken.None);

        output.GetStringBuilder().Clear();
        // Inside the harness directory and simply not there, which is the case the containment check
        // must not swallow: the two absences are different facts and are reported as different ones.
        var exit = await application.RunAsync(
            ["retrospective", "build", .. common, "--coordinator-session", "S1",
             "--coordinator-transcript", Path.Combine(harness, "nowhere.jsonl")], CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var cost = document.RootElement.GetProperty("coordinatorLoop").GetProperty("tokenCost");
        Assert.Contains("transcriptMissing", cost.GetProperty("absence").GetString()!, StringComparison.Ordinal);
    }

    // VC1, and the attack that found it. The identity check is not sufficient on its own: it proves
    // only that a file claims a harness session id, and a file claiming any id with any four token
    // totals can be written anywhere by anyone. The verifier copied a real matching transcript to a
    // temporary directory and the report charged all four buckets to the session and dropped
    // coordinatorCost from notMeasured.
    //
    // So the read is confined to the directory the harness itself writes to, and the absence names
    // which control refused it — a reader who sees only 'transcriptOutsideHarnessDirectory' with no
    // named control cannot tell a policy refusal from a parse failure.
    [Fact]
    public async Task AMatchingTranscriptOutsideTheHarnessDirectoryIsRefusedRatherThanCharged()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var harness = HarnessDirectory(root.Path);
        var application = Create(output, TextWriter.Null, HarnessRoot(root.Path));
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["session", "start", .. common, "--id", "S1", "--harness", "claude-code",
             "--harness-session", "harness-1"], CancellationToken.None);
        // Everything about this file is right except where it is: the identity matches the session
        // exactly, which is what makes it the attack rather than a typo.
        var planted = Path.Combine(root.Path, "harness-1.jsonl");
        await File.WriteAllTextAsync(planted, Transcript("harness-1", inputTokens: 100, outputTokens: 200));

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(
            ["retrospective", "build", .. common, "--coordinator-session", "S1",
             "--coordinator-transcript", planted], CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var cost = document.RootElement.GetProperty("coordinatorLoop").GetProperty("tokenCost");
        var absence = cost.GetProperty("absence").GetString()!;
        Assert.Contains("transcriptOutsideHarnessDirectory", absence, StringComparison.Ordinal);
        // The control that refused it, named, and the boundary it was measured against.
        Assert.Contains("containment check", absence, StringComparison.Ordinal);
        Assert.Contains(HarnessRoot(root.Path), absence, StringComparison.Ordinal);
        // Not a zero and not a partial total: no bucket is emitted at all.
        Assert.False(cost.TryGetProperty("tokensInUncached", out _));
        Assert.False(cost.TryGetProperty("outputTokens", out _));
        Assert.False(cost.TryGetProperty("tokensInCacheWrite", out _));
        Assert.False(cost.TryGetProperty("tokensInCacheRead", out _));
        // And the top-level list still names it, so a reader of that list alone is not told the
        // coordinator's cost was measured.
        Assert.Contains(
            "coordinatorCost",
            document.RootElement.GetProperty("notMeasured").EnumerateArray().Select(entry => entry.GetString()));
    }

    // The traversal form of the same attack: a path that begins inside the harness directory and
    // climbs out of it. Containment is decided on the resolved full path, so the climb is what is
    // refused rather than the spelling.
    [Fact]
    public async Task APathThatClimbsOutOfTheHarnessDirectoryIsRefusedOnItsResolvedForm()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var harness = HarnessDirectory(root.Path);
        var application = Create(output, TextWriter.Null, HarnessRoot(root.Path));
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["session", "start", .. common, "--id", "S1", "--harness", "claude-code",
             "--harness-session", "harness-1"], CancellationToken.None);
        var planted = Path.Combine(root.Path, "harness-1.jsonl");
        await File.WriteAllTextAsync(planted, Transcript("harness-1", inputTokens: 100, outputTokens: 200));

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(
            ["retrospective", "build", .. common, "--coordinator-session", "S1",
             "--coordinator-transcript",
             Path.Combine(harness, "..", "..", "..", "harness-1.jsonl")], CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var cost = document.RootElement.GetProperty("coordinatorLoop").GetProperty("tokenCost");
        Assert.Contains(
            "transcriptOutsideHarnessDirectory",
            cost.GetProperty("absence").GetString()!,
            StringComparison.Ordinal);
        Assert.False(cost.TryGetProperty("tokensInCacheRead", out _));
    }

    // YC2, first of three. A usage row whose counters are not counters was summed as zero, so a
    // corrupt transcript produced a smaller total that read exactly like a measured one. The
    // reader's invariant is that input it cannot read reports an absence and never a number, and a
    // row this harness never wrote is input it cannot read: all four counters are present as
    // non-negative integers on every usage record the supported harness writes (ZC2, ZE2).
    [Theory]
    // Negative, non-integral, absent, and past the range of the bucket it belongs in.
    [InlineData("\"input_tokens\":-1,\"output_tokens\":2,\"cache_creation_input_tokens\":3,\"cache_read_input_tokens\":4")]
    [InlineData("\"input_tokens\":1.5,\"output_tokens\":2,\"cache_creation_input_tokens\":3,\"cache_read_input_tokens\":4")]
    [InlineData("\"output_tokens\":2,\"cache_creation_input_tokens\":3,\"cache_read_input_tokens\":4")]
    [InlineData("\"input_tokens\":1,\"output_tokens\":9223372036854775808,\"cache_creation_input_tokens\":3,\"cache_read_input_tokens\":4")]
    public async Task ATranscriptRowWithABadTokenValueIsAnAbsenceAndNotAPartialTotal(string usage)
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var harness = HarnessDirectory(root.Path);
        var application = Create(output, TextWriter.Null, HarnessRoot(root.Path));
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["session", "start", .. common, "--id", "S1", "--harness", "claude-code",
             "--harness-session", "harness-1"], CancellationToken.None);
        var transcript = Path.Combine(harness, "harness-1.jsonl");
        // One good row and one bad, so the bad row is what the absence is about rather than an
        // empty file: without the repair this reported the good row's total on its own.
        await File.WriteAllTextAsync(transcript, string.Join('\n',
        [
            Transcript("harness-1", inputTokens: 100, outputTokens: 200).TrimEnd('\n'),
            "{\"sessionId\":\"harness-1\",\"message\":{\"model\":\"claude-opus-5\",\"usage\":{" + usage + "}}}"
        ]) + "\n");

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(
            ["retrospective", "build", .. common, "--coordinator-session", "S1",
             "--coordinator-transcript", transcript], CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var cost = document.RootElement.GetProperty("coordinatorLoop").GetProperty("tokenCost");
        Assert.Contains(
            "transcriptTokenValueUnreadable",
            cost.GetProperty("absence").GetString()!,
            StringComparison.Ordinal);
        // No bucket at all, so the good rows are not reported as the whole.
        Assert.False(cost.TryGetProperty("tokensInUncached", out _));
        Assert.False(cost.TryGetProperty("outputTokens", out _));
        Assert.False(cost.TryGetProperty("records", out _));
        Assert.Contains(
            "coordinatorCost",
            document.RootElement.GetProperty("notMeasured").EnumerateArray().Select(entry => entry.GetString()));
    }

    // YC2, second of three. The four additions were unchecked, so a long enough transcript of
    // individually valid values wrapped into a smaller or negative total — a figure below the rows
    // it was summed from, reading as a measurement.
    [Fact]
    public async Task ATotalThatWouldLeaveTheRangeOfItsBucketIsAnAbsenceAndNotAWrappedNumber()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var harness = HarnessDirectory(root.Path);
        var application = Create(output, TextWriter.Null, HarnessRoot(root.Path));
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["session", "start", .. common, "--id", "S1", "--harness", "claude-code",
             "--harness-session", "harness-1"], CancellationToken.None);
        var transcript = Path.Combine(harness, "harness-1.jsonl");
        // Two rows each carrying a valid Int64 whose sum is not one.
        var row = "{\"sessionId\":\"harness-1\",\"message\":{\"model\":\"claude-opus-5\",\"usage\":{" +
                  "\"input_tokens\":1,\"output_tokens\":9223372036854775807," +
                  "\"cache_creation_input_tokens\":3,\"cache_read_input_tokens\":4}}}";
        await File.WriteAllTextAsync(transcript, string.Join('\n', [row, row]) + "\n");

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(
            ["retrospective", "build", .. common, "--coordinator-session", "S1",
             "--coordinator-transcript", transcript], CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var cost = document.RootElement.GetProperty("coordinatorLoop").GetProperty("tokenCost");
        Assert.Contains(
            "transcriptTokenTotalOverflowed",
            cost.GetProperty("absence").GetString()!,
            StringComparison.Ordinal);
        Assert.False(cost.TryGetProperty("outputTokens", out _));
    }

    // YC2, third of three, and the one the identity check on the ledger side could never catch: only
    // the first session identity was kept and later rows were summed regardless, so two transcripts
    // concatenated charged both conversations under whichever id appeared first — and the outer
    // check passed, because it only ever saw that one. No transcript this harness writes carries two
    // identities across 17 files, so a second one means the file is not one transcript (ZC2, ZE2).
    [Fact]
    public async Task ATranscriptCarryingASecondSessionIdentityIsRefusedRatherThanSummed()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var harness = HarnessDirectory(root.Path);
        var application = Create(output, TextWriter.Null, HarnessRoot(root.Path));
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["session", "start", .. common, "--id", "S1", "--harness", "claude-code",
             "--harness-session", "harness-1"], CancellationToken.None);
        var transcript = Path.Combine(harness, "harness-1.jsonl");
        // The matching session first, so the ledger-side identity check would pass it, then another
        // conversation's rows appended behind it.
        await File.WriteAllTextAsync(transcript, string.Concat(
            Transcript("harness-1", inputTokens: 100, outputTokens: 200),
            Transcript("harness-9", inputTokens: 5_000, outputTokens: 6_000)));

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(
            ["retrospective", "build", .. common, "--coordinator-session", "S1",
             "--coordinator-transcript", transcript], CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var cost = document.RootElement.GetProperty("coordinatorLoop").GetProperty("tokenCost");
        var absence = cost.GetProperty("absence").GetString()!;
        Assert.Contains("transcriptCarriesTwoSessionIdentities", absence, StringComparison.Ordinal);
        // Both identities named, so a reader can see which two conversations the file holds.
        Assert.Contains("harness-1", absence, StringComparison.Ordinal);
        Assert.Contains("harness-9", absence, StringComparison.Ordinal);
        // Neither the matching rows alone nor the sum of both: no bucket is emitted.
        Assert.False(cost.TryGetProperty("tokensInUncached", out _));
        Assert.False(cost.TryGetProperty("tokensInCacheRead", out _));
        Assert.Contains(
            "coordinatorCost",
            document.RootElement.GetProperty("notMeasured").EnumerateArray().Select(entry => entry.GetString()));
    }

    // The rows that state no session identity at all, which every real transcript carries between 8
    // and 190 of: they are passed over rather than read as a disagreement, or the working case would
    // refuse itself (ZE2).
    [Fact]
    public async Task RowsThatStateNoSessionIdentityDoNotCountAsADisagreement()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var harness = HarnessDirectory(root.Path);
        var application = Create(output, TextWriter.Null, HarnessRoot(root.Path));
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["session", "start", .. common, "--id", "S1", "--harness", "claude-code",
             "--harness-session", "harness-1"], CancellationToken.None);
        var transcript = Path.Combine(harness, "harness-1.jsonl");
        await File.WriteAllTextAsync(transcript, string.Join('\n',
        [
            "{\"type\":\"summary\"}",
            Transcript("harness-1", inputTokens: 100, outputTokens: 200).TrimEnd('\n'),
            "{\"message\":{\"model\":\"claude-opus-5\",\"usage\":{\"input_tokens\":1," +
            "\"output_tokens\":1,\"cache_creation_input_tokens\":1,\"cache_read_input_tokens\":1}}}"
        ]) + "\n");

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(
            ["retrospective", "build", .. common, "--coordinator-session", "S1",
             "--coordinator-transcript", transcript], CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var cost = document.RootElement.GetProperty("coordinatorLoop").GetProperty("tokenCost");
        Assert.False(cost.TryGetProperty("absence", out _));
        // The two usage rows the fixture states an identity on, plus the one that states none.
        Assert.Equal(3, cost.GetProperty("records").GetInt32());
        Assert.Equal(201, cost.GetProperty("tokensInUncached").GetInt64());
        Assert.Equal(401, cost.GetProperty("outputTokens").GetInt64());
    }

    // The ordinary shape of a live transcript: the harness is still appending to it, so the last
    // line is half written. The four buckets are a floor in that case and not the session's cost,
    // and the count of what could not be read is what makes them readable as one. Before this the
    // count was surfaced only when nothing at all parsed, so the common case reported a total.
    [Fact]
    public async Task APartlyUnreadableTranscriptReportsHowManyRowsWereSkippedBesideItsTotal()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var harness = HarnessDirectory(root.Path);
        var application = Create(output, TextWriter.Null, HarnessRoot(root.Path));
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["session", "start", .. common, "--id", "S1", "--harness", "claude-code",
             "--harness-session", "harness-1"], CancellationToken.None);
        var transcript = Path.Combine(harness, "harness-1.jsonl");
        // The whole transcript, then a line the harness had not finished writing when the
        // retrospective read the file.
        await File.WriteAllTextAsync(transcript,
            Transcript("harness-1", inputTokens: 100, outputTokens: 200) +
            "{\"sessionId\":\"harness-1\",\"message\":{\"usage\":{\"input_tok");

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(
            ["retrospective", "build", .. common, "--coordinator-session", "S1",
             "--coordinator-transcript", transcript], CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var cost = document.RootElement.GetProperty("coordinatorLoop").GetProperty("tokenCost");
        Assert.False(cost.TryGetProperty("absence", out _));
        Assert.Equal(2, cost.GetProperty("records").GetInt32());
        Assert.Equal(200, cost.GetProperty("tokensInUncached").GetInt64());
        // The figure a reader needs to know the four buckets are a floor. It used to be discarded
        // the moment one row parsed, which is every live transcript.
        Assert.Equal(1, cost.GetProperty("unreadableRows").GetInt32());
    }

    // A transcript whose every usage row is a sidechain does hold per-request usage records — this
    // reader excluded them. Saying it holds none sends a reader looking for a harness that wrote
    // nothing, and there is no such harness here.
    [Fact]
    public async Task ATranscriptOfNothingButSidechainsSaysTheyWereExcludedAndNotThatThereAreNone()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var harness = HarnessDirectory(root.Path);
        var application = Create(output, TextWriter.Null, HarnessRoot(root.Path));
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["session", "start", .. common, "--id", "S1", "--harness", "claude-code",
             "--harness-session", "harness-1"], CancellationToken.None);
        var transcript = Path.Combine(harness, "harness-1.jsonl");
        var sidechain =
            "{\"sessionId\":\"harness-1\",\"isSidechain\":true,\"message\":{\"model\":\"claude-opus-5\"," +
            "\"usage\":{\"input_tokens\":5,\"output_tokens\":6,\"cache_creation_input_tokens\":7," +
            "\"cache_read_input_tokens\":8}}}";
        await File.WriteAllTextAsync(transcript, string.Join('\n', [sidechain, sidechain]) + "\n");

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(
            ["retrospective", "build", .. common, "--coordinator-session", "S1",
             "--coordinator-transcript", transcript], CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var absence = document.RootElement.GetProperty("coordinatorLoop")
            .GetProperty("tokenCost").GetProperty("absence").GetString()!;
        Assert.Contains("transcriptHoldsOnlySidechainUsage", absence, StringComparison.Ordinal);
        // The count the reader excluded, so the absence says what is in the file rather than
        // denying that anything is.
        Assert.Contains("2 per-request", absence, StringComparison.Ordinal);
        Assert.DoesNotContain("transcriptCarriesNoUsage", absence, StringComparison.Ordinal);
    }

    // And the other side of the same branch, unchanged: a file that genuinely holds no per-request
    // usage record still says so, in the words it always used. The sidechain case above took a
    // reading away from this message and gave it none of its own.
    [Fact]
    public async Task ATranscriptWithNoUsageRecordAtAllStillSaysItHoldsNone()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var harness = HarnessDirectory(root.Path);
        var application = Create(output, TextWriter.Null, HarnessRoot(root.Path));
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["session", "start", .. common, "--id", "S1", "--harness", "claude-code",
             "--harness-session", "harness-1"], CancellationToken.None);
        var transcript = Path.Combine(harness, "harness-1.jsonl");
        await File.WriteAllTextAsync(transcript,
            "{\"sessionId\":\"harness-1\",\"type\":\"summary\"}\n{\"sessionId\":\"harness-1\"}\n");

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(
            ["retrospective", "build", .. common, "--coordinator-session", "S1",
             "--coordinator-transcript", transcript], CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var absence = document.RootElement.GetProperty("coordinatorLoop")
            .GetProperty("tokenCost").GetProperty("absence").GetString()!;
        Assert.Equal(
            $"transcriptCarriesNoUsage: '{transcript}' holds no per-request usage record. " +
            "Its token cost stays unmeasured rather than being reported as zero.",
            absence);
    }

    // A harness that writes no usage record this reader knows how to parse reports an absence naming
    // its harness, rather than being parsed hopefully. D6 keeps D3's caution in exactly this form.
    [Fact]
    public async Task ACodexHostedCoordinatorReportsAnUnsupportedHarnessRatherThanAnEstimate()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var harness = HarnessDirectory(root.Path);
        var application = Create(output, TextWriter.Null, HarnessRoot(root.Path));
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["session", "start", .. common, "--id", "S1", "--harness", "codex-cli"], CancellationToken.None);
        var transcript = Path.Combine(harness, "harness-1.jsonl");
        await File.WriteAllTextAsync(transcript, Transcript("harness-1", inputTokens: 1, outputTokens: 1));

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(
            ["retrospective", "build", .. common, "--coordinator-session", "S1",
             "--coordinator-transcript", transcript], CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        var cost = document.RootElement.GetProperty("coordinatorLoop").GetProperty("tokenCost");
        Assert.Contains("unsupportedHarness", cost.GetProperty("absence").GetString()!, StringComparison.Ordinal);
    }

    // The session commands print through the same surface every mutation does, and the bracket they
    // record is what the runs dispatched inside it point back at.
    [Fact]
    public async Task SessionStartAndCompleteBracketTheCoordinatorsWorkThroughTheCli()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var application = Create(output, error);
        var common = Common(root.Path);
        var exits = new List<int>
        {
            await application.RunAsync(
                ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None),
            await application.RunAsync(
                ["session", "start", .. common, "--id", "S1", "--harness", "claude-code",
                 "--harness-session", "harness-1"], CancellationToken.None),
            await application.RunAsync(
                ["run", "start", .. common, "--run", "R1", "--provider", "claude",
                 "--coordinator-session", "S1"], CancellationToken.None),
            await application.RunAsync(
                ["run", "complete", .. common, "--run", "R1", "--status", "completed",
                 "--session", "provider-1"], CancellationToken.None),
            await application.RunAsync(
                ["session", "complete", .. common, "--id", "S1"], CancellationToken.None)
        };

        output.GetStringBuilder().Clear();
        var exit = await application.RunAsync(
            ["retrospective", "build", .. common], CancellationToken.None);

        Assert.All(exits, code => Assert.Equal(0, code));
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        var loop = document.RootElement.GetProperty("coordinatorLoop");
        var session = Assert.Single(loop.GetProperty("sessions").EnumerateArray().ToArray());
        Assert.Equal("S1", session.GetProperty("session").GetString());
        Assert.Equal("operator", session.GetProperty("actor").GetString());
        Assert.Equal(1, session.GetProperty("childRuns").GetInt32());
        Assert.True(session.TryGetProperty("wallClockHours", out _));
        AssertNoJudgementKey(document.RootElement);
    }

    // Two usage rows the reader must sum and one it must skip. The sidechain row is a harness
    // subagent's spend, which in this repository is a governed run with a cost record of its own.
    private static string Transcript(string harnessSession, long inputTokens, long outputTokens)
    {
        var row = (bool sidechain) => string.Concat(
            "{\"sessionId\":\"", harnessSession, "\",\"isSidechain\":", sidechain ? "true" : "false",
            ",\"message\":{\"model\":\"claude-opus-5\",\"usage\":{",
            "\"input_tokens\":", inputTokens.ToString(),
            ",\"output_tokens\":", outputTokens.ToString(),
            ",\"cache_creation_input_tokens\":10,\"cache_read_input_tokens\":1000}}}");
        // A row with no usage object at all, which every transcript carries: it is skipped rather
        // than counted as an unreadable one.
        var summary = string.Concat("{\"sessionId\":\"", harnessSession, "\",\"type\":\"summary\"}");
        return string.Join('\n', [row(false), summary, row(true), row(false)]) + "\n";
    }

    // A command nothing documents is a command nobody runs, which is why K7 asks for this at all.
    [Fact]
    public async Task TheHelpTextNamesRetrospectiveBuild()
    {
        var output = new StringWriter();
        var exit = await Create(output, TextWriter.Null).RunAsync(["--help"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("retrospective build", output.ToString(), StringComparison.Ordinal);
    }

    // Recursive, because a judgement would arrive as a nested key rather than a top-level one, and
    // the printed document is the only place a reader would meet it.
    private static void AssertNoJudgementKey(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Assert.DoesNotContain(
                        JudgementWords,
                        word => property.Name.Contains(word, StringComparison.OrdinalIgnoreCase));
                    AssertNoJudgementKey(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AssertNoJudgementKey(item);
                }

                break;
            default:
                break;
        }
    }

    // The forward walk to Archive, through the CLI rather than through the command handler, because
    // what is under test is the surface. It mirrors the walk in CliApplicationTests: the lesson mark
    // first, because archiving mints only from marked sources, then the stages in order with the
    // records each arm asks for.
    private static async Task<int[]> ArchiveATaskAsync(CliApplication application, string[] common)
    {
        var exits = new List<int>
        {
            await application.RunAsync(
                ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None),
            await application.RunAsync(
                ["claim", "add", .. common, "--id", "C1", "--statement", "Retries stop after three attempts"],
                CancellationToken.None),
            await application.RunAsync(
                ["evidence", "add", .. common, "--id", "E1", "--source-type", "test-run",
                 "--citation", "RetryTests.Bounded", "--summary", "The retry policy stopped after three attempts",
                 "--supports", "C1"], CancellationToken.None),
            await application.RunAsync(
                ["claim", "resolve", .. common, "--id", "C1", "--status", "validated", "--evidence", "E1"],
                CancellationToken.None),
            await application.RunAsync(
                ["lesson", "mark", .. common, "--kind", "validated-claim", "--source", "C1",
                 "--class", "untested", "--repo", "AILedger", "--tag", "retry",
                 "--verify", "dotnet test --filter RetryTests.Bounded",
                 "--do-not", "Do not assume retries are bounded without rerunning the test",
                 "--lesson-actor", "verifier", "--verify-expects", "present"], CancellationToken.None),
            // Entering Research asks for something left to research, and leaving it asks what the
            // research produced.
            await application.RunAsync(
                ["claim", "add", .. common, "--id", "C2", "--statement", "The research topic remains open"],
                CancellationToken.None),
            await application.RunAsync(
                ["alternative", "record", .. common, "--id", "ALT1", "--statement", "Skip bounded retries",
                 "--rejected-because", "The validated claim requires a bounded policy"], CancellationToken.None),
            await application.RunAsync(
                ["actor", "attach", .. common, "--target", "researcher", "--role", "researcher"],
                CancellationToken.None),
            await application.RunAsync(
                ["actor", "attach", .. common, "--target", "worker", "--role", "worker"],
                CancellationToken.None),
            await application.RunAsync(
                ["actor", "attach", .. common, "--target", "verifier", "--role", "verifier"],
                CancellationToken.None),
            await application.RunAsync(
                ["actor", "attach", .. common, "--target", "reviewer", "--role", "code-reviewer"],
                CancellationToken.None),
            // Adding work is refused until the acting actor has been briefed.
            await BriefAsync(common),
            await application.RunAsync(
                ["work", "add", .. common, "--id", "W1", "--title", "The work", "--owner", "worker"],
                CancellationToken.None)
        };

        exits.AddRange(await RecordExecutionArtifactsAsync(application, common, "W1"));
        exits.Add(await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research"], CancellationToken.None));
        exits.AddRange(await RunAPassAsync(application, common, "researcher", "R-research", null, null));
        foreach (var stage in new[] { "design", "scope", "ready", "execution" })
        {
            exits.Add(await application.RunAsync(
                ["stage", "transition", .. common, "--stage", stage], CancellationToken.None));
        }

        exits.AddRange(await RunAPassAsync(application, common, "worker", "R-work", "W1", null));
        exits.Add(await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "verification"], CancellationToken.None));
        exits.AddRange(await RunAPassAsync(
            application, common, "verifier", "R-verify", "W1",
            ("A-verify", "verifier-output", ArtifactCommands.VerifierBody)));
        exits.Add(await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "review"], CancellationToken.None));
        exits.AddRange(await RunAPassAsync(
            application, common, "reviewer", "R-review", "W1",
            ("A-review", "code-review-output", "Review findings")));
        exits.Add(await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "learn"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "archive"], CancellationToken.None));
        return [.. exits];
    }

    // One dispatched run, closed. A verifier or code reviewer cannot close without the output
    // artifact its run produced, so the pass files that finding while the run is still active.
    private static async Task<int[]> RunAPassAsync(
        CliApplication application,
        string[] common,
        string subject,
        string run,
        string? work,
        (string Id, string Kind, string Body)? finding)
    {
        string[] scope = work is null ? [] : ["--work", work];
        var exits = new List<int>
        {
            await application.RunAsync(
                ["run", "start", .. common, "--subject", subject, "--run", run, .. scope,
                 "--provider", "codex", "--session", $"session-{run}"], CancellationToken.None)
        };
        if (finding is { } output)
        {
            exits.Add(await RecordArtifactAsync(
                application, common, subject, output.Id, output.Kind, work, run, output.Body));
        }

        exits.Add(await application.RunAsync(
            ["run", "complete", .. common, "--run", run, "--status", "completed",
             "--session", $"session-{run}"], CancellationToken.None));
        return [.. exits];
    }

    private static async Task<int[]> RecordExecutionArtifactsAsync(
        CliApplication application,
        string[] common,
        string workItemId)
    {
        var exits = new List<int>
        {
            await RecordArtifactAsync(
                application, common, "operator", "A-request", "user-request", null, null,
                "The governed request")
        };
        exits.Add(await application.RunAsync(
            ["run", "start", .. common, "--subject", "operator", "--run", "R-artifacts",
             "--work", workItemId, "--provider", "codex", "--session", "artifact-session"],
            CancellationToken.None));
        exits.Add(await RecordArtifactAsync(
            application, common, "operator", "A-contract", "prompt-contract", null, "R-artifacts",
            ArtifactCommands.Body));
        exits.Add(await RecordArtifactAsync(
            application, common, "operator", "A-plan", "orchestration-plan", null, "R-artifacts",
            ArtifactCommands.PlanBody));
        exits.Add(await application.RunAsync(
            ["run", "complete", .. common, "--run", "R-artifacts", "--status", "completed",
             "--session", "artifact-session"], CancellationToken.None));
        return [.. exits];
    }

    private static async Task<int> RecordArtifactAsync(
        CliApplication application,
        string[] common,
        string actor,
        string artifactId,
        string kind,
        string? work,
        string? run,
        string body)
    {
        string[] scope = work is null ? [] : ["--work", work];
        string[] producer = run is null ? [] : ["--run", run];
        using var input = new StandardInput(body);
        return await application.RunAsync(
            ["artifact", "record", .. common, "--actor", actor, "--id", artifactId, "--kind", kind,
             "--title", "Governed document", "--body-stdin", .. scope, .. producer],
            CancellationToken.None);
    }

    // Read through the service rather than off the printed report, so the version the assertions
    // compare against is not the one under test.
    private static async Task<long> VersionAsync(string root)
    {
        var state = await Service(root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        return state!.Version;
    }

    private static string EventLogPath(string root) =>
        Path.Combine(new TaskWorkspacePathResolver(root).Resolve(new TaskId("T1")), "events.jsonl");

    private static string[] Common(string root) =>
        ["--root", root, "--task", "T1", "--actor", "operator"];

    // Returns zero so it can sit in the exits list beside the commands it precedes; the helper
    // throws rather than returning a code when the brief itself is refused.
    private static async Task<int> BriefAsync(string[] common)
    {
        await ContextBrief.BuildAsync(common[1], common[3], common[5]);
        return 0;
    }

    // No lesson store, so recall reaches this root's own archived tasks and nothing else. A test
    // that recalled from the operator's real store would report a lesson debt it did not create.
    //
    // The harness transcript root is the same idea one directory over. A test that left it at the
    // per-user default would be asserting against the operator's own conversations, so each test
    // that reads a transcript stands up a harness directory of its own and names it here.
    private static CliApplication Create(
        TextWriter output,
        TextWriter error,
        string? harnessTranscriptRoot = null) => new(
        output,
        error,
        Service,
        _ => throw new InvalidOperationException("These tests launch no provider."),
        new ContextAssembler())
    {
        HarnessTranscriptRoot = harnessTranscriptRoot ?? "/harness-transcripts-no-test-writes-here"
    };

    // Where the harness writes, as a test sees it: one directory per project slug, holding
    // `<session-id>.jsonl`. Created under the test's own temporary root so nothing here can read or
    // write the operator's real one.
    private static string HarnessDirectory(string root)
    {
        var directory = Path.Combine(root, "harness", "projects", "-a-project");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string HarnessRoot(string root) => Path.Combine(root, "harness", "projects");

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }
}
