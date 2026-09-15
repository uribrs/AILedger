using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Constraints;

public sealed class ConstraintRulesTests
{
    [Fact]
    public void AddingAConstraintNormalizesItsContentAndStartsActive()
    {
        var task = new TestTask();

        task.Apply(new AddConstraintCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            new ConstraintId("K1"),
            " Keep persistence local ",
            " architecture decision ",
            [" src ", " tests "]));

        var constraint = task.State.Constraints[new ConstraintId("K1")];
        Assert.Equal("Keep persistence local", constraint.Statement);
        Assert.Equal("architecture decision", constraint.Source);
        Assert.Equal(["src", "tests"], constraint.Scope);
        Assert.Equal(ConstraintStatus.Active, constraint.Status);
    }

    [Fact]
    public void AddingAConstraintRequiresStatementSourceAndValidScopeEntries()
    {
        var task = new TestTask();

        Assert.Throws<GovernanceException>(() => Add(task, "K1", statement: " "));
        Assert.Throws<GovernanceException>(() => Add(task, "K2", source: " "));
        Assert.Throws<GovernanceException>(() => Add(task, "K3", scope: ["src", " "]));
        Assert.Throws<GovernanceException>(() => Add(task, "K4", scope: ["src", "src"]));
    }

    [Fact]
    public void OnlyAnOperatorMayManageConstraints()
    {
        var task = PlanningLeadTask.Create(out var lead);

        Assert.Throws<GovernanceException>(() => task.Apply(new AddConstraintCommand(
            lead,
            null,
            task.NextCorrelation(),
            new ConstraintId("K1"),
            "Keep persistence local",
            "architecture decision",
            [])));

        Add(task, "K1");

        Assert.Throws<GovernanceException>(() => task.Apply(new SupersedeConstraintCommand(
            lead,
            null,
            task.NextCorrelation(),
            new ConstraintId("K1"))));
    }

    [Fact]
    public void OnlyAnActiveConstraintCanBeSuperseded()
    {
        var task = new TestTask();
        Add(task, "K1");

        task.Apply(new SupersedeConstraintCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            new ConstraintId("K1")));

        Assert.Equal(ConstraintStatus.Superseded, task.State.Constraints[new ConstraintId("K1")].Status);
        Assert.Throws<GovernanceException>(() => task.Apply(new SupersedeConstraintCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            new ConstraintId("K1"))));
    }

    private static void Add(
        TestTask task,
        string id,
        string statement = "Keep persistence local",
        string source = "architecture decision",
        IReadOnlyList<string>? scope = null) =>
        task.Apply(new AddConstraintCommand(
            task.OperatorId,
            null,
            task.NextCorrelation(),
            new ConstraintId(id),
            statement,
            source,
            scope ?? []));
}
