using System.Text.Json;
using AILedger.Cli.Assurance;
using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
using AILedger.Tests.Providers;
using AILedger.Tests.Support;

namespace AILedger.Tests.HandoffAssurance;

public sealed class AssuranceProviderTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task OperatorNamedProviderCannotReceiveAcceptanceAuthority(bool routine)
    {
        using var policy = await AssuranceFixture.CreateAsync();
        using var ledger = new AILedger.Tests.Findings.FindingsFixture();
        using var protectedRoot = new TemporaryDirectory();
        await ledger.OpenAsync();
        await ContextBrief.BuildAsync(ledger.Root, ledger.TaskId.Value);
        var path = Path.Combine(protectedRoot.Path, "authority.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(policy.Policy, AILedger.Core.Handoffs.HandoffJson.Options));
        using var error = new StringWriter();
        var app = new AILedger.Cli.CliApplication(TextWriter.Null, error, _ => ledger.Service(),
            _ => throw new InvalidOperationException("Refusal must precede adapter construction"), new AILedger.Core.Application.ContextAssembler());
        if (routine)
        {
            var service = AILedger.Cli.Dispatch.ProviderDispatchHost.CreateRoutine(ledger.Service(),
                new(await ledger.StateAsync(), ledger.Actor, DateTimeOffset.UtcNow.AddHours(1)),
                new(ledger.Root, Path.Combine(ledger.Root, "lessons"))
                {
                    CognitiveRoot = ContextBrief.CognitiveRoot(), Executable = "/usr/bin/true",
                    AssuranceAuthority = path, AssuranceStore = Path.Combine(protectedRoot.Path, "store")
                }, _ => throw new InvalidOperationException("Refusal must precede adapter construction"),
                new AILedger.Core.Application.ContextAssembler());
            var result = await service.LaunchAsync(new(ledger.TaskId, ledger.Actor, new("R1"), "codex")
                { WorkingDirectory = policy.Root }, default);
            Assert.Equal(AILedger.Cli.Dispatch.DispatchFailureKind.Refused, result.Failure!.Kind);
            Assert.Contains("Acceptance belongs", result.Failure.Diagnostic);
        }
        else
        {
        Assert.Equal(1, await app.RunAsync(["provider", "launch", "--root", ledger.Root, "--task", ledger.TaskId.Value,
            "--actor", "operator", "--run", "R1", "--provider", "codex", "--working-directory", policy.Root,
            "--executable", "/usr/bin/true", "--cognitive-root", ContextBrief.CognitiveRoot(),
            "--assurance-authority", path, "--assurance-store", Path.Combine(protectedRoot.Path, "store")], default));
        Assert.Contains("Acceptance belongs", error.ToString());
        }
        Assert.Empty((await ledger.StateAsync()).Runs);
    }

    [Theory]
    [InlineData("claude")] [InlineData("codex")]
    public async Task ActualAdaptersGrantOnlyExplicitHostToolsAndSupplyAssuranceGuidance(string provider)
    {
        using var directory = new TemporaryDirectory(); Directory.CreateDirectory(Path.Combine(directory.Path, ".git"));
        var request = ProviderProtocolTests.Request(provider, AgentLaunchMode.New, null) with
        {
            WorkingDirectory = directory.Path,
            FindingsEndpoint = new("/trusted/dotnet", ["/trusted/host.dll", "findings", "relay", "12345", new string('A', 64)],
                ["inspect_assurance", "read_assurance", "record_assurance"])
        };
        var runner = new ScriptedProcessRunner().Enqueue(0, ["test-version"]);
        if (provider == "codex") runner.Enqueue(0, ["--strict-config --sandbox --cd --add-dir --output-schema --json"]).Enqueue(0, ["SESSION_ID --json"]);
        else runner.Enqueue(0, ["--print --output-format --session-id --resume --permission-prompts --settings --strict-mcp-config --disable-slash-commands"]);
        runner.Enqueue(invocation =>
        {
            string briefing;
            if (provider == "codex")
            {
                var config = ProviderProtocolTests.ValueAfter(invocation.Arguments, "--config");
                Assert.Contains("record_assurance={approval_mode=\"approve\"}", config);
                Assert.DoesNotContain("accept_assurance", config); Assert.DoesNotContain("run_assurance_checks", config);
                Assert.DoesNotContain("record_assurance", File.ReadAllText(Path.Combine(invocation.Environment["CODEX_HOME"], "config.toml")));
                briefing = invocation.StandardInput;
            }
            else
            {
                var grants = ProviderProtocolTests.ValueAfter(invocation.Arguments, "--allowedTools").Split(',');
                Assert.Contains("mcp__ailedger__record_assurance", grants);
                Assert.DoesNotContain("mcp__ailedger__accept_assurance", grants);
                Assert.DoesNotContain("mcp__ailedger__*", grants);
                briefing = ProviderProtocolTests.ValueAfter(invocation.Arguments, "-p");
            }
            Assert.Contains("Unknown stays unknown", briefing); Assert.Contains("Task 12 remains an offline read-only executor", briefing);
            var session = provider == "claude" ? ProviderProtocolTests.ValueAfter(invocation.Arguments, "--session-id") : "scripted-session";
            return new ScriptedProcessResult(0, provider == "claude"
                ? [JsonSerializer.Serialize(new { type = "result", session_id = session, result = "done" })]
                : [JsonSerializer.Serialize(new { type = "thread.started", thread_id = session }), "{\"type\":\"turn.completed\"}"], []);
        });
        IAgentAdapter adapter = provider == "codex" ? new CodexAgentAdapter(runner, (_, _, _, _, _) => Task.CompletedTask) : new ClaudeAgentAdapter(runner);
        Assert.Equal(AgentRunStatus.Completed, (await adapter.RunAsync(request, default)).Status);
    }

    [Theory]
    [InlineData("review", "Worker")] [InlineData("verification", "Researcher")]
    [InlineData("acceptance", "PlanningLead")] [InlineData("synthesis", "CodeReviewer")]
    public void LocalPolicyCannotExpandTheExistingGovernedRole(string assurance, string role) =>
        Assert.Throws<AssuranceRefusal>(() => AssuranceHost.EnsureGovernedRole(assurance, role));

    [Theory]
    [InlineData("review", "CodeReviewer")] [InlineData("verification", "Verifier")]
    [InlineData("acceptance", "Operator")] [InlineData("synthesis", "PlanningLead")]
    public void CompatibleLiveRolesStillNeedTheirExplicitLocalGrant(string assurance, string role) =>
        AssuranceHost.EnsureGovernedRole(assurance, role);

    [Fact]
    public void ProviderWritableAuthorityOrCanonicalStoreIsRejected()
    {
        using var directory = new TemporaryDirectory();
        Assert.Throws<AssuranceRefusal>(() => AssuranceHost.EnsureProtectedPaths(Path.Combine(directory.Path, "policy.json"), "/private/tmp/other-store", [directory.Path]));
        Assert.Throws<AssuranceRefusal>(() => AssuranceHost.EnsureProtectedPaths("/private/tmp/other-policy.json", Path.Combine(directory.Path, "store"), [directory.Path]));
    }
    [Fact]
    public void AliasedProviderWriteRootCannotContainTheAuthority()
    {
        using var directory = new TemporaryDirectory();
        var actual = Path.Combine(directory.Path, "actual"); Directory.CreateDirectory(actual);
        var alias = Path.Combine(directory.Path, "alias"); Directory.CreateSymbolicLink(alias, actual);
        Assert.Throws<AssuranceRefusal>(() => AssuranceHost.EnsureProtectedPaths(Path.Combine(actual, "policy.json"), "/private/tmp/other-store", [alias]));
    }

    [Fact]
    public void AssuranceInputCannotExpandResourceGrantsOrExposeTheLedger()
    {
        using var directory = new TemporaryDirectory();
        var product = Path.Combine(directory.Path, "product"); Directory.CreateDirectory(product);
        var ledger = Path.Combine(directory.Path, "ledger"); Directory.CreateDirectory(ledger);
        AssuranceHost.EnsureInputGrants([Path.Combine(product, "candidate.txt")], [product, ledger], ledger);
        Assert.Throws<AssuranceRefusal>(() => AssuranceHost.EnsureInputGrants(
            [Path.Combine(directory.Path, "outside.txt")], [product, ledger], ledger));
        Assert.Throws<AssuranceRefusal>(() => AssuranceHost.EnsureInputGrants(
            [Path.Combine(ledger, "events.jsonl")], [product, ledger], ledger));
        var alias = Path.Combine(product, "alias"); Directory.CreateSymbolicLink(alias, ledger);
        Assert.Throws<AssuranceRefusal>(() => AssuranceHost.EnsureInputGrants(
            [Path.Combine(alias, "events.jsonl")], [product, ledger], ledger));
    }

}
