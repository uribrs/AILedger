using AILedger.Core.Application;
using AILedger.Core.Contracts;

namespace AILedger.Tests.ContextBriefing;

public sealed class ContextDependencyTests
{
    private static readonly ActorId Actor = new("worker");
    private static readonly Provenance Provenance = new(Actor, DateTimeOffset.UnixEpoch, "test");

    [Fact]
    public void SelectedDecisionIncludesAllItsPrerequisitesAndTheirEvidence()
    {
        var state = State();
        var manifest = Build(state, "W1");

        Assert.Equal(["C1", "C2"], Ids(manifest, ContextArtifactKind.Claim));
        Assert.Equal(["D1"], Ids(manifest, ContextArtifactKind.Decision));
        Assert.Equal(["E1", "E2"], Ids(manifest, ContextArtifactKind.Evidence));
        Assert.Equal(["W1", "C1", "C2", "D1", "E1", "E2"], manifest.Artifacts.Take(6).Select(item => item.Id));
        // D2 touches C2 but was never a decision about W1. Do not take the graph's entire connected component.
        Assert.DoesNotContain(manifest.Artifacts, item => item.Id is "C3" or "D2" or "E3");
    }

    [Fact]
    public void ObsoleteRecordsAreExcludedButRejectedClaimsRemainUseful()
    {
        var state = State();
        var claims = state.Claims.ToDictionary(pair => pair.Key, pair => pair.Value);
        claims[new ClaimId("C3")] = claims[new ClaimId("C3")] with { Status = ClaimStatus.Superseded };
        claims[new ClaimId("C2")] = claims[new ClaimId("C2")] with { Status = ClaimStatus.Rejected };
        var decisions = state.Decisions.ToDictionary(pair => pair.Key, pair => pair.Value);
        decisions[new DecisionId("D2")] = decisions[new DecisionId("D2")] with { Status = DecisionStatus.Invalidated };
        decisions[new DecisionId("D3")] = Decision("D3", "C3") with { Status = DecisionStatus.Superseded };

        var manifest = Build(state with { Claims = claims, Decisions = decisions });

        Assert.Equal(["C1", "C2"], Ids(manifest, ContextArtifactKind.Claim));
        Assert.Equal(["D1"], Ids(manifest, ContextArtifactKind.Decision));
        Assert.Contains(manifest.Artifacts, item => item.Id == "C2" && item.Content.StartsWith("Rejected:"));
    }

    [Fact]
    public void AnExplicitStaleDependencyNamesItsReplacementAndDoesNotLoopOnACycle()
    {
        var state = State();
        var claims = state.Claims.ToDictionary(pair => pair.Key, pair => pair.Value);
        claims[new ClaimId("C1")] = claims[new ClaimId("C1")] with
            { Status = ClaimStatus.Superseded, SupersededByClaimId = new ClaimId("C3") };
        claims[new ClaimId("C3")] = claims[new ClaimId("C3")] with { SupersededByClaimId = new ClaimId("C1") };

        var manifest = Build(state with { Claims = claims }, "W1");

        Assert.Equal(["C1", "C2", "C3"], Ids(manifest, ContextArtifactKind.Claim));
        Assert.Contains(manifest.Artifacts, item => item.Id == "C1" && item.Content.Contains("Replaced by: C3"));
    }

    [Fact]
    public void CoverageIncludesDependenciesOfEverySelectedMemberExactlyOnce()
    {
        var state = State();
        var manifest = new ContextAssembler().Build(state, Actor, new WorkItemId("W1"), [],
            DateTimeOffset.UnixEpoch, [new WorkItemId("W1"), new WorkItemId("W2")]);

        Assert.Equal(["W1", "W2"], Ids(manifest, ContextArtifactKind.WorkItem));
        Assert.Equal(["C1", "C2", "C3"], Ids(manifest, ContextArtifactKind.Claim));
        Assert.Equal(["D1", "D2"], Ids(manifest, ContextArtifactKind.Decision));
        Assert.Equal(["E1", "E2", "E3"], Ids(manifest, ContextArtifactKind.Evidence));
    }

    private static ContextManifest Build(GovernedTaskState state, string? work = null) =>
        new ContextAssembler().Build(state, Actor, work is null ? null : new WorkItemId(work), [], DateTimeOffset.UnixEpoch);

    private static IEnumerable<string> Ids(ContextManifest manifest, ContextArtifactKind kind) =>
        manifest.Artifacts.Where(item => item.Kind == kind).Select(item => item.Id);

    private static Decision Decision(string id, params string[] claims) =>
        new(new DecisionId(id), id, DecisionStatus.Accepted, "reason", claims.Select(value => new ClaimId(value)).ToArray(), null, Provenance);

    private static GovernedTaskState State()
    {
        var claims = Enumerable.Range(1, 3).Select(index => new Claim(new ClaimId($"C{index}"),
            $"Claim {index}", ClaimStatus.Validated, [new EvidenceId($"E{index}")], null, Provenance));
        // E2 is attached through Claim.EvidenceIds rather than direction, which the old projection missed.
        var evidence = Enumerable.Range(1, 3).Select(index => new Evidence(new EvidenceId($"E{index}"),
            "test", "test.cs", "proof", index == 2 ? [] : [new ClaimId($"C{index}")], [], Provenance));
        var work = new[]
        {
            new WorkItem(new WorkItemId("W1"), "Work", Actor, WorkItemStatus.Proposed, [new ClaimId("C1")], []),
            new WorkItem(new WorkItemId("W2"), "Other work", Actor, WorkItemStatus.Proposed, [new ClaimId("C3")], [])
        };
        return new GovernedTaskState
        {
            TaskId = new TaskId("T1"), Title = "Task", Goal = "Goal",
            Roles = new Dictionary<ActorId, RoleAssignment> { [Actor] = new(Actor, RoleKind.Worker, [Capability.BuildContext], Provenance) },
            Claims = claims.ToDictionary(item => item.Id), Evidence = evidence.ToDictionary(item => item.Id),
            Decisions = new[] { Decision("D1", "C1", "C2"), Decision("D2", "C2", "C3") }.ToDictionary(item => item.Id),
            WorkItems = work.ToDictionary(item => item.Id)
        };
    }
}
