using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Providers.Adapters;
using AILedger.Tests.Cli;
using AILedger.Tests.Support;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Assurance;

[Collection(StandardInput.Collection)]
public sealed partial class BundleAssuranceTests
{
    [Fact]
    public async Task RelatedBundleFilesClosesAndCompletesEveryMember()
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Verify("VAB", "B", "A");
        await f.Review("RAB", "VAB", "A", "B");
        foreach (var id in new[] { "A", "B" }) await f.Ok("work", "complete", "--id", id);
        var state = await f.State();
        Assert.All(new[] { "A", "B" }, id => Assert.Equal(WorkItemStatus.Completed, state.WorkItems[new WorkItemId(id)].Status));
        Assert.Equal(2, f.Adapter.Requests.Count);
        foreach (var request in f.Adapter.Requests)
        {
            Assert.Equal(new[] { "A", "B" }, request.Assurance!.WorkItemIds.Select(i => i.Value));
            Assert.Equal(AgentLaunchMode.New, request.Mode);
            Assert.Null(request.ProviderSessionId);
            Assert.Equal(BundleFixture.Candidate, request.Assurance.CandidateId);
            Assert.Equal(AgentRunStatus.Completed, state.Runs[request.RunId].Status);
        }
        Assert.Equal(new RunId("VAB"), state.Runs[new RunId("RAB")].Assurance!.VerifierRunId);
        Assert.Equal(2, state.Artifacts.Values.Count(a => a.Assurance is not null));
    }

    // R2 (complete-grant-union): includes the separate singleton regression from the reverted work.
    [Fact]
    public async Task R2_SingletonGetsEveryScope()
    {
        using var f = await BundleFixture.CreateAsync(multipleScopes: true);
        Assert.True(await f.Launch("VA", ["A"], candidate: null) == 0, f.Error.ToString());
        var request = Assert.Single(f.Adapter.Requests);
        Assert.Equal(Path.Combine(f.Repository, "A"), request.WorkingDirectory);
        Assert.Contains(Path.Combine(f.Repository, "A-second"), request.AdditionalDirectories);
        Assert.Contains(f.Root, request.AdditionalDirectories);
        Assert.DoesNotContain(f.Repository, request.AdditionalDirectories);
        Assert.DoesNotContain(Path.Combine(f.Repository, "B"), request.AdditionalDirectories);
    }

    [Fact]
    public async Task OperatorDispatchCoversDifferentlyOwnedMembers()
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Verify("VAB", "A", "B");
        var active = Assert.Single(f.Adapter.ActiveStates);
        Assert.Equal(new ActorId("worker"), active.WorkItems[new WorkItemId("A")].Owner);
        Assert.Equal(new ActorId("other"), active.WorkItems[new WorkItemId("B")].Owner);
        Assert.All(new[] { "A", "B" }, id => Assert.Equal(WorkItemStatus.Active, active.WorkItems[new WorkItemId(id)].Status));
        Assert.Equal(new ActorId("operator"), active.Runs[new RunId("VAB")].LaunchedBy);
        Assert.Equal(new ActorId("verifier"), active.Runs[new RunId("VAB")].ActorId);
        var request = Assert.Single(f.Adapter.Requests);
        Assert.Contains(Path.Combine(f.Repository, "B"), request.AdditionalDirectories);
        Assert.DoesNotContain(f.Repository, request.AdditionalDirectories);
    }

    [Fact]
    public async Task LegacySingletonPreservesMeaning()
    {
        using var f = await BundleFixture.CreateAsync();
        Assert.True(await f.Launch("VA", ["B"], candidate: null, extra: ["--work", "A"]) == 0, f.Error.ToString());
        var request = Assert.Single(f.Adapter.Requests);
        Assert.Equal(new WorkItemId("A"), request.WorkItemId);
        Assert.Null(request.Assurance);
        Assert.DoesNotContain("primary", request.StandardInput, StringComparison.OrdinalIgnoreCase);
        var run = (await f.State()).Runs[new RunId("VA")];
        Assert.Null(run.Assurance);
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Equal(new WorkItemId("A"), (await f.State()).Artifacts[new ArtifactId("OUT-VA")].WorkItemId);
    }

    [Theory]
    [InlineData("A", "B", "CODEX", "A")]
    [InlineData("B", "A", "CoDeX", "A")]
    [InlineData("A", "B", "ClAuDe", "B")]
    [InlineData("B", "A", "CLAUDE", "B")]
    public async Task AnySameProviderMemberRefusesBeforeProbe(string anchor, string other, string provider, string offender)
    {
        using var f = await BundleFixture.CreateAsync(workerBProvider: "claude");
        await RefusesWithoutMutation(f, () => f.Launch("BAD", [anchor, other], provider: provider),
            $"Assurance member '{offender}':", "provider");
    }

    [Theory]
    [InlineData("", "Assurance candidate:")]
    [InlineData("ABCDEF", "Assurance candidate:")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "Assurance candidate:")]
    public async Task MalformedCandidateRefusesBeforeProbe(string candidate, string prefix)
    {
        using var f = await BundleFixture.CreateAsync();
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["A", "B"], candidate: candidate), prefix);
    }

    // R3 (candidate-drift): the kernel proves declared identity equality, never filesystem sameness.
    [Fact]
    public async Task R3_CandidateIdentityMustMatchVerifier()
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Verify("VAB", "A", "B");
        await CliStageFixture.ToReviewAsync(f.App, f.Root);
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["A", "B"], "reviewer", "VAB", new string('b', 64)),
            "Assurance pairing:", "candidate");
    }

    [Theory]
    [InlineData("A", "B", "verifier", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("A", "C", "verifier", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("A", "B", "reviewer", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task NewAssuranceResumeRefusesBeforeProbe(string a, string b, string subject, string candidate)
    {
        using var f = await BundleFixture.CreateAsync();
        await RefusesWithoutMutation(f, () => f.Run("provider", "resume", "--subject", subject, "--run", "BAD",
            "--work", a, "--also-work", b, "--candidate", candidate, "--session", "old-session", "--provider", "claude",
            "--executable", "/usr/bin/true", "--cognitive-root", FindCognitiveRoot()),
            1, "Frozen assurance runs require a fresh provider session; use provider launch.");
    }

    [Fact]
    public async Task ReviewerRequiresExactCurrentVerifierPair()
    {
        using var f = await BundleFixture.CreateAsync();
        Assert.True(await f.Launch("VC-legacy", ["C"], candidate: null) == 0, f.Error.ToString());
        await f.Verify("VA", "A");
        await CliStageFixture.ToReviewAsync(f.App, f.Root);
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["A", "B"], "reviewer", "VA"), "Assurance pairing:");
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["A", "B"], "reviewer", "MISSING"), "Assurance pairing:");
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["C"], "reviewer", "VC-legacy"), "Assurance pairing:");
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Repair);
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Verification);
        await f.Verify("VA2", "A");
        await CliStageFixture.ToReviewAsync(f.App, f.Root);
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["A"], "reviewer", "VA"), "Assurance pairing:");
        Assert.True(await f.Launch("RA2", ["A"], "reviewer", "VA2") == 0, f.Error.ToString());
    }

    [Fact]
    public async Task CapabilityAndStageAdmissionHappenBeforeProbe()
    {
        using var f = await BundleFixture.CreateAsync();
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["A", "B"], extra: ["--actor", "worker"]), "ManageRuns");
        await CliStageFixture.BackAsync(f.App, f.Root, TaskStage.Execution);
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["A", "B"]), "Verification");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnauthorizedMemberHasNoProbeOrPartialActivation(bool ownSubject)
    {
        using var f = await BundleFixture.CreateAsync(verifierOwnsA: true);
        await f.Service.ExecuteAsync(new TaskId("T1"), new AssignRoleCommand(new ActorId("operator"), null, "grant",
            new ActorId("verifier"), RoleKind.Verifier, [Capability.ManageRuns, Capability.BuildContext, Capability.RecordArtifact]), CancellationToken.None);
        await ContextBrief.BuildAsync(f.Root, "T1", "verifier");
        if (!ownSubject) await CliStageFixture.ToReviewAsync(f.App, f.Root);
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["A", "B"], ownSubject ? "verifier" : "reviewer",
            extra: ["--actor", "verifier"]), ownSubject ? "Assurance member 'B':" : "operator", ownSubject ? "own" : "dispatch");
    }

    [Fact]
    public async Task DeliveredMembershipIsRunTruthNotContextDedup()
    {
        using var f = await BundleFixture.CreateAsync();
        foreach (var selection in new[] { new[] { "A" }, ["B"], ["A", "B"] })
            await f.Ok(["context", "build", "--actor", "verifier", .. BundleFixture.Scope(selection), "--cognitive-root", FindCognitiveRoot(), "--output", Path.Combine(f.Root, "brief.json")]);
        var before = (await f.History()).Count(e => e.Data is ContextBuilt);
        await f.Verify("VAB", "B", "A");
        var request = Assert.Single(f.Adapter.Requests);
        var manifest = JsonSerializer.Deserialize<ContextManifest>(request.StandardInput, ManifestJson)!;
        var run = (await f.State()).Runs[request.RunId];
        Assert.Equal(new[] { "A", "B" }, manifest.CoveredWorkItemIds!.Select(i => i.Value));
        Assert.Equal(JsonSerializer.Serialize(run.Assurance, ManifestJson), JsonSerializer.Serialize(manifest.Assurance, ManifestJson));
        Assert.Equal(JsonSerializer.Serialize(run.Assurance, ManifestJson), JsonSerializer.Serialize(request.Assurance, ManifestJson));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.StandardInput))).ToLowerInvariant(), run.ManifestHash);
        Assert.Equal(before, (await f.History()).Count(e => e.Data is ContextBuilt));
    }

    // R4 (reviewer-narrative-isolation): all task narrative, including apparently allowed titles,
    // evidence and claims, must be absent from the actual request handed to the adapter.
    [Fact]
    public async Task R4_ReviewerInputHasNoDirectOrIndirectNarrative()
    {
        using var f = await BundleFixture.CreateAsync(dependencies: true);
        await f.Verify("VAB", "A", "B");
        await f.Review("RAB", "VAB", "A", "B");
        var request = f.Adapter.Requests.Last();
        Assert.DoesNotContain("SENTINEL", request.StandardInput, StringComparison.Ordinal);
        var manifest = JsonSerializer.Deserialize<ContextManifest>(request.StandardInput, ManifestJson)!;
        Assert.All(manifest.Artifacts, a => Assert.Contains(a.Kind, new[] { ContextArtifactKind.Rules, ContextArtifactKind.Skill, ContextArtifactKind.StopCondition }));
        Assert.Equal(new[] { "A", "B" }, manifest.ReviewWorkItems!.Select(i => i.WorkItemId.Value));
        Assert.Equal(new RunId("VAB"), manifest.Assurance!.VerifierRunId);
        Assert.Equal(AgentLaunchMode.New, request.Mode);
        Assert.Null(request.ProviderSessionId);
        var process = new ScriptedProcessRunner()
            .Enqueue(0, ["codex-cli 1.2.3"])
            .Enqueue(0, ["--strict-config --sandbox --cd --add-dir --output-schema --json"])
            .Enqueue(0, ["SESSION_ID --json"])
            .Enqueue(0, ["{\"type\":\"thread.started\",\"thread_id\":\"fresh-review\"}", "{\"type\":\"turn.completed\"}"]);
        var result = await new CodexAgentAdapter(process, (_, _, _, _, _) => Task.CompletedTask)
            .RunAsync(request with { Provider = "codex" }, CancellationToken.None);
        Assert.Equal(AgentRunStatus.Completed, result.Status);
        var input = process.Invocations.Last().StandardInput;
        Assert.DoesNotContain("SENTINEL", input, StringComparison.Ordinal);
        Assert.DoesNotContain("Read them; never edit them", input, StringComparison.Ordinal);
        Assert.Contains(BundleFixture.Candidate, input, StringComparison.Ordinal);
        Assert.Contains("VAB", input, StringComparison.Ordinal);
        foreach (var ambient in new[] { "AGENTS.md", "AGENTS.override.md", "CLAUDE.md", "CLAUDE.local.md" })
            Assert.Contains(ambient, input, StringComparison.Ordinal);
        foreach (var path in request.AdditionalDirectories) Assert.Contains(path, process.Invocations.Last().Arguments);
    }

    // R4 (reviewer-narrative-isolation): refreshing context cannot downgrade an active binding.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveBoundReviewerContextRefreshRefusesBeforeDelivery(bool bundle)
    {
        using var f = await BundleFixture.CreateAsync(dependencies: true);
        string[] members = bundle ? ["A", "B"] : ["A"];
        await f.Verify("V", members);
        var callbacks = 0;
        f.Adapter.BeforeResult = async request =>
        {
            callbacks++;
            Assert.DoesNotContain("SENTINEL", request.StandardInput, StringComparison.Ordinal);
            var initial = JsonSerializer.Deserialize<ContextManifest>(request.StandardInput, ManifestJson)!;
            Assert.Equal(members, initial.ReviewWorkItems!.Select(i => i.WorkItemId.Value));
            Assert.Equal(new RunId("V"), initial.Assurance!.VerifierRunId);
            var state = await f.State();
            var bound = new ContextAssembler().Build(state, request.ActorId, request.WorkItemId,
                initial.Artifacts, DateTimeOffset.UtcNow, request.CoveredWorkItemIds, request.Assurance);
            Assert.Equal(JsonSerializer.Serialize(initial.Assurance, ManifestJson), JsonSerializer.Serialize(bound.Assurance, ManifestJson));
            Assert.DoesNotContain("SENTINEL", JsonSerializer.Serialize(bound, ManifestJson), StringComparison.Ordinal);
            Assert.Equal(members, bound.ReviewWorkItems!.Select(i => i.WorkItemId.Value));

            foreach (string[] selection in new string[][] { [], ["A"], members, ["B"], ["A", "C"], ["MISSING"] })
            {
                var directError = Assert.Throws<GovernanceException>(() => new ContextAssembler().Build(
                    state, request.ActorId, selection.Length == 0 ? null : new WorkItemId(selection[0]),
                    initial.Artifacts, DateTimeOffset.UtcNow,
                    selection.Length == 0 ? null : selection.Select(id => new WorkItemId(id)).ToArray()));
                Assert.Equal("Assurance coverage: an active bound reviewer cannot build unbound context; use the supplied frozen manifest.", directError.Message);
                var outputPath = Path.Combine(f.Root, "refused-context.json");
                f.Output.GetStringBuilder().Clear();
                await RefusesWithoutMutation(f, () => f.Run(["context", "build", "--actor", "reviewer",
                    .. BundleFixture.Scope(selection), "--cognitive-root", FindCognitiveRoot(), "--output", outputPath]),
                    "Assurance coverage: an active bound reviewer cannot build unbound context; use the supplied frozen manifest.");
                Assert.Empty(f.Output.ToString());
                Assert.DoesNotContain("SENTINEL", f.Error.ToString(), StringComparison.Ordinal);
                Assert.False(System.IO.File.Exists(outputPath));
            }
            // A stdout refresh must also refuse, rather than merely protecting file output.
            f.Output.GetStringBuilder().Clear();
            await RefusesWithoutMutation(f, () => f.Run("context", "build", "--actor", "reviewer",
                "--cognitive-root", FindCognitiveRoot()), "Assurance coverage: an active bound reviewer cannot build unbound context; use the supplied frozen manifest.");
            Assert.Empty(f.Output.ToString());

            // The restriction belongs to this reviewer, not other actors on the same active state.
            f.Output.GetStringBuilder().Clear();
            await f.Ok("context", "build", "--actor", "worker", "--work", "A", "--cognitive-root", FindCognitiveRoot());
            Assert.Contains("A_TITLE_SENTINEL", f.Output.ToString(), StringComparison.Ordinal);
        };
        await f.Review("R", "V", members);
        Assert.Equal(1, callbacks);
        Assert.Equal(AgentRunStatus.Completed, (await f.State()).Runs[new RunId("R")].Status);
    }

    [Fact]
    public async Task ActiveLegacyReviewerContextRefreshRetainsLegacyBehavior()
    {
        using var f = await BundleFixture.CreateAsync();
        Assert.Equal(0, await f.Launch("V", ["A"], candidate: null));
        await CliStageFixture.ToReviewAsync(f.App, f.Root);
        var callbacks = 0;
        f.Adapter.BeforeResult = async request =>
        {
            callbacks++;
            Assert.Null(request.Assurance);
            f.Output.GetStringBuilder().Clear();
            await f.Ok("context", "build", "--actor", "reviewer", "--work", "A", "--cognitive-root", FindCognitiveRoot());
            var manifest = JsonSerializer.Deserialize<ContextManifest>(f.Output.ToString(), ManifestJson)!;
            Assert.Null(manifest.Assurance);
            Assert.Contains(manifest.Artifacts, a => a.Content.Contains("A_TITLE_SENTINEL", StringComparison.Ordinal));
        };
        Assert.True(await f.Launch("R", ["A"], subject: "reviewer", candidate: null) == 0, f.Error.ToString());
        Assert.Equal(1, callbacks);
        Assert.Equal(AgentRunStatus.Completed, (await f.State()).Runs[new RunId("R")].Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisjointLegacyAndBoundReviewerLaunchesKeepTheirOwnContext(bool legacyFirst)
    {
        using var f = await BundleFixture.CreateAsync();
        Assert.True(await f.Launch("VD", ["D"], candidate: null) == 0, f.Error.ToString());
        await f.Verify("VAB", "A", "B");
        await CliStageFixture.ToReviewAsync(f.App, f.Root);
        var outer = legacyFirst ? "RD" : "RAB";
        var callbacks = 0;
        Task<int> LaunchReview(string id) => id == "RD"
            ? f.Launch(id, ["D"], subject: "reviewer", candidate: null)
            : f.Launch(id, ["A", "B"], subject: "reviewer", pair: "VAB");
        f.Adapter.BeforeResult = async request =>
        {
            callbacks++;
            var manifest = JsonSerializer.Deserialize<ContextManifest>(request.StandardInput, ManifestJson)!;
            var state = await f.State();
            var rebuilt = new ContextAssembler().BuildForRun(state, request.ActorId, request.RunId,
                manifest.Artifacts, DateTimeOffset.UtcNow);
            Assert.Equal(request.WorkItemId, rebuilt.WorkItemId);
            Assert.Equal(JsonSerializer.Serialize(manifest.Assurance, ManifestJson),
                JsonSerializer.Serialize(rebuilt.Assurance, ManifestJson));
            if (request.RunId.Value == "RD")
            {
                Assert.Null(request.Assurance);
                Assert.Null(manifest.Assurance);
                Assert.Null(manifest.CoveredWorkItemIds);
                Assert.Null(rebuilt.CoveredWorkItemIds);
                Assert.Contains("D_TITLE_SENTINEL", request.StandardInput, StringComparison.Ordinal);
                Assert.Contains(rebuilt.Artifacts, a => a.Content.Contains("D_TITLE_SENTINEL", StringComparison.Ordinal));
                Assert.DoesNotContain("A_TITLE_SENTINEL", request.StandardInput, StringComparison.Ordinal);
                Assert.DoesNotContain("B_TITLE_SENTINEL", request.StandardInput, StringComparison.Ordinal);
                Assert.Equal(Path.Combine(f.Repository, "D"), request.WorkingDirectory);
                Assert.Equal(new[] { f.Root }, request.AdditionalDirectories);
            }
            else
            {
                Assert.DoesNotContain("SENTINEL", request.StandardInput, StringComparison.Ordinal);
                Assert.DoesNotContain("SENTINEL", JsonSerializer.Serialize(rebuilt, ManifestJson), StringComparison.Ordinal);
                Assert.Equal(new[] { "A", "B" }, manifest.CoveredWorkItemIds!.Select(i => i.Value));
                Assert.Equal(new[] { "A", "B" }, rebuilt.ReviewWorkItems!.Select(i => i.WorkItemId.Value));
                Assert.Equal(new RunId("VAB"), manifest.Assurance!.VerifierRunId);
                Assert.Equal(BundleFixture.Candidate, manifest.Assurance.CandidateId);
                Assert.Equal(JsonSerializer.Serialize(state.Runs[request.RunId].Assurance, ManifestJson),
                    JsonSerializer.Serialize(manifest.Assurance, ManifestJson));
                Assert.Equal(Path.Combine(f.Repository, "A"), request.WorkingDirectory);
                Assert.Equal(new[] { f.Root, Path.Combine(f.Repository, "B") }.Order(StringComparer.Ordinal),
                    request.AdditionalDirectories.Order(StringComparer.Ordinal));
            }
            if (request.RunId.Value == outer)
            {
                Assert.True(await LaunchReview(legacyFirst ? "RAB" : "RD") == 0, f.Error.ToString());
                Assert.Equal(AgentRunStatus.Active, (await f.State()).Runs[request.RunId].Status);
                return;
            }
            Assert.All(new[] { "RD", "RAB" }, id => Assert.Equal(AgentRunStatus.Active, state.Runs[new RunId(id)].Status));
            Assert.All(new[] { "A", "B", "D" }, id => Assert.Equal(WorkItemStatus.Active, state.WorkItems[new WorkItemId(id)].Status));
            var outputPath = Path.Combine(f.Root, "concurrent-refresh.json");
            f.Output.GetStringBuilder().Clear();
            await RefusesWithoutMutation(f, () => f.Run("context", "build", "--actor", "reviewer",
                "--work", "D", "--cognitive-root", FindCognitiveRoot(), "--output", outputPath),
                "Assurance coverage: an active bound reviewer cannot build unbound context; use the supplied frozen manifest.");
            Assert.Empty(f.Output.ToString());
            Assert.DoesNotContain("SENTINEL", f.Error.ToString(), StringComparison.Ordinal);
            Assert.False(System.IO.File.Exists(outputPath));
        };
        Assert.True(await LaunchReview(outer) == 0, f.Error.ToString());
        Assert.Equal(2, callbacks);
        var final = await f.State();
        foreach (var id in new[] { "RD", "RAB" })
        {
            Assert.Equal(AgentRunStatus.Completed, final.Runs[new RunId(id)].Status);
            Assert.Equal("session-" + id, final.Runs[new RunId(id)].ProviderSessionId);
            var artifact = final.Artifacts[new ArtifactId("OUT-" + id)];
            Assert.Equal(GovernedArtifactKind.CodeReviewOutput, artifact.Kind);
            Assert.Equal(id == "RD" ? new[] { "D" } : ["A", "B"],
                WorkCoverage.Effective(artifact.WorkItemId, artifact.Assurance).Select(i => i.Value));
        }
    }

    [Fact]
    public async Task BuildForRunRejectsMissingWrongActorAndTerminalRunsBeforeProjection()
    {
        using var f = await BundleFixture.CreateAsync();
        var assembler = new ContextAssembler();
        var state = await f.State();
        var missing = Assert.Throws<GovernanceException>(() => assembler.BuildForRun(state,
            new ActorId("verifier"), new RunId("MISSING"), null!, DateTimeOffset.UtcNow));
        Assert.Equal("Context run 'MISSING': run does not exist.", missing.Message);

        await f.Ok("run", "start", "--subject", "verifier", "--run", "VACTIVE", "--work", "A", "--provider", "claude");
        state = await f.State();
        var wrongActor = Assert.Throws<GovernanceException>(() => assembler.BuildForRun(state,
            new ActorId("reviewer"), new RunId("VACTIVE"), null!, DateTimeOffset.UtcNow));
        Assert.Equal("Context run 'VACTIVE': run belongs to actor 'verifier', not 'reviewer'.", wrongActor.Message);
        await f.Ok("run", "complete", "--run", "VACTIVE", "--status", "failed");
        await f.Verify("VAB", "A", "B");
        state = await f.State();
        foreach (var id in new[] { "VACTIVE", "VAB" })
        {
            var terminal = Assert.Throws<GovernanceException>(() => assembler.BuildForRun(state,
                new ActorId("verifier"), new RunId(id), null!, DateTimeOffset.UtcNow));
            Assert.Equal($"Context run '{id}': run must be Active; current status is '{state.Runs[new RunId(id)].Status}'.", terminal.Message);
        }
    }

    [Fact]
    public async Task SelectedUnionExcludesUnrelatedContextAndDirectories()
    {
        using var f = await BundleFixture.CreateAsync(dependencies: true);
        await f.Verify("VC", "C");
        await f.Verify("VAB", "A", "B");
        var request = f.Adapter.Requests.Last();
        var manifest = JsonSerializer.Deserialize<ContextManifest>(request.StandardInput, ManifestJson)!;
        Assert.Contains(manifest.Artifacts, a => a.Id == "CA");
        Assert.Contains(manifest.Artifacts, a => a.Id == "CB");
        Assert.DoesNotContain(manifest.Artifacts, a => a.Id is "CC" or "EC" or "C" or "OUT-VC");
        Assert.DoesNotContain(Path.Combine(f.Repository, "C"), request.AdditionalDirectories);
        Assert.DoesNotContain(f.Repository, request.AdditionalDirectories);
    }

    [Fact]
    public async Task RepairReceivesApplicableBundleFindings()
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Verify("VC", "C");
        f.Adapter.ChangeResult = result => result with { Status = AgentRunStatus.Failed, ExitCode = 1, Failure = "failure after filing" };
        Assert.Equal(3, await f.Launch("VAB", ["A", "B"]));
        var state = await f.State();
        var brief = new ContextAssembler().Build(state, new ActorId("worker"), new WorkItemId("A"), [], DateTimeOffset.UtcNow);
        var artifact = Assert.Single(brief.Artifacts.Where(a => a.Id == "OUT-VAB"));
        Assert.Contains(ArtifactCommands.VerifierBody.Trim(), artifact.Content, StringComparison.Ordinal);
        Assert.Contains("A", artifact.RelatedIds);
        Assert.Contains("B", artifact.RelatedIds);
        Assert.Equal(AgentRunStatus.Failed, artifact.ProducerStatus);
        Assert.Equal(new[] { "A", "B" }, artifact.ApplicableWorkItemIds!.Select(i => i.Value));
        Assert.DoesNotContain(brief.Artifacts, a => a.Id == "OUT-VC");
    }

    // R1 (partial-applicability): production filings establish edges, not hand-built records.
    [Fact]
    public async Task R1_PartialRepairRetainsUnaffectedApplicability()
    {
        using var f = await BundleFixture.CreateAsync();
        await f.Verify("VAB", "B", "A");
        await f.Verify("VD", "D");
        await f.Verify("VA", "A");
        var state = await f.State();
        var ab = state.Artifacts[new ArtifactId("OUT-VAB")];
        Assert.Equal(new[] { "B" }, ArtifactApplicability.CurrentMembers(state, ab).Select(i => i.Value));
        Assert.Equal(new[] { "A", "B" }, ab.Assurance!.WorkItemIds.Select(i => i.Value));
        await f.Verify("VBC", "C", "B");
        state = await f.State();
        Assert.Empty(ArtifactApplicability.CurrentMembers(state, ab));
        Assert.DoesNotContain(ArtifactApplicability.Current(state), a => a.ArtifactId == ab.ArtifactId);
        foreach (var (member, artifact) in new[] { ("A", "OUT-VA"), ("B", "OUT-VBC"), ("C", "OUT-VBC"), ("D", "OUT-VD") })
            Assert.Equal(artifact, Assert.Single(ArtifactApplicability.Current(state).Where(a => a.Kind == GovernedArtifactKind.VerifierOutput && ArtifactApplicability.CurrentMembers(state, a).Contains(new WorkItemId(member)))).ArtifactId.Value);
        var bc = state.Artifacts[new ArtifactId("OUT-VBC")];
        Assert.Equal(new ArtifactId("OUT-VAB"), Assert.Single(bc.MemberReplacements!.Single(r => r.WorkItemId == new WorkItemId("B")).ArtifactIds));
        Assert.Empty(bc.MemberReplacements!.Single(r => r.WorkItemId == new WorkItemId("C")).ArtifactIds);
        Assert.Null(bc.SupersedesArtifactId);
        Assert.Equal(4, state.Artifacts.Values.Count(a => a.Kind == GovernedArtifactKind.VerifierOutput));
    }

    [Fact]
    public async Task InheritedCoverageAndGeneratedFilingAreExact()
    {
        using var f = await BundleFixture.CreateAsync();
        f.Adapter.FileOutput = false;
        f.Adapter.BeforeResult = async request =>
        {
            Assert.Equal(1, await f.File("verifier", "VAB", "BAD", "verifier-output", "--work", "A"));
            Assert.Contains("Assurance artifact coverage:", f.Error.ToString());
            Assert.Equal(1, await f.File("verifier", "VAB", "BAD", "verifier-output", "--work", "A", "--also-work", "B", "--also-work", "C"));
            Assert.Contains("Assurance artifact coverage:", f.Error.ToString());
            var process = new ScriptedProcessRunner()
                .Enqueue(0, ["codex-cli 1.2.3"])
                .Enqueue(0, ["--strict-config --sandbox --cd --add-dir --output-schema --json"])
                .Enqueue(0, ["SESSION_ID --json"])
                .Enqueue(0, ["{\"type\":\"thread.started\",\"thread_id\":\"filing-session\"}", "{\"type\":\"turn.completed\"}"]);
            await new CodexAgentAdapter(process, (_, _, _, _, _) => Task.CompletedTask)
                .RunAsync(request with { Provider = "codex" }, CancellationToken.None);
            var line = Assert.Single(process.Invocations.Last().StandardInput.Split('\n').Where(l => l.StartsWith("  " + request.LedgerCommandLine, StringComparison.Ordinal) && l.Contains(" artifact record ", StringComparison.Ordinal)));
            var command = line[(line.IndexOf("artifact record", StringComparison.Ordinal))..];
            command = command[..command.IndexOf(" < FILE", StringComparison.Ordinal)]
                .Replace("--id ID", "--id OUT-VAB", StringComparison.Ordinal)
                .Replace("--title TEXT", "--title fixture", StringComparison.Ordinal);
            Assert.DoesNotContain("--work", command, StringComparison.Ordinal);
            using var input = new StandardInput(ArtifactCommands.VerifierBody);
            Assert.Equal(0, await f.App.RunAsync([.. command.Split(' ', StringSplitOptions.RemoveEmptyEntries), "--root", f.Root], CancellationToken.None));
        };
        await f.Verify("VAB", "A", "B");
        var state = await f.State();
        Assert.DoesNotContain(new ArtifactId("BAD"), state.Artifacts.Keys);
        Assert.Equal(new[] { "A", "B" }, state.Artifacts[new ArtifactId("OUT-VAB")].Assurance!.WorkItemIds.Select(i => i.Value));
    }

    [Theory]
    [InlineData("missing-output")]
    [InlineData("adapter-throw")]
    [InlineData("wrong-id")]
    [InlineData("cancelled")]
    public async Task LaunchFailuresRetainResultAndRecoverEveryMember(string failure)
    {
        using var f = await BundleFixture.CreateAsync();
        f.Adapter.FileOutput = false;
        if (failure == "adapter-throw") f.Adapter.BeforeResult = _ => throw new IOException("injected adapter failure");
        if (failure == "cancelled") f.Adapter.ChangeResult = result => result with { Status = AgentRunStatus.Cancelled, ExitCode = -1 };
        if (failure == "wrong-id") f.Adapter.ChangeResult = result => result with { RunId = new RunId("WRONG") };
        Assert.NotEqual(0, await f.Launch("VAB", ["A", "B"]));
        var state = await f.State();
        Assert.Equal(failure == "cancelled" ? AgentRunStatus.Cancelled : AgentRunStatus.Failed, state.Runs[new RunId("VAB")].Status);
        Assert.All(new[] { "A", "B" }, member => Assert.Equal(WorkItemStatus.Paused, state.WorkItems[new WorkItemId(member)].Status));
        Assert.Empty(state.Artifacts.Values.Where(a => a.Assurance is not null));
        Assert.Equal(1, await f.Run("work", "complete", "--id", "B"));
        Assert.Contains("verifier", f.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        if (failure == "missing-output") Assert.Equal("session-VAB", state.Runs[new RunId("VAB")].ProviderSessionId);
    }

    private static Task RefusesWithoutMutation(BundleFixture f, Func<Task<int>> action, params string[] diagnostics) =>
        RefusesWithoutMutation(f, action, 1, diagnostics);

    private static async Task RefusesWithoutMutation(BundleFixture f, Func<Task<int>> action, int expectedExit, params string[] diagnostics)
    {
        var before = await f.State();
        var probes = f.Adapter.Probes;
        var calls = f.Adapter.Requests.Count;
        Assert.True(await action() == expectedExit, f.Error.ToString());
        foreach (var diagnostic in diagnostics) Assert.Contains(diagnostic, f.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        var after = await f.State();
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(probes, f.Adapter.Probes);
        Assert.Equal(calls, f.Adapter.Requests.Count);
        Assert.Equal(JsonSerializer.Serialize(before.WorkItems, ManifestJson), JsonSerializer.Serialize(after.WorkItems, ManifestJson));
    }
}
