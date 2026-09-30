using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
using AILedger.Core.Handoffs;
using AILedger.Storage;
using AILedger.Tests.Assurance;
using AILedger.Tests.Cli;
using AILedger.Tests.Findings;
using AILedger.Tests.Inspection;
using AILedger.Tests.Support;

namespace AILedger.Tests.ContractDelivery;

[Collection(StandardInput.Collection)]
public sealed class LaunchPreparationTests
{
    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task RequiredOverflowRefusesBeforeProbeAndStartWithoutDroppingRequiredContent(string provider)
    {
        using var f = await Fixture(provider);
        var before = await f.History();
        Assert.Equal(2, await f.Launch("EARLY", ["A"], provider: provider, extra: ["--max-context-bytes", "1000"]));
        Assert.Contains("No required records were truncated", f.Error.ToString());
        Assert.Equal(JsonSerializer.Serialize(before, LedgerJson.CreateOptions()),
            JsonSerializer.Serialize(await f.History(), LedgerJson.CreateOptions()));
        Assert.Equal(0, f.Adapter.Probes);
        Assert.Empty(f.Adapter.Requests);

        f.Adapter.OnProbe = async () => Assert.DoesNotContain((await f.History()), e => e.Data is RunStarted s && s.Run.Id == new RunId("VALID"));
        Assert.Equal(0, await f.Launch("VALID", ["A"], provider: provider));
        await AssertDelivered(f, "VALID");
        var brief = Assert.Single(f.Adapter.Requests).StandardInput;
        Assert.Contains("A_CLAIM_SENTINEL", brief);
        Assert.Contains("A_EVIDENCE_SENTINEL", brief);
        Assert.Contains("A_TITLE_SENTINEL", brief);
        Assert.DoesNotContain("B_CLAIM_SENTINEL", brief);
        Assert.DoesNotContain("nextActionContract", brief);
    }

    public static IEnumerable<object[]> InvalidConfigurations() =>
        from provider in new[] { "claude", "codex" }
        from failure in new[] { "missing-store", "missing-authority", "missing-file", "invalid-json", "missing-principal",
            "revoked", "expired", "wrong-role", "outside-grant", "ledger-input", "writable-authority",
            "writable-store", "store-file", "missing-input", "dependency-grant", "input-alias" }
        select new object[] { provider, failure };

    [Theory]
    [MemberData(nameof(InvalidConfigurations))]
    public async Task InvalidAssuranceSetupNeverStartsRunOrAdapter(string provider, string failure)
    {
        using var f = await Fixture(provider);
        var config = await Configuration.Create(f);
        var policy = config.Policy;
        string[] flags = config.Flags;
        string diagnostic;
        switch (failure)
        {
            case "missing-store": flags = ["--assurance-authority", config.Authority]; diagnostic = "Supply both"; break;
            case "missing-authority": flags = ["--assurance-store", config.Store]; diagnostic = "Supply both"; break;
            case "missing-file": File.Delete(config.Authority); diagnostic = "Could not find"; break;
            case "invalid-json": await File.WriteAllTextAsync(config.Authority, "{"); diagnostic = "JSON"; break;
            case "missing-principal": policy = policy with { Principals = [policy.Principals[0] with { Id = "somebody-else" }] }; diagnostic = "no assurance grant"; break;
            case "revoked": policy = policy with { Principals = [policy.Principals[0] with { Enabled = false }] }; diagnostic = "revoked or expired"; break;
            case "expired": policy = policy with { Principals = [policy.Principals[0] with { ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) }] }; diagnostic = "revoked or expired"; break;
            case "wrong-role": policy = policy with { Principals = [policy.Principals[0] with { Role = "review" }] }; diagnostic = "governed role"; break;
            case "outside-grant": policy = policy with { CandidateRoot = Path.Combine(f.Repository, "B") }; diagnostic = "resource grants"; break;
            case "ledger-input": policy = policy with { CandidateRoot = f.Root }; diagnostic = "authoritative ledger"; break;
            case "writable-authority":
                var writable = Path.Combine(policy.CandidateRoot, "authority.json");
                await config.Write(writable); flags = ["--assurance-authority", writable, "--assurance-store", config.Store];
                diagnostic = "granted write roots"; break;
            case "writable-store": flags = ["--assurance-authority", config.Authority, "--assurance-store", Path.Combine(policy.CandidateRoot, "store")]; diagnostic = "granted write roots"; break;
            case "store-file": await File.WriteAllTextAsync(config.Store, "not a directory"); diagnostic = "directory ancestors"; break;
            case "missing-input": File.Delete(Path.Combine(policy.CandidateRoot, "candidate.cs")); diagnostic = "Could not find"; break;
            case "dependency-grant":
                policy = policy with { Areas = [policy.Areas[0] with { DependsOnAreas = ["dependency"] }, policy.Areas[0] with { Id = "dependency" }] };
                diagnostic = "dependency area"; break;
            case "input-alias":
                var candidate = Path.Combine(policy.CandidateRoot, "candidate.cs");
                File.Delete(candidate); File.CreateSymbolicLink(candidate, Path.Combine(f.Root, "T1", "events.jsonl"));
                diagnostic = "authoritative ledger"; break;
            default: throw new InvalidOperationException(failure);
        }
        if (policy != config.Policy) await config.Write(policy: policy);
        var before = await f.History();
        Assert.NotEqual(0, await f.Launch("EARLY", ["A"], provider: provider, extra: flags));
        Assert.Contains(diagnostic, f.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JsonSerializer.Serialize(before, LedgerJson.CreateOptions()),
            JsonSerializer.Serialize(await f.History(), LedgerJson.CreateOptions()));
        Assert.Equal(0, f.Adapter.Probes);
        Assert.Empty(f.Adapter.Requests);
        if (failure is "revoked" or "wrong-role" or "outside-grant" or "ledger-input" or "writable-store")
        {
            var refusal = await File.ReadAllTextAsync(Path.Combine(f.Root, "T1", "refusals.jsonl"));
            Assert.Contains(diagnostic, refusal, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("provider launch", refusal);
        }
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task ConfiguredLaunchDeliversDiscoveryAndRechecksAuthorityAndInputsAtToolUse(string provider)
    {
        using var f = await Fixture(provider);
        var config = await Configuration.Create(f);
        Assert.False(Directory.Exists(config.Store));
        f.Adapter.OnProbe = async () =>
        {
            Assert.False(Directory.Exists(config.Store));
            Assert.DoesNotContain(await f.History(), e => e.Data is RunStarted s && s.Run.Id == new RunId("VALID"));
        };
        f.Adapter.BeforeResult = async request =>
        {
            Assert.Contains("configured-assurance-discovery", request.StandardInput);
            Assert.Contains("bounded-area", request.StandardInput);
            Assert.Contains("configured-check", request.StandardInput);
            Assert.DoesNotContain("REQUIREMENT_FILE_SECRET", request.StandardInput);
            using var relay = new McpProcess(request.FindingsEndpoint!.Arguments.Skip(1).ToArray());
            await ProviderFindingsSessionTests.InitializeAsync(relay);
            const string inspect = """{"schema_version":1,"area_id":"bounded-area"}""";
            await relay.SendAsync(InspectionMcpTests.Call("inspect_assurance", inspect, 2));
            var first = InspectionMcpTests.Body(await relay.ReadAsync());
            Assert.Equal("ok", first.GetProperty("status").GetString());
            var oldBinding = first.GetProperty("data").GetProperty("snapshot").GetProperty("binding_sha256").GetString();
            await File.WriteAllTextAsync(Path.Combine(config.Policy.CandidateRoot, "candidate.cs"), "changed candidate");
            await relay.SendAsync(InspectionMcpTests.Call("read_assurance", JsonSerializer.Serialize(new
            {
                schema_version = 1, request_id = "changed", area_id = "bounded-area", expected_binding = oldBinding,
                paths = new[] { "candidate.cs" }
            }), 3));
            Assert.Contains("stale_candidate", InspectionMcpTests.Body(await relay.ReadAsync()).GetRawText());
            await config.Write(policy: config.Policy with { Principals = [config.Policy.Principals[0] with { Enabled = false }] });
            await relay.SendAsync(InspectionMcpTests.Call("inspect_assurance", inspect, 4));
            Assert.Contains("authorization_denied", InspectionMcpTests.Body(await relay.ReadAsync()).GetRawText());
            Assert.Empty(await relay.FinishAsync());
        };
        Assert.Equal(0, await f.Launch("VALID", ["A"], provider: provider, extra: config.Flags));
        await AssertDelivered(f, "VALID");
    }

    [Theory]
    [InlineData("claude", "revoked")]
    [InlineData("codex", "revoked")]
    [InlineData("claude", "inputs")]
    [InlineData("codex", "inputs")]
    public async Task ConfigurationChangedDuringVersionProbeFailsAtUseWithExistingCleanup(string provider, string change)
    {
        using var f = await Fixture(provider);
        var config = await Configuration.Create(f);
        f.Adapter.OnProbe = () => config.Write(policy: change == "revoked"
            ? config.Policy with { Principals = [config.Policy.Principals[0] with { Enabled = false }] }
            : config.Policy with { CandidateRoot = f.Root });
        Assert.Equal(1, await f.Launch("CHANGED", ["A"], provider: provider, extra: config.Flags));
        Assert.Equal(1, f.Adapter.Probes);
        Assert.Empty(f.Adapter.Requests);
        var run = (await f.State()).Runs[new RunId("CHANGED")];
        Assert.Equal(AgentRunStatus.Failed, run.Status);
        Assert.Null(run.ManifestHash);
        var history = await f.History();
        Assert.Single(history.Where(e => e.Data is RunStarted s && s.Run.Id == run.Id));
        Assert.Single(history.Where(e => e.Data is RunCompleted c && c.RunId == run.Id));
        Assert.Contains("nextActionContract", f.Output.ToString());
    }

    public static IEnumerable<object[]> InvalidReviews() =>
        from provider in new[] { "claude", "codex" }
        from failure in new[] { "missing-pair", "candidate", "members", "stale-work", "session", "profile" }
        select new object[] { provider, failure };

    [Theory]
    [MemberData(nameof(InvalidReviews))]
    public async Task ReviewerBindingsAndProfileFailBeforeProbeAndStart(string provider, string failure)
    {
        using var f = await Fixture(provider);
        Assert.Equal(0, await f.Launch("VERIFIED", ["A"], provider: provider));
        if (failure == "stale-work")
        {
            await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Execution);
            await f.Work("A", "NEW-WORK", provider == "claude" ? "codex" : "claude");
            await CliStageFixture.ToVerificationAsync(f.App, f.Root);
        }
        await CliStageFixture.ToReviewAsync(f.App, f.Root);
        var config = await Configuration.Create(f, reviewer: true);
        var before = await f.History();
        var probes = f.Adapter.Probes;
        var requests = f.Adapter.Requests.Count;
        Assert.Equal(1, await f.Launch("EARLY", failure == "members" ? ["A", "B"] : ["A"], "reviewer",
            failure == "missing-pair" ? null : "VERIFIED", failure == "candidate" ? new string('b', 64) : BundleFixture.Candidate,
            provider, failure == "profile" ? config.Flags : failure == "session" ? ["--session", "old-session"] : []));
        Assert.Contains(failure == "profile" ? "incompatible with blind CodeReviewer" : failure == "session" ? "fresh provider session" : "Assurance pairing", f.Error.ToString());
        if (failure == "profile")
        {
            Assert.Contains("bounded-area", f.Error.ToString());
            Assert.Contains("configured-check", f.Error.ToString());
            Assert.Contains("Do not supply requirements", f.Error.ToString());
        }
        Assert.Equal(JsonSerializer.Serialize(before, LedgerJson.CreateOptions()),
            JsonSerializer.Serialize(await f.History(), LedgerJson.CreateOptions()));
        Assert.Equal(probes, f.Adapter.Probes);
        Assert.Equal(requests, f.Adapter.Requests.Count);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task ChangedWorkingProvenanceIsRecheckedByDurableAdmission(string provider)
    {
        using var f = await Fixture(provider);
        f.Adapter.OnProbe = async () =>
        {
            await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Execution);
            await f.Work("A", "NEW-WORK", provider == "claude" ? "codex" : "claude");
            await CliStageFixture.ToVerificationAsync(f.App, f.Root);
        };
        Assert.Equal(1, await f.Launch("CHANGED", ["A"], provider: provider));
        Assert.Contains("working provenance does not match", f.Error.ToString());
        Assert.Equal(1, f.Adapter.Probes);
        Assert.Empty(f.Adapter.Requests);
        Assert.False((await f.State()).Runs.ContainsKey(new RunId("CHANGED")));
    }

    private static Task<BundleFixture> Fixture(string provider) => BundleFixture.CreateAsync(dependencies: true,
        workerProvider: provider == "claude" ? "codex" : "claude");

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public async Task RequiredBriefThatGrowsAfterPreparationIsRebuiltAndRefusedAtUse(string provider)
    {
        using var f = await Fixture(provider);
        f.Adapter.OnProbe = () => f.Ok("constraint", "add", "--id", "GROWTH", "--statement", new string('x', 300_000), "--source", "fixture");
        Assert.Equal(2, await f.Launch("GREW", ["A"], provider: provider));
        Assert.Contains("No required records were truncated", f.Error.ToString());
        Assert.Equal(1, f.Adapter.Probes);
        Assert.Empty(f.Adapter.Requests);
        var run = (await f.State()).Runs[new RunId("GREW")];
        Assert.Equal(AgentRunStatus.Failed, run.Status);
        Assert.Null(run.ManifestHash);
    }

    private static async Task AssertDelivered(BundleFixture f, string id)
    {
        var request = f.Adapter.Requests.Single(r => r.RunId == new RunId(id));
        var active = f.Adapter.ActiveStates.Single(s => s.Runs[new RunId(id)].Status == AgentRunStatus.Active);
        var manifest = JsonSerializer.Deserialize<ContextManifest>(request.StandardInput, LedgerJson.CreateOptions())!;
        Assert.Equal(active.Version, manifest.TaskVersion);
        Assert.Contains(manifest.Artifacts, a => a.Kind == ContextArtifactKind.Rules);
        Assert.Contains(manifest.Artifacts, a => a.Kind == ContextArtifactKind.Skill);
        var completed = (await f.State()).Runs[new RunId(id)];
        Assert.Equal(AgentRunStatus.Completed, completed.Status);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.StandardInput))).ToLowerInvariant(), completed.ManifestHash);
        Assert.Equal(manifest.Artifacts.Count, completed.ManifestArtifactCount);
    }

    private sealed record Configuration(string Authority, string Store, AssurancePolicy Policy)
    {
        public string[] Flags => ["--assurance-authority", Authority, "--assurance-store", Store];
        public Task Write(string? path = null, AssurancePolicy? policy = null) =>
            File.WriteAllTextAsync(path ?? Authority, JsonSerializer.Serialize(policy ?? Policy, HandoffJson.Options));

        public static async Task<Configuration> Create(BundleFixture f, bool reviewer = false)
        {
            var parent = Path.GetDirectoryName(f.Root)!;
            var root = Path.Combine(f.Repository, "A");
            await File.WriteAllTextAsync(Path.Combine(root, "candidate.cs"), "return 1;");
            await File.WriteAllTextAsync(Path.Combine(root, "requirements.txt"), "REQUIREMENT_FILE_SECRET");
            var policy = new AssurancePolicy(1, "phase3-fixture", root, "worker",
                [new("bounded-area", ["candidate.cs"], ["requirements.txt"], [], [], [new("criterion", "Return one", "configured-check")])],
                [new(reviewer ? "reviewer" : "verifier", reviewer ? "review" : "verification", true, DateTimeOffset.UtcNow.AddHours(1), ["bounded-area"])],
                [new("configured-check", "/usr/bin/true", new string('a', 64), [], 5)]);
            var configuration = new Configuration(Path.Combine(parent, "policy.json"), Path.Combine(parent, "assurance-store"), policy);
            await configuration.Write();
            return configuration;
        }
    }
}
