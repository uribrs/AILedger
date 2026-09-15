using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.ContextBriefing;

internal static class ContextBuildTestHost
{
    internal static async Task<ContextManifest> BuildAsync(
        string root,
        string taskId,
        string actorId)
    {
        var application = Application(root);
        Assert.Equal(0, await application.RunAsync(
            ["task", "open", "--root", root, "--task", taskId, "--actor", actorId,
             "--title", "Task", "--goal", "Goal"], CancellationToken.None));

        var manifestPath = Path.Combine(Path.GetTempPath(), $"manifest-{Guid.NewGuid():N}.json");
        Assert.Equal(0, await application.RunAsync(
            ["context", "build", "--root", root, "--task", taskId, "--actor", actorId,
             "--cognitive-root", ContextBrief.CognitiveRoot(), "--output", manifestPath],
            CancellationToken.None));
        var manifest = JsonSerializer.Deserialize<ContextManifest>(
            await File.ReadAllTextAsync(manifestPath), LedgerJson.CreateOptions())!;
        File.Delete(manifestPath);
        return manifest;
    }

    internal static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(
            root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }

    private static CliApplication Application(string root) =>
        new(
            TextWriter.Null,
            TextWriter.Null,
            _ => Service(root),
            _ => throw new InvalidOperationException("No provider is launched here."),
            new ContextAssembler());
}
