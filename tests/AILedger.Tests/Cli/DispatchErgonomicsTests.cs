using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Tests.Support;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Cli;

[Collection(StandardInput.Collection)]
public sealed class DispatchErgonomicsTests
{
    [Theory]
    [InlineData(AgentRunStatus.Completed, 0)]
    [InlineData(AgentRunStatus.Failed, 3)]
    public async Task SummaryRetainsDetailsAndSeparatesLedgerOutcome(AgentRunStatus status, int expectedExit)
    {
        using var root = new TemporaryDirectory();
        using var work = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var app = new CliApplication(output, error, Service, _ => new LargeResultAdapter(status), new ContextAssembler());
        await OpenAsync(app, root.Path);
        await ContextBrief.BuildAsync(root.Path, "T1");
        output.GetStringBuilder().Clear();
        var exit = await app.RunAsync(Launch(root.Path, work.Path), CancellationToken.None);

        Assert.Equal(expectedExit, exit);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(status.ToString(), json.RootElement.GetProperty("ledgerStatus").GetString());
        Assert.True(json.RootElement.TryGetProperty("nextActionContract", out _));
        Assert.False(json.RootElement.TryGetProperty("events", out _));
        var retained = json.RootElement.GetProperty("retention").GetProperty("path").GetString()!;
        Assert.Contains(new string('x', 100_000), await File.ReadAllTextAsync(retained));
        Assert.Equal(status, (await Service(root.Path).GetStateAsync(new("T1"), CancellationToken.None))!.Runs[new("R1")].Status);
        if (status == AgentRunStatus.Failed)
            Assert.Contains("Run R1: ledger=Failed;", error.ToString());
    }

    [Fact]
    public async Task SummaryDoesNotTurnProviderSuccessIntoUnrecordedLedgerCompletion()
    {
        using var root = new TemporaryDirectory();
        using var work = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var service = new CompletionFailureService(Service(root.Path), int.MaxValue);
        var app = new CliApplication(output, error, _ => service,
            _ => new LargeResultAdapter(AgentRunStatus.Completed), new ContextAssembler());
        await OpenAsync(app, root.Path);
        await ContextBrief.BuildAsync(root.Path, "T1");
        output.GetStringBuilder().Clear();
        Assert.Equal(1, await app.RunAsync(Launch(root.Path, work.Path), CancellationToken.None));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("Unconfirmed", json.RootElement.GetProperty("ledgerStatus").GetString());
        Assert.Equal("completed", json.RootElement.GetProperty("providerStatus").GetString());
        Assert.Equal("unknown", json.RootElement.GetProperty("completionRecording").GetString());
        Assert.Equal(AgentRunStatus.Active, (await Service(root.Path).GetStateAsync(new("T1"), CancellationToken.None))!.Runs[new("R1")].Status);
        Assert.Contains("retained provider result path", error.ToString());
    }

    [Fact]
    public async Task SummaryDoesNotWaiveMissingBriefOrAdmitRun()
    {
        using var root = new TemporaryDirectory();
        using var work = new TemporaryDirectory();
        var output = new StringWriter();
        var capture = new CapturingAdapter();
        var app = new CliApplication(output, TextWriter.Null, Service, _ => capture, new ContextAssembler());
        await OpenAsync(app, root.Path);
        output.GetStringBuilder().Clear();
        Assert.Equal(1, await app.RunAsync(Launch(root.Path, work.Path), CancellationToken.None));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("NotStarted", json.RootElement.GetProperty("ledgerStatus").GetString());
        Assert.Contains("context", json.RootElement.GetProperty("failure").GetProperty("diagnostic").GetString());
        Assert.Empty(capture.Requests);
        Assert.Empty((await Service(root.Path).GetStateAsync(new("T1"), CancellationToken.None))!.Runs);
    }

    [Fact]
    public async Task SummaryFallsBackToFullResultWhenRetentionFails()
    {
        using var root = new TemporaryDirectory();
        using var work = new TemporaryDirectory();
        var output = new StringWriter();
        var app = new CliApplication(output, TextWriter.Null, Service,
            _ => new LargeResultAdapter(AgentRunStatus.Completed), new ContextAssembler());
        await OpenAsync(app, root.Path);
        await ContextBrief.BuildAsync(root.Path, "T1");
        Directory.CreateDirectory(Path.Combine(root.Path, "T1", "runs", "R1.json"));
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await app.RunAsync(Launch(root.Path, work.Path), CancellationToken.None));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(100_000, json.RootElement.GetProperty("finalOutput").GetString()!.Length);
        Assert.True(json.RootElement.TryGetProperty("events", out _));
    }

    [Fact]
    public async Task WatchStreamsCorrelatedWritesAndStopsOnLedgerCompletionWithoutWriting()
    {
        using var root = new TemporaryDirectory();
        var app = Create(TextWriter.Null, TextWriter.Null);
        await StartAsync(app, root.Path);
        var version = (await Service(root.Path).GetStateAsync(new("T1"), CancellationToken.None))!.Version;
        var output = new ThreadSafeStringWriter();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var watch = Create(output, TextWriter.Null).RunAsync(
            ["run", "watch", "--root", root.Path, "--task", "T1", "--run", "R1", "--since", version.ToString(), "--json"], deadline.Token);
        await WaitUntilAsync(() => output.GetText().Contains("active"), "Initial status missing");
        foreach (var (id, correlation) in new[] { ("C1", "R1"), ("C2", "unrelated") })
            Assert.Equal(0, await app.RunAsync(["claim", "add", "--root", root.Path, "--task", "T1",
                "--actor", "operator", "--id", id, "--statement", "Observation", "--correlation", correlation], CancellationToken.None));
        Assert.Equal(0, await app.RunAsync(["run", "complete", "--root", root.Path, "--task", "T1",
            "--actor", "operator", "--run", "R1", "--status", "completed"], CancellationToken.None));
        var closedVersion = (await Service(root.Path).GetStateAsync(new("T1"), CancellationToken.None))!.Version;
        Assert.Equal(0, await watch);
        var rows = output.GetText().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement).ToArray();
        Assert.Single(rows.Where(row => row.TryGetProperty("eventType", out var type) && type.GetString() == "ClaimAdded"));
        Assert.DoesNotContain(rows, row => row.TryGetProperty("eventType", out var type) && type.GetString() == "RunStarted");
        Assert.Equal("completed", rows[^1].GetProperty("status").GetString());
        Assert.Equal(closedVersion, (await Service(root.Path).GetStateAsync(new("T1"), CancellationToken.None))!.Version);
    }

    [Fact]
    public async Task CancellingWatcherDoesNotCancelRun()
    {
        using var root = new TemporaryDirectory();
        await StartAsync(Create(TextWriter.Null, TextWriter.Null), root.Path);
        var before = (await Service(root.Path).GetStateAsync(new("T1"), CancellationToken.None))!.Version;
        var output = new ThreadSafeStringWriter();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var watch = Create(output, TextWriter.Null).RunAsync(
            ["run", "watch", "--root", root.Path, "--task", "T1", "--run", "R1"], cancellation.Token);
        await WaitUntilAsync(() => output.GetText().Contains("ledger status Active"), "Initial status missing");
        cancellation.Cancel();
        Assert.Equal(130, await watch);
        var state = (await Service(root.Path).GetStateAsync(new("T1"), CancellationToken.None))!;
        Assert.Equal(AgentRunStatus.Active, state.Runs[new("R1")].Status);
        Assert.Equal(before, state.Version);
    }

    private static async Task OpenAsync(CliApplication app, string root) => Assert.Equal(0,
        await app.RunAsync(["task", "open", "--root", root, "--task", "T1", "--actor", "operator",
            "--title", "Ergonomics", "--goal", "Observe only"], CancellationToken.None));

    private static async Task StartAsync(CliApplication app, string root)
    {
        await OpenAsync(app, root);
        Assert.Equal(0, await app.RunAsync(["run", "start", "--root", root, "--task", "T1", "--actor", "operator",
            "--run", "R1", "--provider", "codex", "--session", "fixture"], CancellationToken.None));
    }

    private static string[] Launch(string root, string work) => ["provider", "launch", "--root", root,
        "--task", "T1", "--actor", "operator", "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
        "--working-directory", work, "--cognitive-root", FindCognitiveRoot(), "--compact"];

    private sealed class LargeResultAdapter(AgentRunStatus status) : IAgentAdapter
    {
        public string Provider => "codex";
        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) => Task.FromResult("fixture");
        public Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken) => Task.FromResult(
            new AgentRunResult(request.RunId, Provider, "fixture", status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                status == AgentRunStatus.Completed ? 0 : 1, new string('x', 100_000), [], "", "fixture", [], false,
                status == AgentRunStatus.Completed ? null : "Startup failed\nMore details"));
    }
}
