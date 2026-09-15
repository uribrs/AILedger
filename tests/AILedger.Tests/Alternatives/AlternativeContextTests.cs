using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Tests.Support;

namespace AILedger.Tests.Alternatives;

public sealed class AlternativeContextTests
{
    [Fact]
    public void AnAlternativeRejectedBeforeAnyDecisionExistsStillReachesLaterContext()
    {
        var task = PlanningLeadTask.Create(out var lead);
        task.Apply(new RecordAlternativeCommand(
            lead,
            null,
            task.NextCorrelation(),
            new AlternativeId("ALT1"),
            "Rewrite the workflow in LangGraph",
            "It buys a state machine we already have",
            null));

        // Work arrives afterwards, and narrowing to it must not hide what was already killed.
        var workItemId = new WorkItemId("W1");
        task.Apply(new AddWorkItemCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            workItemId,
            "Build the kernel",
            lead,
            [],
            [Path.GetFullPath("src")]));

        var manifest = new ContextAssembler().Build(
            task.State,
            lead,
            workItemId,
            [],
            DateTimeOffset.UnixEpoch);

        var artifact = Assert.Single(
            manifest.Artifacts,
            item => item.Kind == ContextArtifactKind.Alternative && item.Id == "ALT1");
        Assert.Contains("LangGraph", artifact.Content, StringComparison.Ordinal);
    }
}
