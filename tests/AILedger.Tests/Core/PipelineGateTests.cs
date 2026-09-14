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

    // The same rule where it is authoritative, without the CLI. A command that names no served
    // skills is a caller that cannot show the brief is current, whatever the reason, and the answer
    // is a refusal rather than the presence check alone.
    [Fact]
    public void AddingWorkIsRefusedWhenTheCommandNamesNoServedSkills()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };
        task.BuildContext(task.OperatorId);

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [])));

        Assert.Contains("cannot be read", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(
            "ailedger context build --task task-1 --actor operator", refusal.Message, StringComparison.Ordinal);
    }

    // The empty case is the same hole with a manifest in front of it: nothing served and nothing
    // recorded compare equal, so a cognitive layer that serves no skill would satisfy a mandate
    // about content.
    [Fact]
    public void AddingWorkIsRefusedWhenTheActorWasServedNoSkillsAtAll()
    {
        var task = new TestTask { AutoBuildContext = false };
        task.Apply(new RecordContextBuiltCommand(
            task.OperatorId, null, task.NextCorrelation(), null, []));

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [],
            SkillsServedNow: [])));

        Assert.Contains("was served no skills", refusal.Message, StringComparison.Ordinal);
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
    //
    // The unbriefed actor used to be an ImplementationLead, which is now refused on a different
    // rule entirely — a coordinating role may not hold a run against a work item. The role was
    // never this test's subject, only the scaffolding that gave it an actor with run authority, so
    // it is a Worker here and the gate under test is the same one. The coordinating refusal has
    // its own tests below.
    [Fact]
    public void AManuallyStartedRunIsNotGated()
    {
        var task = new TestTask { AutoBuildContext = false };
        task.BuildContext(task.OperatorId);
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], []));
        var unbriefed = new ActorId("worker");
        task.Assign(unbriefed, RoleKind.Worker, Capability.ManageRuns, Capability.BuildContext);

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

    // VC1. A gate any caller softens by moving one flag is not a gate. This drives the CLI, because
    // the hole was only reachable there: the launcher swallowed an unreadable cognitive layer, the
    // freshness comparison had nothing to compare, and the presence check alone let the command
    // through. An operator that genuinely has no cognitive layer holds no brief to be mandatory
    // about, and the refusal says so.
    [Fact]
    public async Task AddingWorkIsRefusedWhenTheCognitiveRootCannotBeRead()
    {
        using var root = new TemporaryDirectory();
        using var emptyCognitiveRoot = new TemporaryDirectory();
        var application = Application();
        Assert.Equal(0, await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None));
        await ContextBrief.BuildAsync(root.Path, "T1");

        var error = new StringWriter();
        var exit = await new CliApplication(
            TextWriter.Null, error, Service,
            _ => throw new InvalidOperationException("No provider is launched here."),
            new ContextAssembler()).RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work", "--cognitive-root", emptyCognitiveRoot.Path],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Empty(state!.WorkItems);
        Assert.Contains("cannot be read", error.ToString(), StringComparison.Ordinal);
    }

    // VC3. The gate used to be consulted only by the command, and the launcher probes the provider's
    // version before it submits one — so an actor with no brief had already run the provider binary
    // by the time it was refused. Authority first, then the brief, then anything with a side effect.
    // The adapter factory throwing is the assertion: it is the launcher's first step towards a
    // process, and a refused launch must not reach it.
    [Fact]
    public async Task AProviderLaunchWithNoBriefStartsNoProcess()
    {
        using var root = new TemporaryDirectory();
        using var providerRoot = new TemporaryDirectory();
        var work = Directory.CreateDirectory(Path.Combine(providerRoot.Path, "provider-work")).FullName;
        var factoryCalls = 0;
        var application = new CliApplication(
            TextWriter.Null, TextWriter.Null, Service,
            _ =>
            {
                factoryCalls++;
                throw new InvalidOperationException("A refused launch must resolve no adapter.");
            },
            new ContextAssembler());
        Assert.Equal(0, await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None));

        var exit = await application.RunAsync(
            ["provider", "launch", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", work, "--cognitive-root", ContextBrief.CognitiveRoot()],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Equal(0, factoryCalls);
        Assert.Empty(state!.Runs);
    }

    // A coordinating role plans the work and dispatches it; it does not do it. Without this the
    // lead's own run counted as the item's working pass, so 'work complete' was satisfied by a pass
    // in which nobody worked — the gate reported a verification that had never happened. The
    // refusal text is asserted whole because it is the only instruction the refused actor gets:
    // it has to name which roles may be dispatched instead, and the one run a coordinating role
    // may still hold.
    [Theory]
    [InlineData(RoleKind.Operator)]
    [InlineData(RoleKind.PlanningLead)]
    [InlineData(RoleKind.ImplementationLead)]
    public void ACoordinatingRoleCannotHoldARunAgainstAWorkItem(RoleKind role)
    {
        var task = new TestTask();
        var workItemId = new WorkItemId("W1");
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId, "Work", null, [], []));
        var subject = Coordinator(task, role);

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "codex", null,
            null, null, null, subject)));

        Assert.Equal(
            $"A run against work item '{workItemId}' cannot be held by a coordinating role, and " +
            $"'{subject}' is a {role}. Dispatch a Worker, Researcher, Verifier or CodeReviewer " +
            "against the item, or start this run without --work to file task-wide artifacts.",
            refusal.Message);
        Assert.Empty(task.State.Runs);
    }

    // The case the rule deliberately preserves. The prompt contract and the orchestration plan need
    // a producer run and a lead is the only role that may author them, so a rule that refused every
    // coordinating run would leave those two documents with no way to be recorded at all.
    [Theory]
    [InlineData(RoleKind.Operator)]
    [InlineData(RoleKind.PlanningLead)]
    [InlineData(RoleKind.ImplementationLead)]
    public void ACoordinatingRoleHoldsARunThatNamesNoWorkItem(RoleKind role)
    {
        var task = new TestTask();
        var subject = Coordinator(task, role);

        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), null, "codex", null,
            null, null, null, subject));

        var run = task.State.Runs[new RunId("R1")];
        Assert.Equal(role, run.SubjectRole);
        Assert.Null(run.WorkItemId);
    }

    // The control. The refusal reaches the three coordinating roles and no further, or the kernel
    // would have closed the only door work items are actually worked through.
    [Fact]
    public void AWorkerStillHoldsARunAgainstAWorkItem()
    {
        var task = new TestTask();
        var workItemId = new WorkItemId("W1");
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId, "Work", null, [], []));
        var worker = new ActorId("worker");
        task.Assign(worker, RoleKind.Worker, Capability.BuildContext);

        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), workItemId, "codex", null,
            null, null, null, worker));

        var run = task.State.Runs[new RunId("R1")];
        Assert.Equal(RoleKind.Worker, run.SubjectRole);
        Assert.Equal(workItemId, run.WorkItemId);
    }

    // Where the refusal above has to speak from. The rule was first written inside StartRun, past
    // the pre-flight, so a provider launch resolved an executable and ran the provider binary for
    // its version and only then was refused — the probe's own error was what the dispatcher read.
    // ProviderLaunchPreflight.EnsurePermitted is the call the launcher makes before it resolves an
    // adapter, so a rule this call does not carry is a rule that costs a process every time it
    // fires. Refused here and refused nowhere earlier is the whole property: the test asserts the
    // refusal comes out of this function, and the control below asserts the same function still
    // returns for the launch that is allowed.
    [Fact]
    public void TheLaunchPreflightRefusesACoordinatingSubjectBeforeAnyProcessIsResolved()
    {
        var task = new TestTask();
        var workItemId = new WorkItemId("W1");
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), workItemId, "Work", null, [], []));
        var lead = new ActorId("implementation-lead");
        task.Assign(lead, RoleKind.ImplementationLead, Capability.BuildContext, Capability.ManageRuns);
        var servedNow = task.State.ContextBuilds[task.OperatorId].Skills;

        var refusal = Assert.Throws<GovernanceException>(() => ProviderLaunchPreflight.EnsurePermitted(
            task.State, task.OperatorId, lead, servedNow, workItemId: workItemId));

        Assert.Equal(
            $"A run against work item '{workItemId}' cannot be held by a coordinating role, and " +
            $"'{lead}' is a {RoleKind.ImplementationLead}. Dispatch a Worker, Researcher, Verifier or " +
            "CodeReviewer against the item, or start this run without --work to file task-wide " +
            "artifacts.",
            refusal.Message);
        Assert.Empty(task.State.Runs);
    }

    // The control, and the reason the pre-flight has to be handed the work item rather than assume
    // one. A lead filing the prompt contract and the orchestration plan launches against no item at
    // all, and that launch must still reach its provider.
    [Fact]
    public void TheLaunchPreflightPassesACoordinatingSubjectThatNamesNoWorkItem()
    {
        var task = new TestTask();
        var lead = new ActorId("implementation-lead");
        task.Assign(lead, RoleKind.ImplementationLead, Capability.BuildContext, Capability.ManageRuns);
        task.BuildContext(task.OperatorId);
        var servedNow = task.State.ContextBuilds[task.OperatorId].Skills;

        ProviderLaunchPreflight.EnsurePermitted(task.State, task.OperatorId, lead, servedNow);
    }

    // The operator already holds its role from the moment the task was opened, and an actor cannot
    // assign its own authority, so the operator case is the one that must not be assigned again.
    private static ActorId Coordinator(TestTask task, RoleKind role)
    {
        if (role == RoleKind.Operator)
        {
            return task.OperatorId;
        }

        var actor = new ActorId(role == RoleKind.PlanningLead ? "planning-lead" : "implementation-lead");
        task.Assign(actor, role, Capability.BuildContext, Capability.RecordArtifact);
        return actor;
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
