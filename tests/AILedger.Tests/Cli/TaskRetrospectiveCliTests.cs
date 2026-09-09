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
                 "--lesson-actor", "verifier"], CancellationToken.None),
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

    // No lesson store, so recall reaches this root's own archived tasks and nothing else. A test
    // that recalled from the operator's real store would report a lesson debt it did not create.
    private static CliApplication Create(TextWriter output, TextWriter error) => new(
        output,
        error,
        Service,
        _ => throw new InvalidOperationException("These tests launch no provider."),
        new ContextAssembler());

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }
}
