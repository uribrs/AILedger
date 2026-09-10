using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// The gate: an event nothing requires is ignorable, so adding work and launching a provider are
// refused until the acting actor has been briefed. What these pin most carefully is where the gate
// must NOT reach — 'context build' itself, and a run nobody dispatched.
public sealed class PipelineGateTests
{
    // R3 (the-gate-must-not-lock-the-operator-out). A fresh task has no work item, and adding one
    // needs a brief. If building the brief were itself gated the task could never be opened at all,
    // which is worse than the hole the gate closes.
    [Fact]
    public async Task AnOperatorCanBuildContextOnATaskWithNoWorkItem()
    {
        using var root = new TemporaryDirectory();
        var application = Application();
        Assert.Equal(0, await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None));

        var manifestPath = Path.Combine(Path.GetTempPath(), $"manifest-{Guid.NewGuid():N}.json");
        var exit = await application.RunAsync(
            ["context", "build", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--cognitive-root", ContextBrief.CognitiveRoot(), "--output", manifestPath],
            CancellationToken.None);
        File.Delete(manifestPath);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, exit);
        Assert.Empty(state!.WorkItems);
        Assert.Contains(new ActorId("operator"), state.ContextBuilds.Keys);
    }

    [Fact]
    public void AddingWorkIsRefusedUntilTheActingActorHasBuiltItsContext()
    {
        var task = new TestTask { AutoBuildContext = false };

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [])));

        // The refusal has to name the command that fixes it. A gate that says only "no" leaves the
        // actor to guess, and a guess at a governance command is a second refusal.
        Assert.Contains("has not built its context", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(
            "ailedger context build --task task-1 --actor operator", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddingWorkIsAllowedOnceTheActingActorHasBuiltItsContext()
    {
        var task = new TestTask { AutoBuildContext = false };
        task.BuildContext(task.OperatorId);

        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], []));

        Assert.Contains(new WorkItemId("W1"), task.State.WorkItems.Keys);
    }

    // R4's other half. The event records what each skill said, and the gate reads it: a brief built
    // against a skill that has since been edited is not a current brief.
    [Fact]
    public void AddingWorkIsRefusedWhenTheServedSkillsNoLongerMatchTheRecordedBrief()
    {
        var task = new TestTask { AutoBuildContext = false };
        task.BuildContext(task.OperatorId);

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [],
            SkillsServedNow: [
                new ContextSkill("workflow-coordinator", "hash-of-workflow-coordinator"),
                new ContextSkill("task-orchestrator", "an-edited-orchestrator")])));

        Assert.Contains("have changed since its context was built", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddingWorkIsRefusedWhenTheBriefServedTheSkillsInAnotherOrder()
    {
        var task = new TestTask { AutoBuildContext = false };
        task.BuildContext(task.OperatorId, "task-orchestrator", "workflow-coordinator");

        Assert.Throws<GovernanceException>(() => task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [],
            SkillsServedNow: [
                new ContextSkill("workflow-coordinator", "hash-of-workflow-coordinator"),
                new ContextSkill("task-orchestrator", "hash-of-task-orchestrator")])));
    }

    // The gate is on the launch, not on every run. A run started by hand records no launch token,
    // and gating it would reach far wider than the two commands the change is about (IC3).
    [Fact]
    public void AManuallyStartedRunIsNotGated()
    {
        var task = new TestTask { AutoBuildContext = false };
        task.BuildContext(task.OperatorId);
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], []));
        var unbriefed = new ActorId("lead");
        task.Assign(unbriefed, RoleKind.ImplementationLead, Capability.ManageRuns, Capability.BuildContext);

        task.Apply(new StartRunCommand(
            unbriefed, null, task.NextCorrelation(), new RunId("R1"), new WorkItemId("W1"), "codex", null));

        Assert.Contains(new RunId("R1"), task.State.Runs.Keys);
    }

    [Fact]
    public void AProviderLaunchIsRefusedUntilTheDispatchingActorHasBuiltItsContext()
    {
        var task = new TestTask { AutoBuildContext = false };
        task.BuildContext(task.OperatorId);
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], []));
        var dispatcher = new ActorId("lead");
        task.Assign(dispatcher, RoleKind.ImplementationLead, Capability.ManageRuns, Capability.BuildContext);

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new StartRunCommand(
            dispatcher, null, task.NextCorrelation(), new RunId("R1"), new WorkItemId("W1"), "codex", null,
            null, null, CommandHandler.HashLaunchToken("a-launch-token"))));

        Assert.Contains("cannot launch a provider", refusal.Message, StringComparison.Ordinal);
    }

    // The gate must not speak before the rule that cannot be satisfied at all. An actor that may
    // never dispatch should be told that, not told to go and build context first.
    [Fact]
    public void ANonOperatorDispatchingForAnotherActorIsRefusedOnAuthorityRatherThanOnTheGate()
    {
        var task = new TestTask { AutoBuildContext = false };
        task.BuildContext(task.OperatorId);
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], []));
        var lead = new ActorId("lead");
        var verifier = new ActorId("verifier");
        task.Assign(lead, RoleKind.ImplementationLead, Capability.ManageRuns, Capability.BuildContext);
        task.Assign(verifier, RoleKind.Verifier, Capability.BuildContext);

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new StartRunCommand(
            lead, null, task.NextCorrelation(), new RunId("R1"), new WorkItemId("W1"), "codex", null,
            null, null, CommandHandler.HashLaunchToken("a-launch-token"), verifier)));

        Assert.Contains("on another actor's behalf", refusal.Message, StringComparison.Ordinal);
    }

    private static CliApplication Application() => new(
        TextWriter.Null,
        TextWriter.Null,
        Service,
        _ => throw new InvalidOperationException("No provider is launched here."),
        new ContextAssembler());

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }
}
