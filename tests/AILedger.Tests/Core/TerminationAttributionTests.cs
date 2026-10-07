using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class TerminationAttributionTests
{
    [Fact]
    public void EvenOperatorNamedProviderCannotManufactureTrustedTermination()
    {
        var task = new TestTask();
        var run = new RunId("R1");
        task.Apply(new StartRunCommand(task.OperatorId, null, "start", run, null, "codex", null));
        var session = new AILedger.Core.Authority.AgentSessionAuthority(task.TaskId, task.State.Roles[task.OperatorId], run, "codex", DateTimeOffset.MaxValue);
        var forged = new AddEvidenceCommand(task.OperatorId, null, run.Value, new("E1"), "process-termination", "task-1/R1", "Agent claims stopped", [], []);
        Assert.Throws<GovernanceException>(() => session.EnsureAllowed(task.State, forged, DateTimeOffset.UtcNow));
        session.EnsureAllowed(task.State, forged with { SourceType = "local-probe" }, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void WorkerObservationCannotBecomeTrustedThroughPromotionOrForgedReplayField()
    {
        var task = new TestTask(); var worker = new ActorId("worker");
        task.Apply(new AssignRoleCommand(task.OperatorId, null, task.NextCorrelation(), worker, RoleKind.Worker, [Capability.AddEvidence]));
        var before = task.State;
        var recorded = task.Apply(new AddEvidenceCommand(worker, null, task.NextCorrelation(), new("E1"), "process-termination", "task-1/R1", "Worker claims stop", [], []));
        var row = Assert.Single(recorded.Events);
        var evidence = Assert.IsType<EvidenceAdded>(row.Data).Evidence;
        Assert.Null(evidence.TrustedTerminationVersion);
        // Legal older shape stays legal; adding a forged present field does not.
        Assert.NotNull(new TaskReducer().Apply(before, row).Evidence[new("E1")]);
        Assert.Throws<GovernanceException>(() => new TaskReducer().Apply(before,
            row with { Data = new EvidenceAdded(evidence with { TrustedTerminationVersion = 1 }) }));
        task.Apply(new AssignRoleCommand(task.OperatorId, null, task.NextCorrelation(), worker, RoleKind.Operator, [Capability.AddEvidence]));
        Assert.Null(task.State.Evidence[new("E1")].TrustedTerminationVersion);
        task.Apply(new AddEvidenceCommand(task.OperatorId, null, task.NextCorrelation(), new("E2"), "process-termination", "task-1/R1", "Trusted observed stop", [], []));
        Assert.Equal(1, task.State.Evidence[new("E2")].TrustedTerminationVersion);
    }
}
