using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Stages;

// A stage transition that goes back in the pipeline says something was learned that invalidates
// work already done, and the arrow alone does not say what. So a backward move now carries a
// reason, recorded on StageTransitioned itself rather than in a note beside it, and a forward move
// refuses one instead of dropping it silently.
//
// The hazard this whole file exists around: RequestStageTransitionCommand ends in two nullable
// strings that mean opposite things. WithoutPrerequisitesReason excuses a refused arm;
// Reason narrates a move that was allowed. A positional string binds to the first of them, so every
// command below passes both by name, and AWaiverIsNotAReasonAndABackwardTransitionStillNeedsOne
// pins that the kernel does not treat one as the other.
//
// Command-time only. Every stage.transitioned already on disk carries no reason, so the rule must
// not reach the replay validator; StagePrerequisiteReplayBoundaryTests holds that boundary.
public sealed class StageTransitionRequestTests
{
    private const string ReasonProperty = "\"reason\"";
    private static readonly DateTimeOffset When = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CommandTimeRefusesAStagePrerequisiteWaiverFromANonOperator()
    {
        var task = new TestTask();
        var lead = new ActorId("lead");
        task.Assign(lead, RoleKind.ImplementationLead, Capability.RequestTransition);
        var handler = new CommandHandler(new NonValidatingReducer(), new AuthorizationPolicy());

        var refusal = Assert.Throws<GovernanceException>(() => handler.Handle(
            task.State,
            new RequestStageTransitionCommand(
                lead, null, "waive-command-time", TaskStage.Research,
                WithoutPrerequisitesReason: "The arm is inapplicable"),
            When));

        Assert.Equal("Only an operator can transition stages without prerequisites.", refusal.Message);
    }

    [Fact]
    public void ReplayRefusesAStagePrerequisiteWaiverFromANonOperator()
    {
        var task = new TestTask();
        var lead = new ActorId("lead");
        task.Assign(lead, RoleKind.ImplementationLead, Capability.RequestTransition);
        var forged = new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{task.TaskId.Value}:{task.State.Version + 1:D10}"),
            task.TaskId,
            lead,
            When,
            null,
            "waive-replay",
            new StagePrerequisitesWaived(TaskStage.Research, "The arm is inapplicable"));

        var refusal = Assert.Throws<GovernanceException>(() => new TaskReducer().Apply(task.State, forged));

        Assert.Equal("Only an operator can transition stages without prerequisites.", refusal.Message);
    }

    [Fact]
    public void ABlankWaiverIsRefusedBeforeAnUnknownSerialJustification()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Execution);

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(
            new RequestStageTransitionCommand(
                task.OperatorId,
                null,
                task.NextCorrelation(),
                TaskStage.Verification,
                WithoutPrerequisitesReason: "   ",
                SerialJustification: new AlternativeId("ALT-missing"))));

        Assert.Equal(
            "Waiving stage prerequisites needs a reason; a blank waiver records nothing.",
            refusal.Message);
    }

    [Fact]
    public void ABackwardTransitionWithoutAReasonIsRefused()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Research);

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(
            new RequestStageTransitionCommand(
                task.OperatorId, null, task.NextCorrelation(), TaskStage.Discovery)));

        Assert.Equal(
            "Transition from 'Research' to 'Discovery' goes back in the pipeline and needs a reason. " +
            "Pass --reason to record what was learned that sends the work back.",
            refusal.Message);
        Assert.Equal(TaskStage.Research, task.State.Stage);
    }

    // A separate refusal from the one above, and it has to stay separate: a caller who passed
    // nothing forgot the rule, and a caller who passed "   " believes they satisfied it. Whitespace
    // rather than an empty string, because a shell argument that looks like a reason is the way this
    // arrives in practice.
    [Fact]
    public void ABackwardTransitionWithABlankReasonIsRefusedForSayingNothing()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Research);

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(
            new RequestStageTransitionCommand(
                task.OperatorId, null, task.NextCorrelation(), TaskStage.Discovery,
                Reason: "   ")));

        Assert.Equal("A backward transition needs a reason; a blank reason records nothing.", refusal.Message);
        Assert.Equal(TaskStage.Research, task.State.Stage);
    }

    // The test that separates carrying the value from merely validating it. A rule that refused the
    // reasonless move and then dropped the reason would pass every assertion above and leave the log
    // saying exactly what it said before the rule existed.
    [Fact]
    public void ABackwardTransitionRecordsItsReasonOnTheEventItEmits()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Research);

        var outcome = task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), TaskStage.Discovery,
            Reason: "The framing named the wrong subsystem, so there is nothing here to research"));

        var transitioned = (StageTransitioned)outcome.Events.Single(item => item.Data is StageTransitioned).Data;
        Assert.Equal(TaskStage.Discovery, task.State.Stage);
        Assert.Equal(TaskStage.Research, transitioned.Previous);
        Assert.Equal(TaskStage.Discovery, transitioned.Current);
        Assert.Equal(
            "The framing named the wrong subsystem, so there is nothing here to research",
            transitioned.Reason);

        // And on the log the task actually accumulated, not only on what the handler handed back.
        var logged = (StageTransitioned)task.Events.Last(item => item.Data is StageTransitioned).Data;
        Assert.Equal(transitioned.Reason, logged.Reason);
    }

    // The reason has to survive the wire, because the log is where it is read from. Asserted on the
    // deserialised event rather than by finding the property in the text: LedgerJson writes nothing
    // for a null, so the property is present only sometimes and its absence proves nothing on its own.
    [Fact]
    public void AReasonSurvivesTheRoundTripAndANullOneIsAbsentFromTheJsonEntirely()
    {
        var options = LedgerJson.CreateOptions();

        var withReason = JsonSerializer.Serialize<LedgerEventData>(
            new StageTransitioned(TaskStage.Repair, TaskStage.Verification, "The fix changed the contract"),
            options);
        var read = Assert.IsType<StageTransitioned>(
            JsonSerializer.Deserialize<LedgerEventData>(withReason, options));
        Assert.Equal("The fix changed the contract", read.Reason);
        Assert.Equal(TaskStage.Repair, read.Previous);
        Assert.Equal(TaskStage.Verification, read.Current);

        // A forward transition writes the same bytes it wrote before the field existed, which is
        // what keeps every stage.transitioned already on disk readable.
        var withoutReason = JsonSerializer.Serialize<LedgerEventData>(
            new StageTransitioned(TaskStage.Research, TaskStage.Design),
            options);
        Assert.DoesNotContain(ReasonProperty, withoutReason, StringComparison.Ordinal);
        var legacy = Assert.IsType<StageTransitioned>(
            JsonSerializer.Deserialize<LedgerEventData>(withoutReason, options));
        Assert.Null(legacy.Reason);
    }

    // A forward move refuses a reason rather than ignoring it. A caller who passed one meant it to
    // be recorded, and a reason silently dropped reads in the log exactly like one never written.
    [Fact]
    public void AForwardTransitionRefusesAReasonRatherThanDroppingIt()
    {
        var task = new TestTask();
        task.RecordResearchTopic();

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(
            new RequestStageTransitionCommand(
                task.OperatorId, null, task.NextCorrelation(), TaskStage.Research,
                Reason: "Starting the research")));

        Assert.Equal(
            "Transition from 'Discovery' to 'Research' goes forward and does not take a reason. " +
            "Only a transition that goes back records why.",
            refusal.Message);
        Assert.Equal(TaskStage.Discovery, task.State.Stage);
    }

    // The same argument as ABackwardTransitionWithABlankReasonIsRefusedForSayingNothing, pointed the
    // other way. It is the case the forward branch used to swallow: a blank reason trims to null, so
    // a branch testing only the trimmed value saw a caller who had passed nothing and moved the task
    // on. The caller did pass one, and a forward move takes none — so it is refused, and the two
    // tests differ only in direction.
    [Fact]
    public void AForwardTransitionWithABlankReasonIsRefusedRatherThanTrimmedAway()
    {
        var task = new TestTask();
        task.RecordResearchTopic();

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(
            new RequestStageTransitionCommand(
                task.OperatorId, null, task.NextCorrelation(), TaskStage.Research,
                Reason: "   ")));

        Assert.Equal(
            "Transition from 'Discovery' to 'Research' goes forward and does not take a reason. " +
            "Only a transition that goes back records why.",
            refusal.Message);
        Assert.Equal(TaskStage.Discovery, task.State.Stage);
    }

    [Fact]
    public void AForwardTransitionWithoutAReasonSucceedsAndRecordsNone()
    {
        var task = new TestTask();
        task.RecordResearchTopic();

        var outcome = task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), TaskStage.Research));

        var transitioned = (StageTransitioned)outcome.Events.Single(item => item.Data is StageTransitioned).Data;
        Assert.Equal(TaskStage.Research, task.State.Stage);
        Assert.Null(transitioned.Reason);
    }

    // Order of refusals. An edge that does not exist is refused for not existing, whichever
    // direction it would have pointed in; telling a caller to pass --reason for a move the graph
    // does not permit sends them to fix the wrong thing. Design to Discovery is the cheapest case
    // that is both illegal and backward.
    [Fact]
    public void AnIllegalTransitionThatIsAlsoBackwardIsRefusedAsIllegalNotAsReasonless()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Design);
        Assert.True(StageTransitionPolicy.IsBackward(TaskStage.Design, TaskStage.Discovery));

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(
            new RequestStageTransitionCommand(
                task.OperatorId, null, task.NextCorrelation(), TaskStage.Discovery)));

        Assert.Equal(
            "Transition from 'Design' to 'Discovery' is not legal. legal from 'Design': 'Research', 'Scope'.",
            refusal.Message);
        Assert.DoesNotContain("reason", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TaskStage.Design, task.State.Stage);
    }

    // The counterintuitive pair, and the one the ordinal rule is most likely to be misread on.
    // Repair is declared after Verification, so entering repair is forward — the loop working as
    // designed, which needs no sentence — and closing a repair cycle is the backward edge that does.
    [Fact]
    public void EnteringRepairGoesForwardAndTakesNoReason()
    {
        var task = AtRepairEntry();

        var refused = Assert.Throws<GovernanceException>(() => task.Apply(
            new RequestStageTransitionCommand(
                task.OperatorId, null, task.NextCorrelation(), TaskStage.Repair,
                Reason: "The verifier found three defects")));
        Assert.Equal(
            "Transition from 'Verification' to 'Repair' goes forward and does not take a reason. " +
            "Only a transition that goes back records why.",
            refused.Message);
        Assert.Equal(TaskStage.Verification, task.State.Stage);

        var outcome = task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), TaskStage.Repair));

        var transitioned = (StageTransitioned)outcome.Events.Single(item => item.Data is StageTransitioned).Data;
        Assert.Equal(TaskStage.Repair, task.State.Stage);
        Assert.Null(transitioned.Reason);
    }

    [Fact]
    public void LeavingRepairForVerificationGoesBackAndNeedsOne()
    {
        var task = AtRepairEntry();
        task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), TaskStage.Repair));
        Assert.True(StageTransitionPolicy.IsBackward(TaskStage.Repair, TaskStage.Verification));

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(
            new RequestStageTransitionCommand(
                task.OperatorId, null, task.NextCorrelation(), TaskStage.Verification)));
        Assert.Equal(
            "Transition from 'Repair' to 'Verification' goes back in the pipeline and needs a reason. " +
            "Pass --reason to record what was learned that sends the work back.",
            refusal.Message);
        Assert.Equal(TaskStage.Repair, task.State.Stage);

        var outcome = task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), TaskStage.Verification,
            Reason: "The three defects are fixed and the work wants reading again"));

        var transitioned = (StageTransitioned)outcome.Events.Single(item => item.Data is StageTransitioned).Data;
        Assert.Equal(TaskStage.Verification, task.State.Stage);
        Assert.Equal("The three defects are fixed and the work wants reading again", transitioned.Reason);
    }

    // The guard for the two-trailing-strings hazard. A waiver is not a reason: it excuses the arm,
    // it does not say what was learned. If the handler ever read one where it meant the other — or a
    // caller's positional string landed in the wrong slot — this move would be allowed through with
    // no reason at all, and the log would carry a waiver where the sentence should be.
    [Fact]
    public void AWaiverIsNotAReasonAndABackwardTransitionStillNeedsOne()
    {
        var task = ResearchTopicSettledAtDesign();

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(
            new RequestStageTransitionCommand(
                task.OperatorId, null, task.NextCorrelation(), TaskStage.Research,
                WithoutPrerequisitesReason: "Every claim this task opened is already settled")));

        Assert.Equal(
            "Transition from 'Design' to 'Research' goes back in the pipeline and needs a reason. " +
            "Pass --reason to record what was learned that sends the work back.",
            refusal.Message);
        Assert.Equal(TaskStage.Design, task.State.Stage);
    }

    // A real operator case: a move that goes back and whose target arm refuses. Both trailing
    // strings are set at once, and both are recorded — the waiver on its own event, the reason on
    // the transition — so a reader finds which arm was skipped and what sent the work back.
    [Fact]
    public void AWaivedBackwardTransitionRecordsBothItsReasonAndItsWaiver()
    {
        var task = ResearchTopicSettledAtDesign();

        // The arm does refuse this target, so the waiver is what carries the move rather than a
        // prerequisite that was satisfied anyway.
        var armRefusal = Assert.Throws<GovernanceException>(() => task.Apply(
            new RequestStageTransitionCommand(
                task.OperatorId, null, task.NextCorrelation(), TaskStage.Research,
                Reason: "The evidence settled the claim but raised a question the design cannot answer")));
        Assert.Equal("Research requires at least one open claim to investigate.", armRefusal.Message);

        var outcome = task.Apply(new RequestStageTransitionCommand(
            task.OperatorId, null, task.NextCorrelation(), TaskStage.Research,
            WithoutPrerequisitesReason: "The topic to research is the question, and it is not yet a claim",
            Reason: "The evidence settled the claim but raised a question the design cannot answer"));

        var waiverEvent = outcome.Events.Single(item => item.Data is StagePrerequisitesWaived);
        var transitionEvent = outcome.Events.Single(item => item.Data is StageTransitioned);
        var waived = (StagePrerequisitesWaived)waiverEvent.Data;
        var transitioned = (StageTransitioned)transitionEvent.Data;

        Assert.Equal(TaskStage.Research, task.State.Stage);
        Assert.Equal(TaskStage.Research, waived.TargetStage);
        Assert.Equal("The topic to research is the question, and it is not yet a claim", waived.Reason);
        Assert.Equal(
            "The evidence settled the claim but raised a question the design cannot answer",
            transitioned.Reason);
        // The two strings did not swap places on their way into the log.
        Assert.NotEqual(waived.Reason, transitioned.Reason);
        Assert.Equal(waiverEvent.EventId, transitionEvent.CausationId);
    }

    // A task at Verification with a verifier's findings on the record, which is what the Repair arm
    // asks for. Both Repair tests start here so neither restates the walk.
    private static TestTask AtRepairEntry()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Verification);
        task.RecordVerifierPass(task.StageWorkItem());
        return task;
    }

    // A task at Design whose one claim has been validated, so the Research arm behind it now
    // refuses: there is no open claim left to research. This is the cheapest backward edge whose
    // target arm genuinely fails, which is what the waiver tests need.
    private static TestTask ResearchTopicSettledAtDesign()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Design);
        var claimId = new ClaimId("C-topic");
        var evidenceId = new EvidenceId("E-topic");
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), evidenceId, "source-read",
            "src/AILedger.Core/Stages/Logic/StagePrerequisiteRules.cs:60",
            "The arms read state the kernel already holds", [claimId], []));
        task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), claimId, ClaimStatus.Validated, [evidenceId]));

        Assert.DoesNotContain(task.State.Claims.Values, claim => claim.Status == ClaimStatus.Open);
        Assert.Equal(TaskStage.Design, task.State.Stage);
        return task;
    }

    private sealed class NonValidatingReducer : ITaskReducer
    {
        public GovernedTaskState Apply(GovernedTaskState? state, LedgerEvent @event) =>
            state! with { Version = state.Version + 1 };
    }
}
