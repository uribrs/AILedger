using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Core.Application;
using AILedger.Core.Domain;
using AILedger.Core.Artifacts;
using AILedger.Core.Handoffs;
using AILedger.Core.Inspection;
using AILedger.Storage;
using AILedger.Tests.Support;
using AILedger.TestSupport;

namespace AILedger.Tests.Handoffs;

public sealed class FrozenHandoffTests
{
    private static string Fixtures => Path.Combine(RepositoryLayout.Root, "tests", "Fixtures", "bounded-handoffs-v1");

    [Theory]
    [InlineData("axonius")] [InlineData("falcon")] [InlineData("s3")] [InlineData("current-reanchor")]
    public async Task FrozenCutoffsRetainMaterialAndRetrievableOmissionsWithoutLaterFindings(string name)
    {
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Fixtures, "manifest.json")))!.AsArray();
        var entry = manifest.Single(e => e!["case"]!.GetValue<string>() == name)!;
        var text = await File.ReadAllTextAsync(Path.Combine(Fixtures, name + ".jsonl"));
        Assert.Equal(entry["sha256"]!.GetValue<string>(), ArtifactSubmissionIdentity.ContentHash(text));
        Assert.Equal(entry["cutoff"]!.GetValue<int>(), text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        using var root = new TemporaryDirectory();
        var taskId = entry["task_id"]!.GetValue<string>();
        var directory = Path.Combine(root.Path, taskId); Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "events.jsonl"), text);
        var service = new FileGovernedTaskService(root.Path, new CommandHandler(), new TaskReducer());
        var preparer = new HandoffPreparer(service);
        var binding = new InspectionBinding(new(taskId), new("operator"), null, "fixture",
            AllowInspect: true, AllowRunless: true);
        var index = await preparer.IndexAsync(binding, root.Path, "task", null, default);
        var curation = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Fixtures, name + "-curation.json")))!;
        var included = curation["included_keys"]!.Deserialize<string[]>()!.ToHashSet();
        var omitted = curation["omitted"]!.AsObject();
        Assert.Equal(index.Records.Count, included.Count + omitted.Count);
        var request = HandoffTests.Request(index) with
        {
            Spec = curation["spec"]!.Deserialize<EpisodeSpec>(HandoffJson.Options)!,
            Inputs = index.Records.Select(r => Select(r, included, omitted)).ToArray(),
            Preservation = curation["preservation"]!.Deserialize<PreservationCheck[]>(HandoffJson.Options)!,
            Sources = curation["sources"]!.Deserialize<PinnedSource[]>(HandoffJson.Options)!
        };
        var before = await Inspection.InspectionTests.Files(directory);
        var prepared = await preparer.PrepareAsync(binding, request, default);
        var package = JsonSerializer.Deserialize<HandoffPackage>(prepared.PackageJson, HandoffJson.Options)!;
        var evaluation = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Fixtures, "evaluation.json")))![name]!;
        foreach (var key in evaluation["required_keys"]!.Deserialize<string[]>()!)
        {
            if (key.StartsWith("source:", StringComparison.Ordinal))
                Assert.Contains(package.Sources, s => "source:" + s.Key == key && s.Material && s.Content is not null);
            else
            {
                Assert.Contains(package.Inputs, i => i.Key == key && i.RecordJson is not null && i.Selection.Material);
                var incomplete = request with { Inputs = request.Inputs.Select(i => i.Kind + ":" + i.Id == key
                    ? i with { Include = false } : i).ToArray() };
                await Assert.ThrowsAsync<ArgumentException>(() => preparer.PrepareAsync(binding, incomplete, default));
            }
        }
        foreach (var key in evaluation["withheld_keys"]!.Deserialize<string[]>()!)
            Assert.DoesNotContain(package.Inputs, i => i.Key == key);
        var omission = package.Inputs.First(i => i.RecordJson is null);
        var retrieved = await service.RetrieveAsync(binding, omission.Reference.Retrieve, default);
        Assert.Equal("ok", retrieved.Status); Assert.Equal(omission.Reference.Sha256, retrieved.Sha256);
        Assert.Null(package.Measurements.ObservedAdditionalReads); // This is a test retrieval, not a client trial.
        Assert.Equal(before, await Inspection.InspectionTests.Files(directory));
        if (name == "current-reanchor")
        {
            Assert.Contains("\"status\":\"proposed\"", package.Inputs.Single(i => i.Key == "Decision:RPD1").RecordJson);
            Assert.Contains("\"status\":\"open\"", package.Inputs.Single(i => i.Key == "Claim:C1").RecordJson);
        }
    }

    private static InputSelection Select(ContextReference reference, HashSet<string> included, JsonObject omitted)
    {
        var key = reference.Kind + ":" + reference.Id;
        var include = included.Contains(key);
        var reason = include ? "Needed for this bounded technical question." : omitted[key]!["reason"]!.GetValue<string>();
        return new(reference.Kind, reference.Id, reference.Sha256, include, include, reason,
            include ? "Would lose relevant evidence." : omitted[key]!["risk"]!.GetValue<string>(),
            include ? "Before reliance." : omitted[key]!["retrieve_when"]!.GetValue<string>());
    }
}
