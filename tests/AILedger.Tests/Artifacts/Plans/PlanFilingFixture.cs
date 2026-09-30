using System.Text.Json.Nodes;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Cli;
using AILedger.Tests.Support;

namespace AILedger.Tests.Artifacts.Plans;

// Production CLI/handler/store for filing; all setup and historical inputs stay in this temp root.
internal sealed class PlanFilingFixture : IDisposable
{
    private readonly TemporaryDirectory _root = new();
    private readonly StringWriter _error = new();
    private readonly CliApplication _cli;
    private readonly TaskId _task = new("T1");

    internal PlanFilingFixture() => _cli = new(TextWriter.Null, _error, Service,
        _ => throw new InvalidOperationException("Plan tests must never launch a provider."), new ContextAssembler());

    internal string Error => _error.ToString();
    internal string EventsPath => Path.Combine(_root.Path, _task.Value, "events.jsonl");

    internal async Task StartAsync()
    {
        await RunAsync("task", "open", "--title", "Plan contract", "--goal", "Validate filing");
        await CliStageFixture.AdvanceAsync(_cli, _root.Path, _task.Value, TaskStage.Research, TaskStage.Design);
        await RunAsync("run", "start", "--run", "RP", "--provider", "codex");
    }

    internal async Task<int> FileAsync(string body, string id = "P1", string? supersedes = null,
        string actor = "operator", string run = "RP", string kind = "OrchestrationPlan", string? work = null)
    {
        _error.GetStringBuilder().Clear();
        using var input = new StandardInput(body);
        return await _cli.RunAsync(
            ["artifact", "record", "--root", _root.Path, "--task", _task.Value, "--actor", actor,
             "--id", id, "--kind", kind, "--title", "Contract fixture", "--run", run, "--body-stdin",
             .. supersedes is null ? Array.Empty<string>() : ["--supersedes", supersedes],
             .. work is null ? Array.Empty<string>() : ["--work", work]], CancellationToken.None);
    }

    internal Task<int> VerifyAsync(string body) =>
        FileAsync(body, "V1", actor: "verifier", run: "RV", kind: "VerifierOutput", work: "W1");

    internal async Task PrepareVerifierAsync(bool dependentClaim = false)
    {
        await RunAsync("run", "complete", "--run", "RP", "--status", "completed", "--session", "fixture-plan");
        await CliStageFixture.AdvanceAsync(_cli, _root.Path, _task.Value, TaskStage.Scope, TaskStage.Ready);
        if (dependentClaim)
            await ExecuteAsync(new AddClaimCommand(new("operator"), null, "claim", new("C1"), "Dependent assumption", null));
        await ExecuteAsync(new AddWorkItemCommand(new("operator"), null, "work", new("W1"), "Verify fixture",
            new ActorId("operator"), dependentClaim ? [new ClaimId("C1")] : [], [],
            WithoutBriefReason: "Disposable fixture for artifact filing"));
        await ExecuteAsync(new AssignRoleCommand(new("operator"), null, "role", new("verifier"),
            RoleKind.Verifier, [Capability.BuildContext, Capability.RecordArtifact]));
        await CliStageFixture.AdvanceAsync(_cli, _root.Path, _task.Value, TaskStage.Execution, TaskStage.Verification);
        await RunAsync("run", "start", "--run", "RV", "--work", "W1", "--provider", "claude", "--subject", "verifier");
    }

    internal async Task ReplanAsync()
    {
        await RunAsync("run", "complete", "--run", "RV", "--status", "failed");
        await CliStageFixture.BackAsync(_cli, _root.Path, TaskStage.Execution);
        await CliStageFixture.BackAsync(_cli, _root.Path, TaskStage.Scope);
        await RunAsync("run", "start", "--run", "RP2", "--provider", "codex");
    }

    internal async Task<GovernedTaskState> StateAsync() =>
        (await Service(_root.Path).GetStateAsync(_task, CancellationToken.None))!;

    internal async Task ReplaceWithHistoricalPlanAsync(string body)
    {
        var lines = await File.ReadAllLinesAsync(EventsPath);
        for (var i = 0; i < lines.Length; i++)
        {
            var row = JsonNode.Parse(lines[i])!;
            // Keep the event envelope and commit metadata intact; only simulate the old plan body.
            if (row["data"]?["artifact"]?["artifactId"]?.ToString() != "P1") continue;
            row["data"]!["artifact"]!["content"] = body;
            lines[i] = row.ToJsonString();
        }
        await File.WriteAllLinesAsync(EventsPath, lines);
        File.Delete(Path.Combine(_root.Path, _task.Value, "state.json"));
        Assert.Equal(body, (await StateAsync()).Artifacts[new("P1")].Content);
    }

    private Task<CommandOutcome> ExecuteAsync(LedgerCommand command) =>
        Service(_root.Path).ExecuteAsync(_task, command, CancellationToken.None);

    private async Task RunAsync(params string[] args) =>
        Assert.True(await _cli.RunAsync([.. args, "--root", _root.Path, "--task", _task.Value,
            "--actor", "operator"], CancellationToken.None) == 0, Error);

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }

    public void Dispose()
    {
        _error.Dispose();
        _root.Dispose();
    }
}
