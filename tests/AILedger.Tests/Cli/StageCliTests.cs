using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Cli;

[Collection(StandardInput.Collection)]
public sealed class StageCliTests
{
    // Constraint K8: the stage arms are the operator's to waive, and the waiver carries a reason
    // rather than being a flag. These five drive it through the CLI, because that is where the
    // option is parsed and where the per-command allow-list would otherwise refuse it unread.
    // Discovery -> Research is the only exit from Discovery and its arm needs an open claim, so a
    // task with no claims is a stage whose arm is unsatisfied without any setup.
    [Fact]
    public async Task WaivingStagePrerequisitesIsRefusedForAnActorWhoIsNotTheOperator()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        // An implementation lead holds requestTransition, so what is refused below is the waiver and
        // not the authority to ask for a transition at all.
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "lead", "--role", "implementation-lead"],
            CancellationToken.None);

        var exit = await application.RunAsync(
            ["stage", "transition", "--root", root.Path, "--task", "T1", "--actor", "lead",
             "--stage", "research", "--without-prerequisites", "The lead read the arm as inapplicable"],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Contains(
            "Only an operator can transition stages without prerequisites",
            error.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(TaskStage.Discovery, state!.Stage);
    }

    [Fact]
    public async Task WaivingStagePrerequisitesIsRefusedWhenTheReasonIsBlank()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);

        // Whitespace rather than an empty string: the option consumes the following argument either
        // way, and a waiver whose reason records nothing is the one the log cannot be read from.
        var exit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research", "--without-prerequisites", "   "],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Contains("a blank waiver records nothing", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(TaskStage.Discovery, state!.Stage);
    }

    [Fact]
    public async Task AnOperatorWaivesTheArmAndTheReasonLandsOnItsOwnEvent()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);

        var exit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research",
             "--without-prerequisites", "Discovery closed with no open claim and the wave is scoped"],
            CancellationToken.None);

        var events = await HistoryAsync(root.Path);
        var waiver = events.Single(item => item.Data is StagePrerequisitesWaived);
        var transition = events.Single(item => item.Data is StageTransitioned);
        var waived = (StagePrerequisitesWaived)waiver.Data;
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal("Discovery closed with no open claim and the wave is scoped", waived.Reason);
        Assert.Equal(TaskStage.Research, waived.TargetStage);
        // The reason is its own event, and the transition cites it, so a later reader sees which arm
        // was skipped and why rather than only that a transition happened.
        Assert.Equal(waiver.EventId, transition.CausationId);
    }

    [Fact]
    public async Task AWaivedTransitionReplaysThroughAFreshServiceWithItsReasonIntact()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research",
             "--without-prerequisites", "Discovery closed with no open claim and the wave is scoped"],
            CancellationToken.None);

        // A fresh service holds no state, so reading it replays the whole log through the validator.
        // The waiver has to be legal at replay too, and its reason has to survive the round trip.
        var replayed = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var waived = (StagePrerequisitesWaived)(await HistoryAsync(root.Path))
            .Single(item => item.Data is StagePrerequisitesWaived).Data;

        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(TaskStage.Research, replayed!.Stage);
        Assert.Equal("Discovery closed with no open claim and the wave is scoped", waived.Reason);
    }

    [Fact]
    public async Task AStageTransitionWithNoWaiverStillRefusesWhenItsArmIsUnsatisfied()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);

        // The same operator and the same transition as the accepted case above, without the waiver.
        // If this passed, the waiver would have become the default path rather than an override.
        var exit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research"], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Contains(
            "Research requires at least one open claim", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(TaskStage.Discovery, state!.Stage);
    }

    // A move back through the pipeline says something was learned that invalidates work already
    // done, and --reason is where that sentence goes. These four drive it through the CLI because
    // that is the only level at which the option can be shown to exist: an option the parser does
    // not know is refused as a usage error before the kernel ever sees the command, so a rule
    // tested only in the kernel can be complete while the flag it reads is unreachable.
    //
    // The walk is Discovery -> Research -> Discovery. Discovery is the one stage with no arm of its
    // own, so the backward leg is refused for its reason and never for a prerequisite.
    [Fact]
    public async Task ABackwardStageTransitionCarriesItsReasonOntoTheRecordedTransition()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["claim", "add", .. common, "--id", "C1", "--statement", "The arm is satisfiable"],
            CancellationToken.None);
        var forwardExit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research"], CancellationToken.None);

        var backwardExit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "discovery",
             "--reason", "The open claim turned out to rest on an assumption nobody had recorded"],
            CancellationToken.None);

        // Read back off disk through a fresh service, so the reason has survived being written and
        // replayed rather than only having been accepted.
        var transitions = (await HistoryAsync(root.Path))
            .Select(item => item.Data)
            .OfType<StageTransitioned>()
            .ToArray();
        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, forwardExit);
        Assert.Equal(0, backwardExit);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(TaskStage.Discovery, state!.Stage);
        Assert.Equal(2, transitions.Length);
        // The forward leg carries none. LedgerJson omits a null field entirely, so this is the half
        // that proves an absent reason deserialises as absent and not as something else.
        Assert.Equal(TaskStage.Research, transitions[0].Current);
        Assert.Null(transitions[0].Reason);
        Assert.Equal(TaskStage.Discovery, transitions[1].Current);
        Assert.Equal(
            "The open claim turned out to rest on an assumption nobody had recorded",
            transitions[1].Reason);
    }

    [Fact]
    public async Task ABackwardStageTransitionIsRefusedWithNoReason()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["claim", "add", .. common, "--id", "C1", "--statement", "The arm is satisfiable"],
            CancellationToken.None);
        await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research"], CancellationToken.None);

        var exit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "discovery"], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Contains("goes back in the pipeline and needs a reason", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(TaskStage.Research, state!.Stage);
    }

    [Fact]
    public async Task ABackwardStageTransitionIsRefusedWhenTheReasonIsBlank()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["claim", "add", .. common, "--id", "C1", "--statement", "The arm is satisfiable"],
            CancellationToken.None);
        await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research"], CancellationToken.None);

        // Whitespace rather than an empty string, as with the waiver above: the option consumes the
        // following argument either way, and a reason that records nothing is the one a later reader
        // cannot learn anything from.
        var exit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "discovery", "--reason", "   "],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Contains("a blank reason records nothing", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(TaskStage.Research, state!.Stage);
    }

    // The forward half, and the one test that pins the option onto the parser's allow-list. A
    // refusal from the kernel exits 1; an option the command does not declare exits 2 without the
    // kernel running at all. Asserting the code and the absence of the usage wording is what tells
    // "the rule refused it" from "the flag was never wired".
    [Fact]
    public async Task AForwardStageTransitionRefusesAReasonRatherThanIgnoringIt()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["claim", "add", .. common, "--id", "C1", "--statement", "The arm is satisfiable"],
            CancellationToken.None);

        var exit = await application.RunAsync(
            ["stage", "transition", .. common, "--stage", "research",
             "--reason", "Discovery is finished and the wave is scoped"], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.DoesNotContain("Unknown option", error.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            "goes forward and does not take a reason", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(TaskStage.Discovery, state!.Stage);
    }

}
