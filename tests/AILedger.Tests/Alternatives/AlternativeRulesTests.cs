using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Alternatives;

public sealed class AlternativeRulesTests
{
    [Fact]
    public void RecordingAnAlternativeRequiresAStatementAndRejectionRationale()
    {
        var task = PlanningLeadTask.Create(out var lead);

        Assert.Throws<GovernanceException>(() => Record(task, lead, "ALT1", statement: " "));
        Assert.Throws<GovernanceException>(() => Record(task, lead, "ALT2", rationale: " "));
    }

    [Fact]
    public void RecordingAnAlternativeNormalizesItsContentAndMayNameAReplacementDecision()
    {
        var task = PlanningLeadTask.Create(out var lead);
        task.Apply(new ProposeDecisionCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            new DecisionId("D1"),
            "Use files",
            "They are inspectable",
            [],
            null));

        Record(
            task,
            lead,
            "ALT1",
            statement: " Use a database ",
            rationale: " Files are sufficient ",
            replacedBy: new DecisionId("D1"));

        var alternative = task.State.Alternatives[new AlternativeId("ALT1")];
        Assert.Equal("Use a database", alternative.Statement);
        Assert.Equal("Files are sufficient", alternative.RejectionRationale);
        Assert.Equal(new DecisionId("D1"), alternative.ReplacedByDecisionId);
    }

    [Fact]
    public void AReplacementDecisionMustExist()
    {
        var task = PlanningLeadTask.Create(out var lead);

        Assert.Throws<GovernanceException>(() => Record(
            task,
            lead,
            "ALT1",
            replacedBy: new DecisionId("D-missing")));
    }

    [Fact]
    public void RecordingAnAlternativeRequiresTheCapability()
    {
        var task = new TestTask();
        var worker = new ActorId("worker");
        task.Assign(worker, RoleKind.Worker, Capability.BuildContext);

        Assert.Throws<GovernanceException>(() => Record(task, worker, "ALT1"));
    }

    private static void Record(
        TestTask task,
        ActorId actor,
        string id,
        string statement = "Use a database",
        string rationale = "Files are sufficient",
        DecisionId? replacedBy = null) =>
        task.Apply(new RecordAlternativeCommand(
            actor,
            null,
            task.NextCorrelation(),
            new AlternativeId(id),
            statement,
            rationale,
            replacedBy));
}
