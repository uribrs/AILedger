using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Cli;

// The provider-launch site is the journal's second write site, and it is not a convenience: the
// authority-and-scope refusals it records are decided in the CLI before run.start, by design, so no
// command reaches the service and the service site cannot see them (C11). Three of the operator's own
// saved lessons were learned by hitting that one method.
//
// Every test drives the real CliApplication end to end, because the question is what a refused launch
// writes — not what a re-reading of the gate would say (lesson status-owed:LC11). Neither absence
// assertion stands alone: each one is paired, in its own test, with a refusal against the same task
// that proves the write path is alive. An empty journal is also what a completely dead site leaves.
public sealed class RefusalJournalLaunchTests
{
    // Contract test 4. The refusal lands before the run begins, so the journal is the only record
    // that the attempt happened at all: there is no run to carry a Failed status.
    [Fact]
    public async Task ALaunchRefusedForAuthorityAppendsOneRowAndStartsNoRun()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        var application = Application(new CapturingAdapter());
        await OpenTaskWithAWorkerAsync(application, root.Path);
        var versionBeforeTheAttempt =
            (await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None))!.Version;

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "impl",
             "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", work, "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        var row = Assert.Single(ReadJournal(root.Path));
        Assert.Equal(1, exit);
        Assert.Equal("impl", row.ActorId);
        Assert.Equal("provider launch", row.Command);
        Assert.Equal("provider-launch", row.Site);
        Assert.Equal(
            "Only an operator may launch a provider without governed work scope.", row.Message);
        Assert.Equal(versionBeforeTheAttempt, row.TaskVersion);
        // The C11 guard: the refusal was decided before any command was submitted, so the event log
        // holds no run at all and the journal is the only place this attempt exists.
        Assert.DoesNotContain("run.started", await File.ReadAllTextAsync(EventLogPath(root.Path)));
    }

    // Contract test 5. A malformed command line is not a gate: it exits 2 where a governance refusal
    // exits 1, and journalling it would fill the file with typing mistakes. The bad timeout is parsed
    // inside the launch request construction, which is after run.start — so this exercises the same
    // catch block the site-2b row is written from, and the row must not appear there.
    [Fact]
    public async Task AMalformedTimeoutAppendsNoRowWhileARefusalInTheSameTaskStillDoes()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        var adapter = new CapturingAdapter();
        var application = Application(adapter);
        await OpenTaskWithAWorkerAsync(application, root.Path);

        var usageExit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", work, "--cognitive-root", FindCognitiveRoot(),
             "--timeout-seconds", "0"],
            CancellationToken.None);

        Assert.Equal(2, usageExit);
        Assert.Null(adapter.Request);
        Assert.Empty(ReadJournal(root.Path));

        // Without this the assertion above would pass against a launch site that journals nothing at
        // all. A refused launch against the same task, in the same test, is what makes the emptiness
        // above evidence of an exclusion rather than of a dead site.
        var refusedExit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "impl",
             "--run", "R2", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", work, "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        Assert.Equal(1, refusedExit);
        Assert.Equal("provider launch", Assert.Single(ReadJournal(root.Path)).Command);
    }

    // Contract test 6. The other half of the launch site: a refusal raised while the manifest is
    // built, which is after run.start. Only the cleanup command reaches the service, so without this
    // row the refusal itself is invisible and the log shows nothing but a run that failed.
    [Fact]
    public async Task ARefusalWhileBriefingAppendsOneRowAndStillClosesTheRunFailed()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        var adapter = new CapturingAdapter();
        var application = Application(adapter);
        await OpenTaskWithAWorkerAsync(application, root.Path);
        // A worker that cannot build context. The manifest is filtered by the subject's role, so the
        // capability the subject lacks is refused where the brief is assembled — after the run has
        // started and inside the block that closes it.
        await application.RunAsync(
            ["actor", "attach", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--target", "briefless", "--role", "worker", "--capability", "AddClaim"],
            CancellationToken.None);
        var versionBeforeTheAttempt =
            (await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None))!.Version;

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--subject", "briefless", "--run", "R1", "--provider", "codex",
             "--executable", "/usr/bin/true", "--working-directory", work,
             "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var run = state!.Runs[new RunId("R1")];
        var row = Assert.Single(ReadJournal(root.Path));
        Assert.Equal(1, exit);
        Assert.Null(adapter.Request);
        Assert.Equal("operator", row.ActorId);
        Assert.Equal("provider launch", row.Command);
        Assert.Equal("provider-launch", row.Site);
        Assert.Equal("Actor 'briefless' lacks capability 'BuildContext'.", row.Message);
        // The existing handling proceeds unchanged: the run this launch started is still closed.
        Assert.Equal(AgentRunStatus.Failed, run.Status);
        // The version the refused attempt was made against, which here is after run.started — one
        // event — and before the completion that closed it. That is the difference from site 2a, and
        // asserting it is what stops the two sites from silently sharing one stale number.
        Assert.Equal(versionBeforeTheAttempt + 1, row.TaskVersion);
        Assert.True(row.TaskVersion < state.Version);
    }

    // Open the task and attach one non-operator actor that owns nothing. That is all an
    // authority-and-scope refusal needs: 'impl' holds a role, so it is a real actor being refused
    // real authority rather than an unknown name.
    private static async Task OpenTaskWithAWorkerAsync(CliApplication application, string root)
    {
        await application.RunAsync(
            ["task", "open", "--root", root, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["actor", "attach", "--root", root, "--task", "T1", "--actor", "operator",
             "--target", "impl", "--role", "worker"], CancellationToken.None);
    }

    private static CliApplication Application(CapturingAdapter adapter) => new(
        TextWriter.Null, TextWriter.Null, Service, _ => adapter, new ContextAssembler());

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }

    private static string JournalPath(string root) => Path.Combine(root, "T1", "refusals.jsonl");

    private static string EventLogPath(string root) => Path.Combine(root, "T1", "events.jsonl");

    // Read through the property names the row is specified with, so a rename fails here instead of
    // deserializing quietly into a default.
    private static IReadOnlyList<JournalRow> ReadJournal(string root)
    {
        var path = JournalPath(root);
        if (!File.Exists(path))
        {
            return [];
        }

        return File.ReadAllLines(path).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            var element = document.RootElement;
            return new JournalRow(
                element.GetProperty("recordedAt").GetDateTimeOffset(),
                element.GetProperty("actorId").GetString()!,
                element.GetProperty("command").GetString()!,
                element.GetProperty("site").GetString()!,
                element.GetProperty("taskVersion").GetInt64(),
                element.GetProperty("message").GetString()!);
        }).ToArray();
    }

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

    private sealed record JournalRow(
        DateTimeOffset RecordedAt,
        string ActorId,
        string Command,
        string Site,
        long TaskVersion,
        string Message);

    // Records whether the adapter was reached at all. Every refusal here must land before that
    // point, and a test that only read the exit code could not tell the difference.
    private sealed class CapturingAdapter : IAgentAdapter
    {
        public AgentLaunchRequest? Request { get; private set; }

        public string Provider => "codex";

        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult("test");

        public Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new AgentRunResult(
                request.RunId, Provider, "session-1", AgentRunStatus.Completed,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1),
                0, null, [], string.Empty, "test", [], false, "failed"));
        }
    }
}
