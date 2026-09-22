using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Cli;

internal static class InternalReconCliSetup
{
    public static async Task RecordAsync(CliApplication application, string[] common)
    {
        var root = common[Array.IndexOf(common, "--root") + 1];
        var id = common[Array.IndexOf(common, "--task") + 1];
        var reducer = new TaskReducer();
        var service = new FileGovernedTaskService(root,
            new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
        var state = await service.GetStateAsync(new TaskId(id), CancellationToken.None);
        var template = InternalReconDocuments.CreateTemplate(state!);
        var body = JsonSerializer.Serialize(template with
        {
            Assessments = template.Assessments.Select(a => a with { Domain = "internal" }).ToArray(),
            Report = "Local CLI lifecycle recon."
        });
        Assert.Equal(0, await application.RunAsync(
            ["run", "start", .. common, "--run", "R-recon", "--provider", "codex"], CancellationToken.None));
        using (new StandardInput(body))
            Assert.Equal(0, await application.RunAsync(
                ["artifact", "record", .. common, "--id", "A-recon", "--kind", "InternalRecon",
                 "--title", "Recon", "--run", "R-recon", "--body-stdin"], CancellationToken.None));
        Assert.Equal(0, await application.RunAsync(
            ["run", "complete", .. common, "--run", "R-recon", "--status", "completed",
             "--session", "recon-session"], CancellationToken.None));
    }
}
