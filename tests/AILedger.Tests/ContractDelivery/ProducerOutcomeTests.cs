using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Findings;
using AILedger.Tests.Inspection;

namespace AILedger.Tests.ContractDelivery;

public sealed class ProducerOutcomeTests
{
    [Theory]
    [InlineData("blocked")]
    [InlineData("partial")]
    [InlineData("reported-complete")]
    public async Task SuppliedToolBindsAndReplaysWithoutCompletingRun(string outcome)
    {
        using var f = new FindingsMcpFixture();
        await PrepareAsync(f);
        var body = Body(outcome);
        var first = await CallAsync(f, body);
        Assert.Equal("ok", first.GetProperty("status").GetString());
        Assert.Equal("producer", first.GetProperty("actor_id").GetString());
        Assert.Equal("R", first.GetProperty("run_id").GetString());
        var before = await f.Ledger.StateAsync();
        Assert.Equal(AgentRunStatus.Active, before.Runs[new("R")].Status);
        Assert.Equal(outcome, before.Runs[new("R")].ProducerOutcome!.Outcome);
        var replay = await CallAsync(f, body);
        Assert.True(replay.GetProperty("replayed").GetBoolean());
        Assert.Equal(before.Version, (await f.Ledger.StateAsync()).Version);
        await f.Ledger.ExecuteAsync(new CompleteRunCommand(f.Ledger.Actor, null, "end", new("R"), AgentRunStatus.Failed, null));
        Assert.True((await CallAsync(f, body)).GetProperty("replayed").GetBoolean());
        Assert.Equal(outcome, (await f.Ledger.StateAsync()).Runs[new("R")].ProducerOutcome!.Outcome);
    }

    [Theory]
    [InlineData("{\"outcome\":\"blocked\",\"output_evidence_ids\":[],\"blocker_evidence_ids\":[]}")]
    [InlineData("{\"outcome\":\"success\",\"output_evidence_ids\":[\"E\"],\"blocker_evidence_ids\":[]}")]
    [InlineData("{\"outcome\":\"blocked\",\"output_evidence_ids\":[],\"blocker_evidence_ids\":[\"missing\"]}")]
    [InlineData("{\"outcome\":\"blocked\",\"output_evidence_ids\":[],\"blocker_evidence_ids\":[\"E\"],\"actor_id\":\"operator\"}")]
    public async Task RefusesMalformedDeclarationsWithoutMutation(string body)
    {
        using var f = new FindingsMcpFixture();
        await PrepareAsync(f);
        var before = await f.Ledger.StateAsync();
        Assert.Equal("error", (await CallAsync(f, body)).GetProperty("status").GetString());
        Assert.Equal(before.Version, (await f.Ledger.StateAsync()).Version);
    }

    [Fact]
    public async Task OwnershipGrantRoleAndImmutabilityAreEnforced()
    {
        using var f = new FindingsMcpFixture();
        await PrepareAsync(f);
        f.Configuration = f.Configuration with { AllowDeclareProducerOutcome = false };
        Assert.Equal("error", (await CallAsync(f, Body("blocked"))).GetProperty("status").GetString());
        f.Configuration = f.Configuration with { AllowDeclareProducerOutcome = true, ActorId = "operator" };
        Assert.Equal("error", (await CallAsync(f, Body("blocked"))).GetProperty("status").GetString());
        f.Configuration = f.Configuration with { ActorId = "producer" };
        Assert.Equal("ok", (await CallAsync(f, Body("blocked"))).GetProperty("status").GetString());
        Assert.Equal("error", (await CallAsync(f, Body("reported-complete"))).GetProperty("status").GetString());
        await f.Ledger.ExecuteAsync(new AssignRoleCommand(f.Ledger.Actor, null, "revoke", new("producer"),
            RoleKind.Researcher, [Capability.BuildContext]));
        Assert.Equal("error", (await CallAsync(f, Body("blocked"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task MissingHistoricalOutcomeRemainsUnknownAndNewEventReplays()
    {
        using var f = new FindingsMcpFixture();
        await PrepareAsync(f);
        Assert.Null((await f.Ledger.StateAsync()).Runs[new("R")].ProducerOutcome);
        await CallAsync(f, Body("blocked"));
        var history = new List<LedgerEvent>();
        await foreach (var item in f.Ledger.Service().GetHistoryAsync(f.Ledger.TaskId, default)) history.Add(item);
        GovernedTaskState? state = null;
        foreach (var row in history) state = new TaskReducer().Apply(state, row);
        Assert.Equal("blocked", state!.Runs[new("R")].ProducerOutcome!.Outcome);
        var declaration = history.Single(e => e.Data is ProducerOutcomeDeclared);
        Assert.Equal(new ActorId("producer"), declaration.ActorId);
        Assert.Throws<GovernanceException>(() => new TaskReducer().Apply(
            history.Take(history.Count - 1).Aggregate((GovernedTaskState?)null, (s, e) => new TaskReducer().Apply(s, e)),
            declaration with { ActorId = f.Ledger.Actor }));
    }

    [Fact]
    public async Task DeclarationCannotBeSmuggledIntoRunStartOrUseForeignEvidence()
    {
        using var f = new FindingsMcpFixture(); await PrepareAsync(f);
        await f.Ledger.ExecuteAsync(new AddEvidenceCommand(f.Ledger.Actor, null, "foreign", new("foreign"),
            "test", "fixture:1", "Another actor's evidence", [], []));
        Assert.Equal("error", (await CallAsync(f, Body("blocked").Replace("\"E\"", "\"foreign\""))).GetProperty("status").GetString());
        GovernedTaskState? state = null;
        await foreach (var row in f.Ledger.Service().GetHistoryAsync(f.Ledger.TaskId, default))
        {
            if (row.Data is RunStarted start)
            {
                var forged = row with { Data = new RunStarted(start.Run with
                    { ProducerOutcome = new("blocked", [], [new("E")]) }) };
                Assert.Throws<GovernanceException>(() => new TaskReducer().Apply(state, forged));
                break;
            }
            state = new TaskReducer().Apply(state, row);
        }
    }

    private static async Task PrepareAsync(FindingsMcpFixture f)
    {
        await f.OpenAsync();
        await f.Ledger.ExecuteAsync(new AssignRoleCommand(f.Ledger.Actor, null, "role", new("producer"),
            RoleKind.Researcher, [Capability.BuildContext, Capability.AddEvidence]));
        await f.Ledger.ExecuteAsync(new RequestStageTransitionCommand(f.Ledger.Actor, null, "stage", TaskStage.Research,
            WithoutPrerequisitesReason: "Disposable outcome fixture"));
        await f.Ledger.ExecuteAsync(new StartRunCommand(f.Ledger.Actor, null, "start", new("R"), null,
            "codex", null, SubjectActorId: new ActorId("producer")));
        await f.Ledger.ExecuteAsync(new AddEvidenceCommand(new("producer"), null, "R", new("E"),
            "test-run", "fixture://output", "Observed output or blocker", [], []));
        f.Configuration = f.Configuration with { ActorId = "producer", RunId = "R", CorrelationId = "R",
            AllowRunless = false, AllowDeclareProducerOutcome = true };
    }

    internal static string Body(string outcome) => JsonSerializer.Serialize(new
    {
        outcome, output_evidence_ids = outcome == "blocked" ? Array.Empty<string>() : ["E"],
        blocker_evidence_ids = outcome == "blocked" ? new[] { "E" } : []
    });

    private static async Task<JsonElement> CallAsync(FindingsMcpFixture f, string body) =>
        InspectionMcpTests.Body(Assert.Single(await f.ExchangeAsync(InspectionMcpTests.Call("declare_producer_outcome", body, 2) + "\n")));
}
