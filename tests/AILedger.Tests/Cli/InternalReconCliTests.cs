using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Cli;

[Collection(StandardInput.Collection)]
public sealed class InternalReconCliTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TemplateRecordShowRoundTrip(bool malformed)
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var cli = new CliApplication(output, error, Service,
            _ => throw new InvalidOperationException("No provider dispatch in CLI contract test"), new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        async Task Run(params string[] args)
        {
            Assert.Equal(0, await cli.RunAsync([.. args, .. common], CancellationToken.None));
        }
        await Run("task", "open", "--title", "Recon", "--goal", "Test");
        await Run("claim", "add", "--id", "C1", "--statement", "Unicode λ and mixed CASE");
        await Run("stage", "transition", "--stage", "Research");
        await Run("run", "start", "--run", "R1", "--provider", "codex");
        // A1 (recon-consultation-arm): the producer run consults before it files.
        await Run("lesson", "consult", "--run", "R1", "--purpose", "recon",
            "--question", "What do earlier tasks say about this recon?", "--tag", "recon");
        output.GetStringBuilder().Clear();
        var stateBefore = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, await cli.RunAsync(["artifact", "recon-template", "--root", root.Path, "--task", "T1"], CancellationToken.None));
        var doc = JsonNode.Parse(output.ToString())!.AsObject();
        Assert.Equal(5, doc.Count);
        Assert.Null(doc["assessments"]![0]!["domain"]);
        Assert.Equal(InternalReconDocuments.ComputeClaimSetHash(stateBefore!), doc["claimSetHash"]!.GetValue<string>());
        Assert.Equal(stateBefore!.Version, (await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None))!.Version);
        doc["assessments"]![0]!["domain"] = "internal";
        doc["report"] = "# Recon\n  Preserve this report λ\n";
        var body = malformed ? doc.ToJsonString() + "{}" : doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        int exit;
        using (new StandardInput(body))
            exit = await cli.RunAsync(["artifact", "record", .. common, "--id", "A1", "--kind", "InternalRecon",
                "--title", "Recon", "--run", "R1", "--body-stdin"], CancellationToken.None);
        if (malformed)
        {
            Assert.NotEqual(0, exit);
            Assert.Contains("InternalRecon", error.ToString());
            Assert.Empty((await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None))!.Artifacts);
            return;
        }
        Assert.Equal(0, exit);
        output.GetStringBuilder().Clear();
        await Run("artifact", "show", "--id", "A1", "--json");
        using var shown = JsonDocument.Parse(output.ToString());
        Assert.Equal(body, shown.RootElement.GetProperty("content").GetString());
        File.Delete(Path.Combine(root.Path, "T1", "state.json"));
        var replayed = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(body, replayed!.Artifacts[new ArtifactId("A1")].Content);
    }

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }
}
