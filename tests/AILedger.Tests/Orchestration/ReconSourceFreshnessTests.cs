using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Cli.Findings;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Orchestration;

public sealed class ReconSourceFreshnessTests
{
    [Fact]
    public async Task HostMeasuresSourceRefusesDriftAtFilingAndScopeButReplayNeverReadsFiles()
    {
        using var source = new TemporaryDirectory();
        var path = ReconSourceFiles.Canonicalize(Path.Combine(source.Path, "engine.cs"));
        await File.WriteAllTextAsync(path, "original source");
        using var f = await CognitiveFixture.OpenAsync(TaskStage.Design);
        await f.Ledger.ExecuteAsync(new AddClaimCommand(f.Lead, null, f.Run.Value, new("C-source"), "Source observation", "Reassess if changed"));
        await f.Ledger.ExecuteAsync(new AddEvidenceCommand(f.Lead, null, f.Run.Value, new("E-source"), "source-read", path, "Inspected source", [new("C-source")], []));
        await f.Ledger.ExecuteAsync(new RecordAlternativeCommand(f.Lead, null, f.Run.Value, new("A-source"), "Reuse unchecked facts", "Source changed", null));
        var state = await f.Ledger.StateAsync();
        await using var session = ProviderFindingsSession.Start(f.Ledger.Service(), f.Ledger.Root, f.Ledger.TaskId,
            f.Lead, f.Run, null, "codex", state.Roles[f.Lead], DateTimeOffset.UtcNow.AddMinutes(5), sourceDirectories: [source.Path]);
        using var relay = await CognitiveRelay.OpenAsync(session.Endpoint);
        var template = await relay.HandoffAsync(new { kind = "recon_template", source_paths = new[] { path } });
        Assert.Equal("observed", template.GetProperty("status").GetString());
        Assert.Equal(state.Version, (await f.Ledger.StateAsync()).Version);
        var document = JsonSerializer.Deserialize<InternalReconDocument>(template.GetProperty("reconTemplate"))!;
        var file = Assert.Single(document.SourceReview!.Files);
        document = document with
        {
            Assessments = [new("C-source", "internal")], Report = "Reassessed source against the proposed plan",
            SourceReview = new("Changed engine source; fixture has no other product files",
                [file with { ClaimIds = ["C-source"], EvidenceIds = ["E-source"], Assessment = "Current source supports the observation" }], "", [])
        };
        await relay.HandoffAsync(new { kind = "lesson_consultation", purpose = "recon", question = "Lessons?", tags = new[] { "source" }, claims = Array.Empty<string>() });
        var operation = new { kind = "governing_artifact", artifact_kind = "internal_recon", title = "Source-bound recon", markdown = JsonSerializer.Serialize(document) };
        await File.WriteAllTextAsync(path, "changed source");
        var refused = await relay.HandoffAsync(operation, "source-recon");
        Assert.Equal("refused", refused.GetProperty("status").GetString());
        Assert.Contains("source basis", refused.GetProperty("diagnostic").GetString());
        await File.WriteAllTextAsync(path, "original source");
        Assert.Equal("recorded", (await relay.HandoffAsync(operation, "source-recon")).GetProperty("status").GetString());
        Assert.Equal("recorded", (await relay.HandoffAsync(new { kind = "governing_artifact", artifact_kind = "prompt_contract", title = "Contract", markdown = ArtifactCommands.Body })).GetProperty("status").GetString());
        await f.Ledger.ExecuteAsync(new CompleteRunCommand(f.Ledger.Actor, null, "finish", f.Run, AgentRunStatus.Completed, "session"));

        File.Delete(path);
        Assert.NotEmpty((await f.Ledger.StateAsync()).Artifacts); // Replay must not depend on live files.
        var before = (await f.Ledger.StateAsync()).Version;
        var error = await Assert.ThrowsAsync<GovernanceException>(() => f.Ledger.ExecuteAsync(
            new RequestStageTransitionCommand(f.Ledger.Actor, null, "advance", TaskStage.Scope)));
        Assert.Contains("source basis", error.Message);
        Assert.Equal(before, (await f.Ledger.StateAsync()).Version);
        await File.WriteAllTextAsync(path, "original source");
        await f.Ledger.ExecuteAsync(new RequestStageTransitionCommand(f.Ledger.Actor, null, "advance", TaskStage.Scope));
        Assert.Equal(TaskStage.Scope, (await f.Ledger.StateAsync()).Stage);
        await relay.FinishAsync();
    }

    [Fact]
    public async Task SourceTemplateCannotReadOutsideHostSourceGrantOrThroughAnEscapingLink()
    {
        using var source = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var secret = Path.Combine(outside.Path, "outside.txt");
        await File.WriteAllTextAsync(secret, "outside");
        var link = Path.Combine(source.Path, "link.txt");
        File.CreateSymbolicLink(link, secret);
        var access = new AILedger.Cli.Cognitive.ReconSourceAccess([source.Path], Path.Combine(source.Path, ".ailedger"));
        Assert.Throws<GovernanceException>(() => access.Resolve([secret]));
        Assert.Throws<GovernanceException>(() => access.Resolve([link]));
        Assert.Throws<GovernanceException>(() => access.Resolve([Path.Combine(source.Path, ".ailedger", "events.jsonl")]));
    }

    [Fact]
    public void SourceReviewRequiresDirectionalEvidenceRatherThanOrphanTestResults()
    {
        var f = new AILedger.Tests.Artifacts.Recon.InternalReconFixture();
        f.Start();
        f.Task.Apply(new AddEvidenceCommand(f.Task.OperatorId, null, "evidence", new("orphan"), "test-run", "retained.log", "Six controls passed", [], []));
        var document = InternalReconDocuments.CreateSourceReviewTemplate(f.Task.State) with
        {
            Assessments = [new("C-topic", "internal")], Report = "Recon",
            SourceReview = new("Controls", [new("/source/controls.cs", new string('a', 64), ["C-topic"], ["orphan"], "Controls checked")], "", [])
        };
        var error = Assert.Throws<GovernanceException>(() => f.File(JsonSerializer.Serialize(document)));
        Assert.Contains("directional link", error.Message);
    }
}
