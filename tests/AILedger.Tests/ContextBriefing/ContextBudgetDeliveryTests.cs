using System.Text;
using System.Text.Json;
using AILedger.Cli;
using AILedger.Cli.ContextBriefing;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Storage;
using AILedger.Tests.Support;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.ContextBriefing;

public sealed class ContextBudgetDeliveryTests
{
    [Fact]
    public async Task OversizedContextWritesNeitherAnOutputFileNorABriefEvent()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = new CliApplication(TextWriter.Null, error, Service,
            _ => throw new InvalidOperationException("No provider expected"), new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        Assert.Equal(0, await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None));
        var output = Path.Combine(root.Path, "manifest.json");

        Assert.Equal(2, await application.RunAsync(
            ["context", "build", .. common, "--cognitive-root", ContextBrief.CognitiveRoot(),
             "--max-context-bytes", "1", "--output", output], CancellationToken.None));

        Assert.Contains("Required context", error.ToString());
        Assert.False(File.Exists(output));
        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Empty(state!.ContextBuilds);

        Assert.Equal(0, await application.RunAsync(
            ["context", "build", .. common, "--cognitive-root", ContextBrief.CognitiveRoot(),
             "--max-context-bytes", "524288", "--output", output], CancellationToken.None));
        var bytes = await File.ReadAllBytesAsync(output);
        var manifest = JsonSerializer.Deserialize<ContextManifest>(bytes, LedgerJson.CreateOptions())!;
        Assert.Equal(524288, manifest.Budget!.MaximumBytes);
        Assert.True(bytes.Length <= manifest.Budget.MaximumBytes);
    }

    [Theory]
    [InlineData("launch")]
    [InlineData("resume")]
    public async Task ProviderReceivesBoundedJsonAndNeverRunsWithAnOversizedManifest(string command)
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(providerRoot.Path, ".git"));
        var capture = new CapturingAdapter();
        var error = new StringWriter();
        var application = new CliApplication(TextWriter.Null, error, Service, _ => capture, new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        Assert.Equal(0, await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None));
        await ContextBrief.BuildAsync(root.Path, "T1");
        Assert.Equal(0, await application.RunAsync(
            ["actor", "attach", .. common, "--target", "coordinator", "--role", "implementation-lead"], CancellationToken.None));
        string[] launch = ["provider", command, .. common, "--subject", "coordinator", "--provider", "codex",
            "--executable", "/usr/bin/true", "--working-directory", providerRoot.Path,
            "--cognitive-root", ContextBrief.CognitiveRoot()];
        if (command == "resume") launch = [.. launch, "--session", "session-1"];

        var failed = await application.RunAsync([.. launch, "--run", "R1", "--max-context-bytes", "1"], CancellationToken.None);
        Assert.NotEqual(0, failed);
        Assert.Contains("Required context", error.ToString());
        Assert.Empty(capture.Requests);
        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(AgentRunStatus.Failed, state!.Runs[new RunId("R1")].Status);

        var success = await application.RunAsync([.. launch, "--run", "R2"], CancellationToken.None);
        Assert.True(success == 0, error.ToString());
        var request = Assert.Single(capture.Requests);
        var manifest = JsonSerializer.Deserialize<ContextManifest>(request.StandardInput, LedgerJson.CreateOptions())!;
        Assert.Equal(ContextManifestBudget.DefaultMaximumBytes, manifest.Budget!.MaximumBytes);
        Assert.True(Encoding.UTF8.GetByteCount(request.StandardInput) <= manifest.Budget.MaximumBytes);
    }
}
