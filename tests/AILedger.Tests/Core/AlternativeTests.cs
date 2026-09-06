using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class AlternativeTests
{
    [Fact]
    public void RecordingARejectedAlternativeRequiresItsRationale()
    {
        var task = EscalationTests.Prepare(out var lead);

        Assert.Throws<GovernanceException>(() => task.Apply(new RecordAlternativeCommand(
            lead, null, task.NextCorrelation(), new AlternativeId("ALT1"), "Use a database", "   ", null)));

        task.Apply(new RecordAlternativeCommand(
            lead, null, task.NextCorrelation(), new AlternativeId("ALT1"),
            "Use a database", "Files plus locking are inspectable and enough at this scale", null));

        Assert.Equal("Use a database", task.State.Alternatives[new AlternativeId("ALT1")].Statement);
    }

    [Fact]
    public void AnAlternativeRejectedBeforeAnyDecisionExistsStillReachesLaterContext()
    {
        var task = EscalationTests.Prepare(out var lead);
        task.Apply(new RecordAlternativeCommand(
            lead, null, task.NextCorrelation(), new AlternativeId("ALT1"),
            "Rewrite the workflow in LangGraph", "It buys a state machine we already have", null));

        // Work arrives afterwards, and narrowing to it must not hide what was already killed.
        var workItemId = new WorkItemId("W1");
        task.Apply(new AddWorkItemCommand(task.OperatorId, null, task.NextCorrelation(), workItemId,
            "Build the kernel", lead, [], [Path.GetFullPath("src")]));

        var manifest = new ContextAssembler().Build(
            task.State, lead, workItemId, [], DateTimeOffset.UnixEpoch);

        var artifact = Assert.Single(manifest.Artifacts,
            item => item.Kind == ContextArtifactKind.Alternative && item.Id == "ALT1");
        Assert.Contains("LangGraph", artifact.Content, StringComparison.Ordinal);
    }
}
