using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Cli;
using AILedger.Tests.Support;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Assurance;

public sealed partial class BundleAssuranceTests
{
    [Fact]
    public async Task ManualCoverageMatchesProviderAndNoneDoesNotQualify()
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Ok("run", "start", "--run", "VAB", "--subject", "verifier", "--work", "B", "--also-work", "A", "--candidate", BundleFixture.Candidate, "--provider", "none");
        var active = await f.State();
        Assert.All(new[] { "A", "B" }, id => Assert.Equal(WorkItemStatus.Active, active.WorkItems[new WorkItemId(id)].Status));
        Assert.Equal(0, await f.File("verifier", "VAB", "OUT-VAB", "verifier-output"));
        await f.Ok("run", "complete", "--run", "VAB", "--status", "completed");
        Assert.Equal(1, await f.Run("work", "complete", "--id", "B"));
        Assert.Contains("verifier", f.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(f.Adapter.Requests);
        Assert.Equal(new[] { "A", "B" }, (await f.State()).Runs[new RunId("VAB")].Assurance!.WorkItemIds.Select(i => i.Value));
        await f.Ok("run", "start", "--run", "VREAL", "--subject", "verifier", "--work", "A", "--also-work", "B", "--candidate", BundleFixture.Candidate, "--provider", "claude");
        Assert.Equal(0, await f.File("verifier", "VREAL", "OUT-VREAL", "verifier-output"));
        await f.Ok("run", "complete", "--run", "VREAL", "--status", "completed", "--session", "new-manual-session");
        await f.Review("RREAL", "VREAL", "A", "B");
        await f.Ok("work", "complete", "--id", "A");
        await f.Ok("work", "complete", "--id", "B");
    }

    [Fact]
    public async Task ManualNewAssuranceCannotReuseProviderSession()
    {
        using var f = await BundleFixture.CreateAsync();
        await RefusesWithoutMutation(f, () => f.Run("run", "start", "--run", "BAD", "--subject", "verifier", "--work", "A", "--also-work", "B", "--candidate", BundleFixture.Candidate, "--provider", "claude", "--session", "old-session"), "fresh", "session");
    }

    [Fact]
    public async Task IntersectingRunBlocksEveryMemberButNotDisjoint()
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Ok("run", "start", "--run", "VAB", "--subject", "verifier", "--work", "A", "--also-work", "B", "--candidate", BundleFixture.Candidate, "--provider", "claude");
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["B", "C"]), "Assurance member 'B':", "active");
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["A"]), "Assurance member 'A':", "active");
        Assert.Equal(1, await f.Run("work", "complete", "--id", "B"));
        Assert.Contains("active", f.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await f.Run("work", "abandon", "--id", "B", "--reason", "overlap proof"));
        Assert.Contains("active", f.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        await f.Verify("VD", "D");
        var state = await f.State();
        var replay = Replay(await f.History());
        Assert.Equal(JsonSerializer.Serialize(state.WorkItems, ManifestJson), JsonSerializer.Serialize(replay.WorkItems, ManifestJson));
        Assert.Equal(WorkItemStatus.Active, replay.WorkItems[new WorkItemId("B")].Status);
    }

    [Fact]
    public async Task AllMembersActivateAndBlockedMemberSurvivesFailure()
    {
        using var f = await BundleFixture.CreateAsync(dependencies: true);
        await f.Ok("run", "start", "--run", "VAB", "--subject", "verifier", "--work", "A", "--also-work", "B", "--candidate", BundleFixture.Candidate, "--provider", "claude");
        await f.Ok("evidence", "add", "--id", "REFUTE", "--source-type", "fixture", "--citation", "fixture.cs:1", "--summary", "refutation", "--refutes", "CB");
        await f.Ok("claim", "resolve", "--id", "CB", "--status", "rejected", "--evidence", "REFUTE");
        var active = await f.State();
        Assert.Equal(WorkItemStatus.Active, active.WorkItems[new WorkItemId("A")].Status);
        Assert.Equal(WorkItemStatus.Blocked, active.WorkItems[new WorkItemId("B")].Status);
        await f.Ok("run", "complete", "--run", "VAB", "--status", "failed");
        var closed = await f.State();
        Assert.Equal(WorkItemStatus.Paused, closed.WorkItems[new WorkItemId("A")].Status);
        Assert.Equal(WorkItemStatus.Blocked, closed.WorkItems[new WorkItemId("B")].Status);
        var replay = Replay(await f.History());
        Assert.Equal(JsonSerializer.Serialize(closed.WorkItems, ManifestJson), JsonSerializer.Serialize(replay.WorkItems, ManifestJson));
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["A", "B"]), "Assurance member 'B':", "blocked");
        // Rejected claims are terminal in the existing kernel. Recovery cannot re-resolve CB;
        // pin the honest refusal instead of manufacturing a legal replacement history.
        Assert.Equal(1, await f.Run("work", "unblock", "--id", "B"));
        Assert.Contains("CB", f.Error.ToString(), StringComparison.Ordinal);

    }

    [Fact]
    public async Task LaterAWorkInvalidatesOnlyAQualification()
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Verify("VAB", "A", "B");
        await f.Review("RAB", "VAB", "A", "B");
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Repair);
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Execution);
        await f.Work("A", "WA2");
        await CliStageFixture.ToVerificationAsync(f.App, f.Root);
        Assert.Equal(1, await f.Run("work", "complete", "--id", "A"));
        Assert.Contains("verif", f.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        await f.Ok("work", "complete", "--id", "B");
        Assert.Equal(WorkItemStatus.Completed, (await f.State()).WorkItems[new WorkItemId("B")].Status);
    }

    // R5 (legacy-replay-boundary): real persisted fixture history, modified only in-memory to
    // reproduce historical absent/null fields and the reverted candidate's ignored extra field.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task R5_OldAndRevertedCoverageReplayAsBaseline(bool explicitNull, bool taskWide)
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Ok(["run", "start", "--run", "LEGACY", "--subject", taskWide ? "operator" : "verifier", .. (taskWide ? Array.Empty<string>() : new[] { "--work", "A" }), "--provider", "claude"]);
        var history = await f.History();
        var baseline = Replay(history);
        var json = JsonNode.Parse(JsonSerializer.Serialize(history, LedgerJson.CreateOptions()))!.AsArray();
        foreach (var node in json)
        {
            RemoveHistoricalFields(node!, explicitNull);
        }
        var parsed = JsonSerializer.Deserialize<LedgerEvent[]>(json.ToJsonString(), LedgerJson.CreateOptions())!;
        var replay = Replay(parsed);
        Assert.Equal(JsonSerializer.Serialize(baseline.WorkItems, ManifestJson), JsonSerializer.Serialize(replay.WorkItems, ManifestJson));
        Assert.Equal(taskWide ? WorkItemStatus.Paused : WorkItemStatus.Active, replay.WorkItems[new WorkItemId("A")].Status);
        Assert.Equal(WorkItemStatus.Paused, replay.WorkItems[new WorkItemId("B")].Status);
        Assert.Null(replay.Runs[new RunId("LEGACY")].Assurance);
        Assert.Null(replay.Runs[new RunId("LEGACY")].SubjectRole);
    }

    private static GovernedTaskState Replay(IEnumerable<LedgerEvent> history)
    {
        var reducer = new TaskReducer();
        GovernedTaskState? state = null;
        foreach (var item in history) state = reducer.Apply(state, item);
        return state!;
    }

    private static void RemoveHistoricalFields(JsonNode node, bool explicitNull)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("provider") && obj.ContainsKey("startedAt") && obj["id"]?.GetValue<string>() == "LEGACY")
            {
                if (explicitNull) obj["assurance"] = null; else obj.Remove("assurance");
                obj.Remove("subjectRole");
                obj.Remove("providerSessionId");
                obj["additionalWorkItemIds"] = new JsonArray("B");
            }
            foreach (var child in obj.ToArray()) if (child.Value is not null) RemoveHistoricalFields(child.Value, explicitNull);
        }
        else if (node is JsonArray array)
            foreach (var child in array) if (child is not null) RemoveHistoricalFields(child, explicitNull);
    }

    [Theory]
    [InlineData("duplicate", "Assurance coverage:")]
    [InlineData("empty", "Assurance coverage:")]
    [InlineData("blank", "Assurance coverage:")]
    [InlineData("anchor", "Assurance coverage:")]
    [InlineData("schema", "Assurance coverage:")]
    [InlineData("versions", "Assurance coverage:")]
    [InlineData("candidate", "Assurance candidate:")]
    public async Task MalformedNewAssuranceNeverFallsBackToLegacy(string defect, string diagnostic)
    {
        using var f = await BundleFixture.CreateAsync();
        var binding = await f.Binding("A", "B");
        binding = defect switch
        {
            "duplicate" => binding with { WorkItemIds = [new("A"), new("A")] },
            "empty" => binding with { WorkItemIds = [] },
            "blank" => binding with { WorkItemIds = [new("A"), new(" ")] },
            "anchor" => binding with { WorkItemIds = [new("B")] },
            "schema" => binding with { SchemaVersion = 99 },
            "versions" => binding with { WorkVersions = [] },
            "candidate" => binding with { CandidateId = "bad" },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        var state = await f.State();
        var command = new StartRunCommand(new ActorId("operator"), null, "malformed", new RunId("BAD"), new WorkItemId("A"), "claude", null,
            SubjectActorId: new ActorId("verifier"), Assurance: binding);
        var error = Assert.Throws<GovernanceException>(() => new CommandHandler().Handle(state, command, DateTimeOffset.UtcNow));
        Assert.Contains(diagnostic, error.Message, StringComparison.Ordinal);
        var timestamp = DateTimeOffset.UtcNow;
        var run = new AgentRun(new RunId("BAD"), new ActorId("verifier"), new WorkItemId("A"), "claude", null, AgentRunStatus.Active,
            timestamp, null, LaunchedBy: new ActorId("operator"), SubjectRole: RoleKind.Verifier, Assurance: binding);
        var forged = new LedgerEvent(GovernedTaskState.CurrentSchemaVersion, new EventId($"T1:{state.Version + 1:D10}"), new TaskId("T1"),
            new ActorId("operator"), timestamp, null, "raw-new-shape", new RunStarted(run));
        var json = JsonSerializer.Serialize(forged, LedgerJson.CreateOptions());
        var replayError = Assert.Throws<GovernanceException>(() => new TaskReducer().Apply(state, JsonSerializer.Deserialize<LedgerEvent>(json, LedgerJson.CreateOptions())!));
        Assert.Contains(diagnostic, replayError.Message, StringComparison.Ordinal);
    }
}
