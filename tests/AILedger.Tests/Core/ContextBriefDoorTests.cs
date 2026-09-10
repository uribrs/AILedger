using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// The two doors. The gate must be unbypassable by accident and openable on purpose with a record:
// it was the only hard refusal in this kernel with no override, and within two hours of landing it
// locked every actor out of adding work and dispatching because another agent was rewriting the
// skills its hashes are taken over (C6).
//
// Two doors and not one, because an absent brief and a stale brief are different failures (C7). An
// absent brief can only be carried by an operator's decision — there is no evidence that an unread
// brief was read. A stale one can be carried by an evidence record, which is a citation a later
// reader can check.
public sealed class ContextBriefDoorTests
{
    // The operator door on 'work add'. What it buys is the item; what it costs is a permanent event
    // saying the brief was skipped and why, which is the same trade --without-verification makes.
    [Fact]
    public void AnOperatorCanAddWorkWithNoBriefByRecordingWhy()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };

        var outcome = task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [],
            WithoutBriefReason: "The skills are being rewritten and this item does not touch them"));

        // The waiver precedes the item it let through, so a reader sees which gate was opened
        // before it sees what the opening bought.
        var waived = Assert.IsType<ContextBriefWaived>(outcome.Events[0].Data);
        Assert.Equal("add work", waived.Action);
        Assert.Equal("The skills are being rewritten and this item does not touch them", waived.OperatorReason);
        Assert.Null(waived.StaleBriefEvidenceId);
        Assert.IsType<WorkItemAdded>(outcome.Events[1].Data);
        Assert.Contains(new WorkItemId("W1"), task.State.WorkItems.Keys);
    }

    [Fact]
    public void TheOperatorDoorIsRefusedWithoutAReason()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [],
            WithoutBriefReason: "   ")));

        Assert.Contains("a blank waiver records nothing", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(task.State.WorkItems);
    }

    // On 'provider launch', because that is the only one of the two gated commands where the check
    // can be reached: AuthorizationPolicy already refuses AddWorkItemCommand from any non-operator,
    // so the same test against 'work add' would pass on the authority refusal and prove nothing
    // about the door (DC1).
    [Fact]
    public void ANonOperatorCannotOpenTheOperatorDoorOnAProviderLaunch()
    {
        var task = new TestTask { AutoBuildContext = false };
        task.BuildContext(task.OperatorId);
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], []));
        var lead = new ActorId("lead");
        task.Assign(lead, RoleKind.ImplementationLead, Capability.ManageRuns, Capability.BuildContext);

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new StartRunCommand(
            lead, null, task.NextCorrelation(), new RunId("R1"), new WorkItemId("W1"), "codex", null,
            null, null, CommandHandler.HashLaunchToken("a-launch-token"), null, null,
            WithoutBriefReason: "I decided my own brief was not warranted")));

        Assert.Contains(
            "Only an operator can launch a provider without a brief", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(task.State.Runs);
    }

    // The conditional door. The kernel checks that the evidence record exists and that there is a
    // brief for it to be about, and never whether the reason is a good one — the same contract as
    // --not-split-because, which checks that an alternative exists and never that it justifies the
    // split. That asymmetry is what makes the door usable under time pressure while leaving a
    // citation a later reader can check.
    [Fact]
    public void AStaleBriefIsCarriedByAnEvidenceRecordOnTheTask()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };
        task.BuildContext(task.OperatorId);
        RecordEvidence(task, "E1");

        var outcome = task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [],
            SkillsServedNow: EditedSince,
            StaleBriefEvidenceId: new EvidenceId("E1")));

        var waived = Assert.IsType<ContextBriefWaived>(outcome.Events[0].Data);
        Assert.Equal(new EvidenceId("E1"), waived.StaleBriefEvidenceId);
        Assert.Null(waived.OperatorReason);
        Assert.Contains(new WorkItemId("W1"), task.State.WorkItems.Keys);
    }

    // An unknown evidence id is refused exactly as an unknown alternative id is. A citation nobody
    // can follow is not a citation, and accepting one would make the door the free pass the
    // operator door is deliberately not.
    [Fact]
    public void TheStaleDoorIsRefusedWhenTheEvidenceRecordDoesNotExist()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };
        task.BuildContext(task.OperatorId);

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [],
            SkillsServedNow: EditedSince,
            StaleBriefEvidenceId: new EvidenceId("E9"))));

        Assert.Contains("Unknown evidence 'E9'", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(task.State.WorkItems);
    }

    // The refusal that keeps the two doors apart. There is no evidence that an unread brief was
    // read, so the stale door cannot discharge an absent one — and the refusal names the two routes
    // that can, rather than leaving the actor to find them.
    [Fact]
    public void TheStaleDoorIsRefusedWhenTheActorHasNoBriefAtAll()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };
        RecordEvidence(task, "E1");

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [],
            SkillsServedNow: EditedSince,
            StaleBriefEvidenceId: new EvidenceId("E1"))));

        Assert.Contains("no context brief at all", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("ailedger context build", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("--without-brief", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(task.State.WorkItems);
    }

    [Fact]
    public void NeitherDoorMayBeOpenedWithTheOther()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };
        task.BuildContext(task.OperatorId);
        RecordEvidence(task, "E1");

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [],
            SkillsServedNow: EditedSince,
            WithoutBriefReason: "The skills are moving",
            StaleBriefEvidenceId: new EvidenceId("E1"))));

        Assert.Contains("different failures", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(task.State.WorkItems);
    }

    // The refusal an actor reads is the instruction it acts on. This is the case the doors were
    // written for — a brief that exists and is no longer current — so it names every way out, not
    // only the slowest.
    [Fact]
    public void TheStaleBriefRefusalNamesAllThreeRoutes()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };
        task.BuildContext(task.OperatorId);

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [],
            SkillsServedNow: EditedSince)));

        Assert.Contains("ailedger context build", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("--without-brief", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("--with-stale-brief", refusal.Message, StringComparison.Ordinal);
    }

    // And the case they were not. An actor holding no brief cannot use the stale door, so naming it
    // here would send the reader to a second refusal — the cost the doors exist to remove. Two
    // routes, and only the two that work.
    [Fact]
    public void TheAbsentBriefRefusalNamesOnlyTheTwoRoutesThatWork()
    {
        var task = new TestTask { AutoBuildContext = false, AutoServeSkills = false };

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [])));

        Assert.Contains("ailedger context build", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("--without-brief", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("--with-stale-brief", refusal.Message, StringComparison.Ordinal);
    }

    // A run started by hand is not gated, so there is nothing for a door to open. Recording a
    // waiver for a refusal that never happens is how an override stops being read as a decision.
    [Fact]
    public void ARunStartedByHandHasNoGateToWaive()
    {
        var task = new TestTask { AutoBuildContext = false };
        task.BuildContext(task.OperatorId);
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], []));

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), new RunId("R1"), new WorkItemId("W1"), "codex",
            null, null, null, null, null, null,
            WithoutBriefReason: "Not needed")));

        Assert.Contains("nothing to waive", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(task.State.Runs);
    }

    // Both doors through the real file store and back out of a fresh service, because every rule is
    // written twice and the replay copy has to agree with the command copy on real events. What it
    // must NOT have grown is the other direction — a replay rule requiring a brief or a waiver,
    // which would make all 36 existing tasks unreadable.
    [Fact]
    public async Task BothDoorsCommitAndReplayThroughTheFileStore()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("door-task");
        var actor = new ActorId("operator");
        var writer = Service(root.Path);

        await Run(writer, taskId, new OpenTaskCommand(actor, null, "d1", taskId, "Task", "Goal"));
        await Run(writer, taskId, new RecordContextBuiltCommand(
            actor, null, "d2", null, ContextBrief.Served));
        await Run(writer, taskId, new AddEvidenceCommand(
            actor, null, "d3", new EvidenceId("E1"), "source-read",
            "cognitive/skills/code-reviewer/SKILL.md", "The edited skill is not one this item reads", [], []));
        await Run(writer, taskId, new AddWorkItemCommand(
            actor, null, "d4", new WorkItemId("W1"), "On a stale brief", actor, [], [],
            SkillsServedNow: EditedSince, StaleBriefEvidenceId: new EvidenceId("E1")));
        await Run(writer, taskId, new AddWorkItemCommand(
            actor, null, "d5", new WorkItemId("W2"), "On no brief", actor, [], [],
            WithoutBriefReason: "The cognitive layer is mid-rewrite and cannot be read at all"));

        // A separate service instance models a separate process: nothing but events.jsonl carries
        // over, so the replay copy of the rules has to accept both waivers.
        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.NotNull(replayed);
        Assert.Contains(new WorkItemId("W1"), replayed.WorkItems.Keys);
        Assert.Contains(new WorkItemId("W2"), replayed.WorkItems.Keys);

        // Neither waiver is projected into state, so the log is the only place either can be read
        // back — which is the whole point of the doors leaving an event rather than a silent pass.
        var events = await File.ReadAllTextAsync(Path.Combine(root.Path, taskId.Value, "events.jsonl"));
        Assert.Contains("context.brief-waived", events, StringComparison.Ordinal);
        Assert.Contains("The cognitive layer is mid-rewrite and cannot be read at all", events, StringComparison.Ordinal);
    }

    // The flags reach the kernel. A door the kernel honours and the command line cannot spell is a
    // door nobody can open, which is the state the last two hours were spent in.
    [Fact]
    public async Task TheOperatorDoorIsReachableFromTheCommandLine()
    {
        using var root = new TemporaryDirectory();
        var application = new CliApplication(
            TextWriter.Null, TextWriter.Null, Service,
            _ => throw new InvalidOperationException("No provider is launched here."),
            new ContextAssembler());
        Assert.Equal(0, await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None));

        var exit = await application.RunAsync(
            ["work", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "W1", "--title", "Work",
             "--without-brief", "The cognitive layer is being rewritten by another agent"],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, exit);
        Assert.Contains(new WorkItemId("W1"), state!.WorkItems.Keys);
        Assert.Empty(state.ContextBuilds);
    }

    // What the cognitive layer serves after a skill has been edited: the same two skills, one of
    // them saying something else. Nothing in the kernel reads a skill's text, only whether the
    // digest a caller found still matches the one the brief recorded.
    private static readonly IReadOnlyList<ContextSkill> EditedSince =
    [
        new ContextSkill("workflow-coordinator", "hash-of-workflow-coordinator"),
        new ContextSkill("task-orchestrator", "an-edited-orchestrator")
    ];

    private static void RecordEvidence(TestTask task, string evidenceId) =>
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), new EvidenceId(evidenceId), "source-read",
            "cognitive/skills/task-orchestrator/SKILL.md",
            "The edited section is the one this work item does not read", [], []));

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }

    // No ContextBrief.WithServedSkills wrapper: every command here says for itself what the layer
    // serves now, because that is exactly what these tests are about.
    private static Task<CommandOutcome> Run(IGovernedTaskService service, TaskId taskId, LedgerCommand command) =>
        service.ExecuteAsync(taskId, command, CancellationToken.None);
}
