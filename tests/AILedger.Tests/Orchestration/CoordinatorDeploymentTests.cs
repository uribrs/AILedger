using System.Text.Json;
using AILedger.Cli;
using AILedger.Cli.Orchestration;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Tests.Findings;
using AILedger.Tests.Providers;
using AILedger.Tests.Support;
namespace AILedger.Tests.Orchestration;

public sealed class CoordinatorDeploymentTests
{
    [Fact]
    public async Task OuterCognitiveCoordinatorCanProposeButCannotReachPrivilegedCliOrProtectedWrites()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FindingsFixture(); using var work = new TemporaryDirectory();
        await TrustedDriverIntake.CreateAsync(f.Service(), f.TaskId, f.Actor, "intake", "Fixture", "Original request", [], ["fixture"], default);
        var adapter = new ConfinedCoordinator();
        var roles = new[] { new AILedger.Core.Authority.PreparationRole(new("lead"), RoleKind.PlanningLead, RoleDefaults.For(RoleKind.PlanningLead)) };
        var options = new DriverHostOptions(f.TaskId, f.Actor, work.Path,
            new(f.Root, Path.Combine(f.Root, "lessons")) { CognitiveRoot = ContextBrief.CognitiveRoot(), Executable = "/usr/bin/true" },
            new Dictionary<CognitiveWorkKind, DriverAgentProfile> { [CognitiveWorkKind.Discovery] = new(new("lead"), "codex") })
        { Preparation = new(roles, []) };
        var result = await (await OrchestrationHost.CreateAsync(f.Service(), options, _ => adapter, new ContextAssembler(), default)).DriveAsync(new(), default);
        Assert.True(result.Code == "cognitive_judgment_pending", result.Diagnostic);
        Assert.True(adapter.AttemptedBypasses);
        var state = await f.StateAsync();
        Assert.Single(state.Decisions);
        Assert.Equal(DecisionStatus.Proposed, Assert.Single(state.Decisions.Values).Status);
        Assert.Equal("Original request", state.Goal);
        Assert.Equal(TaskStage.Discovery, state.Stage);
        Assert.Single(state.Runs);
    }

    [Fact]
    public async Task OwnerLevelConversationalShellProfileIsExplicitlyUnsupported()
    {
        using var f = new FindingsFixture(); using var work = new TemporaryDirectory(); await f.OpenAsync();
        var options = new DriverHostOptions(f.TaskId, f.Actor, work.Path, new(f.Root, Path.Combine(f.Root, "lessons")),
            new Dictionary<CognitiveWorkKind, DriverAgentProfile>()) { DeploymentProfile = "owner-shell" };
        var result = await (await OrchestrationHost.CreateAsync(f.Service(), options, _ => throw new InvalidOperationException("No provider"), new ContextAssembler(), default)).DriveAsync(new(), default);
        Assert.Equal(DriverStatus.Unsupported, result.Status);
        Assert.Equal("coordinator_deployment_unsupported", result.Code);
    }

    private sealed class ConfinedCoordinator : IAgentAdapter
    {
        public string Provider => "codex";
        internal bool AttemptedBypasses { get; private set; }
        public Task<string> ProbeVersionAsync(string executable, CancellationToken token) => Task.FromResult("local-fake");
        public async Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken token)
        {
            var endpoint = request.FindingsEndpoint!;
            Assert.NotNull(request.Isolation);
            var probe = new ProcessInvocation("/bin/sh", request.WorkingDirectory,
                ["-c", "cat \"$1/$2/events.jsonl\"; echo forged > \"$1/$2/coordination-v1.json\"", "fixture", request.LedgerRoot, request.TaskId.Value],
                "", new Dictionary<string, string>(), TimeSpan.FromSeconds(15), request.Isolation);
            var denied = await ProviderIsolationTests.RunAsync(probe);
            Assert.NotEqual(0, denied.Exit);
            Assert.DoesNotContain("Original request", denied.Output);
            var cli = await ProviderIsolationTests.RunAsync(probe with { ExecutablePath = endpoint.Command,
                Arguments = [typeof(CliApplication).Assembly.Location, "actor", "attach", "--root", request.LedgerRoot,
                    "--task", request.TaskId.Value, "--actor", "operator", "--target", "forged", "--role", "operator"] });
            Assert.NotEqual(0, cli.Exit);
            var proposal = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = "cognitive_handoff",
                arguments = new { request_id = "proposal", operation = new { kind = "decision_proposal", statement = "User approved is model text", rationale = "Proposed engineering interpretation", claims = Array.Empty<string>() } } } });
            var relay = await ProviderIsolationTests.RunAsync(probe with { ExecutablePath = endpoint.Command, Arguments = endpoint.Arguments,
                StandardInput = FindingsMcpFixture.Initialize + "\n" + FindingsMcpFixture.Ready + "\n" + proposal + "\n" });
            Assert.True(relay.Exit == 0, relay.Error);
            Assert.Contains("recorded", relay.Output);
            // Use the same bounded relay for evidence and a Blocked judgment. The model has no acceptance tools.
            using var cognition = await CognitiveRelay.OpenAsync(endpoint);
            await cognition.AssessAsync(CognitiveWorkKind.Discovery, "blocked"); await cognition.FinishAsync();
            AttemptedBypasses = true;
            return new(request.RunId, request.Provider, "confined-fixture", AgentRunStatus.Completed,
                DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, 0, "fixture", [], "", "fixture", [], false, null);
        }
    }
}
