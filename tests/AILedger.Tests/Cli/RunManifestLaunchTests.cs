using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace AILedger.Tests.Cli;

// The manifest record is produced in the CLI, not in the kernel: RunManifestRecordTests pins the
// rule that accepts the pair, and these pin the launcher that fills it. Both are needed. A hash in
// the wrong case or over a re-serialisation would satisfy every kernel test and refuse, or
// misdescribe, every real launch.
public sealed class RunManifestLaunchTests
{
    [Fact]
    public async Task LaunchRecordsTheHashOfTheExactBriefHandedToTheChild()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        var adapter = new CapturingAdapter(AgentRunStatus.Completed);
        var application = new CliApplication(
            TextWriter.Null, TextWriter.Null, Service, _ => adapter, new ContextAssembler());
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", work, "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var run = state!.Runs[new RunId("R1")];
        // The brief the child was actually handed, taken from the launch request rather than
        // rebuilt: a hash over anything else names a document nobody read.
        var delivered = adapter.Request!.StandardInput;
        Assert.Equal(0, exit);
        Assert.Equal(HashOf(delivered), run.ManifestHash);
        Assert.Equal(JsonNode.Parse(delivered)!["artifacts"]!.AsArray().Count, run.ManifestArtifactCount);
    }

    // The absence is the measurement, and only the production path can show that it survives: the
    // cognitive root is read while the manifest is built, which is after the run has started. So the
    // run exists, the child never ran, and the pair must stay null rather than record a brief.
    [Fact]
    public async Task ALaunchThatFailsBeforeItsBriefExistsRecordsNoManifest()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        using var emptyCognitiveRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        var adapter = new CapturingAdapter(AgentRunStatus.Completed);
        var application = new CliApplication(
            TextWriter.Null, TextWriter.Null, Service, _ => adapter, new ContextAssembler());
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None);

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", work, "--cognitive-root", emptyCognitiveRoot.Path],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var run = state!.Runs[new RunId("R1")];
        Assert.Equal(1, exit);
        Assert.Null(adapter.Request);
        Assert.Equal(AgentRunStatus.Failed, run.Status);
        Assert.Null(run.ManifestHash);
        Assert.Null(run.ManifestArtifactCount);
    }

    private static string HashOf(string manifestJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifestJson))).ToLowerInvariant();

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
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

    // RV1's finding, as its own production-path test. The manifest is built, then the launch
    // request is constructed — and constructing it parses --timeout-seconds, which throws on a bad
    // value. Marking the brief delivered before that point recorded a manifest for a run the adapter
    // never received, which inverts what the absence is meant to mean.
    //
    // Asserts on the adapter as well as the ledger: the point is not that the run failed, it is that
    // nothing was handed over and the record agrees.
    [Fact]
    public async Task ALaunchThatFailsWhileBuildingItsRequestRecordsNoManifest()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        var adapter = new CapturingAdapter(AgentRunStatus.Completed);
        var application = new CliApplication(
            TextWriter.Null, TextWriter.Null, Service, _ => adapter, new ContextAssembler());
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None);

        // The manifest builds fine; the request does not. Zero is refused by PositiveInt, which runs
        // inside the AgentLaunchRequest constructor call.
        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", work, "--cognitive-root", FindCognitiveRoot(),
             "--timeout-seconds", "0"],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var run = state!.Runs[new RunId("R1")];
        Assert.NotEqual(0, exit);
        Assert.Null(adapter.Request);
        Assert.Equal(AgentRunStatus.Failed, run.Status);
        Assert.Null(run.ManifestHash);
        Assert.Null(run.ManifestArtifactCount);
    }

    private sealed class CapturingAdapter(AgentRunStatus status) : IAgentAdapter
    {
        public AgentLaunchRequest? Request { get; private set; }

        public string Provider => "codex";

        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult("test");

        public Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new AgentRunResult(
                request.RunId, Provider, "session-1", status,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1),
                0, null, [], string.Empty, "test", [], false, "failed"));
        }
    }
}
