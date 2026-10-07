using System.Text.Json;
using AILedger.Cli;
using AILedger.Cli.Dispatch;
using AILedger.Core.Authority;
using AILedger.Cli.Findings;
using AILedger.Core.Contracts;
using AILedger.Core.Application;
using AILedger.Core.Domain;
using AILedger.Tests.Providers;
using AILedger.Tests.Support;

namespace AILedger.Tests.Findings;

public sealed class ConfinedProviderLaunchTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ActualConfinedRelayRecordsAsSubjectButForgedLocalHostCannotActAsOperator(bool routine)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FindingsFixture();
        using var work = new TemporaryDirectory();
        await f.OpenAsync();
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "assign", new("researcher"), RoleKind.Researcher,
            [Capability.BuildContext, Capability.AddClaim, Capability.AddEvidence]));
        await f.ExecuteAsync(new RequestStageTransitionCommand(f.Actor, null, "research", TaskStage.Research,
            WithoutPrerequisitesReason: "Isolated process integration fixture"));
        await ContextBrief.BuildAsync(f.Root, f.TaskId.Value);
        using var error = new StringWriter();
        var adapter = new ConfinedAdapter();
        if (routine)
        {
            var authority = new RoutineOrchestrationAuthority(await f.StateAsync(), f.Actor, DateTimeOffset.UtcNow.AddHours(1));
            var service = ProviderDispatchHost.CreateRoutine(f.Service(), authority,
                new(f.Root, Path.Combine(f.Root, "lessons")) { CognitiveRoot = ContextBrief.CognitiveRoot(), Executable = "/usr/bin/true" },
                _ => adapter, new ContextAssembler());
            var receipt = await service.LaunchAsync(new(f.TaskId, f.Actor, new("R1"), "codex")
                { SubjectActorId = new("researcher"), WorkingDirectory = work.Path }, default);
            Assert.Null(receipt.Failure);
            Assert.Equal(LedgerRecordingStatus.Recorded, receipt.CompletionRecording);
        }
        else
        {
        var app = new CliApplication(TextWriter.Null, error, _ => f.Service(), _ => adapter, new ContextAssembler());
        var exit = await app.RunAsync(["provider", "launch", "--root", f.Root, "--task", f.TaskId.Value,
            "--actor", "operator", "--subject", "researcher", "--run", "R1", "--provider", "codex",
            "--executable", "/usr/bin/true", "--working-directory", work.Path,
            "--cognitive-root", ContextBrief.CognitiveRoot()], default);
        Assert.True(exit == 0, error.ToString());
        }
        var state = await f.StateAsync();
        Assert.Single(state.Claims);
        Assert.Single(state.Evidence);
        Assert.Equal(AgentRunStatus.Completed, state.Runs[new("R1")].Status);
        Assert.Equal("observed", state.Runs[new("R1")].ProviderSessionId);
    }

    private sealed class ConfinedAdapter : IAgentAdapter
    {
        public string Provider => "codex";
        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken token) => Task.FromResult("fixture");

        public async Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken token)
        {
            var endpoint = Assert.IsType<ProviderFindingsEndpoint>(request.FindingsEndpoint);
            Assert.NotNull(request.Isolation);
            Assert.DoesNotContain(request.LedgerRoot, request.AdditionalDirectories);
            var spoofed = FindingsMcpFixture.Body().Replace("\"schema_version\":1", "\"schema_version\":1,\"actor_id\":\"operator\"");
            var input = FindingsMcpFixture.Initialize + "\n" + FindingsMcpFixture.Ready + "\n" +
                FindingsMcpFixture.Call(FindingsMcpFixture.Body()) + "\n" + FindingsMcpFixture.Call(spoofed, 3) + "\n";
            var invocation = new ProcessInvocation(endpoint.Command, request.WorkingDirectory, endpoint.Arguments,
                input, new Dictionary<string, string>(), TimeSpan.FromSeconds(20), request.Isolation);
            var result = await ProviderIsolationTests.RunAsync(invocation);
            Assert.True(result.Exit == 0, $"{result.Output}\n{result.Error}");
            var responses = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(FindingsMcpFixture.Parse).ToArray();
            var recorded = FindingsMcpFixture.Payload(responses.Single(r => r.GetProperty("id").GetInt32() == 2));
            Assert.Equal("committed", recorded.GetProperty("status").GetString());
            Assert.Equal("researcher", recorded.GetProperty("receipt").GetProperty("actor_id").GetString());
            Assert.Equal("error", FindingsMcpFixture.Payload(responses.Single(r => r.GetProperty("id").GetInt32() == 3)).GetProperty("status").GetString());

            var config = new FindingsHostConfiguration(request.LedgerRoot, request.TaskId.Value, "operator", "fake",
                Path.Combine(request.WorkingDirectory, "fake-telemetry"), AllowRecordFindings: true, AllowRunless: true);
            var path = Path.Combine(request.WorkingDirectory, "fake-host.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(config,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), token);
            var forged = await ProviderIsolationTests.RunAsync(invocation with
            {
                Arguments = [typeof(CliApplication).Assembly.Location, "findings", "serve", path],
                StandardInput = FindingsMcpFixture.Initialize + "\n" + FindingsMcpFixture.Ready + "\n" +
                    FindingsMcpFixture.Call(FindingsMcpFixture.Body(FindingsFixture.Claims("forged"))) + "\n"
            });
            Assert.DoesNotContain("\"status\":\"committed\"", forged.Output);
            Assert.Contains("error", forged.Output);
            return new(request.RunId, Provider, "observed", AgentRunStatus.Completed,
                DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, 0, "fixture", [], "", "fixture", [], false, null);
        }
    }
}
