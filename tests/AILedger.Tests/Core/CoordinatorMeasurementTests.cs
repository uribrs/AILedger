using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// What the coordinating loop cost and how well it was run. These tests are about the honesty of the
// figures rather than about any gate: the projection refuses nothing, writes nothing, and scores
// nothing.
//
// Four properties are load-bearing and each has its own test below. No bracket is ever inferred for
// history written without one. No raw event count is reported as a measure. Every avoidability split
// shows the two sequences it was derived from. And an absent measurement is never a zero.
public sealed class CoordinatorMeasurementTests
{
    // R1 (causal-attribution-gap): metrics 10 to 12 need a causal link from a coordinator record to
    // a dispatch, a launch timeout, and a terminal failure reason. Only one of 401 historical
    // run.started events carries a causationId and neither of the other two fields is persisted at
    // all, so a precise count here would be an inference presented as a measurement — of a
    // coordinator's own mistakes, in a report the coordinator is scored by. D7 settles that those
    // inputs arrive going forward and that nothing is backfilled.
    [Fact]
    public void R1_MeasuresTenToTwelveAreNotMeasuredWithTheirReason()
    {
        var loop = new Loop();

        var report = loop.Build();

        var keys = report.NotMeasured.Select(entry => entry.Measure).ToArray();
        Assert.Contains("reworkCausedByCoordinatorDecisions", keys);
        Assert.Contains("failedDispatchesAttributableToCoordinator", keys);
        Assert.Contains("wastedDispatches", keys);
        // Each one says why, and the reason is a state-checkable condition rather than a judgement.
        Assert.All(report.NotMeasured, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Reason)));
        Assert.All(report.NotMeasured, entry => Assert.Contains("D7", entry.Reason, StringComparison.Ordinal));
    }

    // R2 (fabricated-historical-brackets): one task or actor lifetime presented as a session would
    // collapse several conversations into one bracket, and wall clock, idle time and child ownership
    // would all be wrong while looking precise. So a task with no session reports the two bracketed
    // measures as unbracketedHistory and returns no duration at all.
    [Fact]
    public void R2_HistoryWithNoSessionReportsUnbracketedHistoryAndNoDuration()
    {
        var loop = new Loop();
        loop.WithRun("R1", session: null, startMinutesIn: 0, endMinutesIn: 30);
        loop.WithRun("R2", session: null, startMinutesIn: 120, endMinutesIn: 150);

        var report = loop.Build();

        Assert.Empty(report.Sessions);
        Assert.True(report.HasUnbracketedCoordinatorActivity);
        var wallClock = Assert.Single(
            report.Degradations, entry => entry.Measure == "sessionWallClock");
        var idle = Assert.Single(
            report.Degradations, entry => entry.Measure == "timeWithNoAgentRunning");
        Assert.Contains("unbracketedHistory", wallClock.Reason, StringComparison.Ordinal);
        Assert.Contains("unbracketedHistory", idle.Reason, StringComparison.Ordinal);
        // Nothing inferred a span from the two-hour gap between the runs, which is exactly what
        // PALT3 refuses.
        Assert.DoesNotContain("120", wallClock.Reason, StringComparison.Ordinal);
    }

    // Measures 1 and 2. Time with no agent running is the single most important figure here: it was
    // 74% on 2026-09-09.
    [Fact]
    public void ASessionsWallClockAndIdleTimeAreMeasuredFromItsChildRuns()
    {
        var loop = new Loop();
        loop.WithSession("S1", startMinutesIn: 0, endMinutesIn: 240);
        loop.WithRun("R1", "S1", startMinutesIn: 30, endMinutesIn: 60);
        loop.WithRun("R2", "S1", startMinutesIn: 180, endMinutesIn: 210);

        var session = Assert.Single(loop.Build().Sessions);

        Assert.Equal(4.0, session.WallClockHours);
        // Four hours bracketed, one hour of it with an agent running.
        Assert.Equal(3.0, session.IdleHours);
        Assert.Equal(0.75, session.IdleShare);
        Assert.Equal(2, session.ChildRuns);
        Assert.Empty(session.Degradations);
    }

    // Two agents running at once is one busy span and not two, so idle time cannot be driven
    // negative by parallel dispatch — nor can a session look busier than its own clock allows.
    [Fact]
    public void OverlappingChildRunsCountAsOneBusySpan()
    {
        var loop = new Loop();
        loop.WithSession("S1", startMinutesIn: 0, endMinutesIn: 120);
        loop.WithRun("R1", "S1", startMinutesIn: 0, endMinutesIn: 60);
        loop.WithRun("R2", "S1", startMinutesIn: 30, endMinutesIn: 60);

        var session = Assert.Single(loop.Build().Sessions);

        Assert.Equal(2.0, session.WallClockHours);
        Assert.Equal(1.0, session.IdleHours);
    }

    // An open session has no end, so its duration is absent rather than measured against a clock
    // this projection deliberately does not take: a read that answered differently on every
    // invocation could not be compared with itself.
    [Fact]
    public void AnOpenSessionReportsNoDurationRatherThanMeasuringAgainstAClock()
    {
        var loop = new Loop();
        loop.WithSession("S1", startMinutesIn: 0, endMinutesIn: null);
        loop.WithRun("R1", "S1", startMinutesIn: 10, endMinutesIn: 40);

        var report = loop.Build();
        var session = Assert.Single(report.Sessions);

        Assert.Null(session.WallClockHours);
        Assert.Null(session.IdleHours);
        Assert.Null(session.IdleShare);
        Assert.Contains("sessionStillOpen", session.Degradations);
        Assert.Contains(report.Degradations, entry => entry.Measure == "sessionDurations");
    }

    // Measure 3, both halves: how long a finished run waited for its findings to be disposed, and
    // how long the loop waited before dispatching again. The distribution and not only the mean,
    // because the tail is where the stalls live.
    //
    // Driven through the command handler because the hand-built version of this test disposed its
    // finding with a validated resolution naming no evidence, which ClaimRules refuses. A test
    // whose input the kernel would never accept cannot fail for the reason it exists.
    [Fact]
    public void TheDelayFromARunFinishingToItsDispositionAndToTheNextDispatchIsDistributed()
    {
        var governed = new GovernedLoop();
        var worker = governed.WithWorker("bench-worker");
        governed.Dispatch("R1", worker, correlation: "R1");
        governed.AddClaim("C1", actor: worker, correlation: "R1");
        governed.AddEvidence("E1", supports: "C1", actor: worker, correlation: "R1");
        governed.CloseRun("R1");
        governed.ResolveClaim("C1", ClaimStatus.Validated, ["E1"]);
        governed.Dispatch("R2", worker);

        var delays = governed.Build().Delays;

        Assert.Equal(1, delays.ToFindingDisposed.Measured);
        Assert.Equal(
            governed.MinutesBetween(
                data => data is RunCompleted completed && completed.RunId.Value == "R1",
                data => data is ClaimResolved resolved && resolved.ClaimId.Value == "C1"),
            delays.ToFindingDisposed.MedianMinutes);
        Assert.Equal(1, delays.ToNextDispatch.Measured);
        Assert.Equal(
            governed.MinutesBetween(
                data => data is RunCompleted completed && completed.RunId.Value == "R1",
                data => data is RunStarted started && started.Run.Id.Value == "R2"),
            delays.ToNextDispatch.MedianMinutes);
        // Both gaps were real ones and not two readings of the same instant, which is what a
        // fixture whose clock did not advance would report.
        Assert.True(delays.ToFindingDisposed.MedianMinutes > 0);
        Assert.True(delays.ToNextDispatch.MedianMinutes > delays.ToFindingDisposed.MedianMinutes);
    }

    // A run whose findings nobody ever disposed is the case a mean silently drops, so it is counted
    // as an unmatched population beside the distribution rather than left out of both.
    [Fact]
    public void ARunWhoseFindingsWereNeverDisposedIsCountedAsUnmatchedRatherThanDropped()
    {
        var loop = new Loop();
        loop.WithRun("R1", session: null, startMinutesIn: 0, endMinutesIn: 60);
        loop.Append(new ClaimAdded(Claim("C1")), minutesIn: 30, actor: "worker", correlation: "R1");
        loop.Append(new RunCompleted(new RunId("R1"), AgentRunStatus.Completed, "s", At(60)), minutesIn: 60);

        var delays = loop.Build().Delays;

        Assert.Equal(0, delays.ToFindingDisposed.Measured);
        Assert.Equal(1, delays.ToFindingDisposed.Unmatched);
        Assert.Null(delays.ToFindingDisposed.MedianMinutes);
        Assert.Equal(1, delays.ToNextDispatch.Unmatched);
    }

    // D4: a raw event count is never a measure on its own, because a task can raise its event count
    // without delivering anything. So volume appears as a ratio against delivered work, the
    // denominator travels with it, and the numerator is nowhere in the output.
    //
    // Governed for the same reason as the delay test above — its finding used to be disposed by a
    // validated resolution naming no evidence — and asserted the way a reader of the output has to
    // assert it. The numerator is not in the report, so this test does not read it either: it
    // checks that one numerator is divided by two different denominators, which is the whole of
    // what the two ratios claim.
    [Fact]
    public void VolumeIsReportedOnlyAsARatioAgainstDeliveredWork()
    {
        var governed = new GovernedLoop();
        governed.AddClaim("C1");
        governed.AddEvidence("E1", supports: "C1");
        governed.ResolveClaim("C1", ClaimStatus.Validated, ["E1"]);
        governed.AddWork("W1");
        governed.CompleteWork("W1");
        governed.AddWork("W2");
        governed.CompleteWork("W2");

        var ratios = governed.Build().Ratios;

        Assert.Equal(2, ratios.EventsPerDeliveredWorkItem.Denominator);
        Assert.Equal(1, ratios.EventsPerFindingDisposed.Denominator);
        Assert.NotNull(ratios.EventsPerDeliveredWorkItem.Ratio);
        Assert.NotNull(ratios.EventsPerFindingDisposed.Ratio);
        // Two items delivered against one finding disposed, so the second ratio is twice the first
        // and neither states the count they came from.
        Assert.Equal(
            ratios.EventsPerFindingDisposed.Ratio!.Value,
            ratios.EventsPerDeliveredWorkItem.Ratio!.Value * 2,
            precision: 1);
    }

    // A ratio over an empty denominator is no number rather than a large one, and the absence says
    // which denominator was empty.
    [Fact]
    public void ARatioOverNothingDeliveredIsAnAbsenceAndNotALargeNumber()
    {
        var loop = new Loop();
        loop.Append(new ClaimAdded(Claim("C1")));

        var ratios = loop.Build().Ratios;

        Assert.Null(ratios.EventsPerDeliveredWorkItem.Ratio);
        Assert.Equal("noWorkItemDeliveredYet", ratios.EventsPerDeliveredWorkItem.Absence);
        Assert.Equal(0, ratios.EventsPerDeliveredWorkItem.Denominator);
    }

    // Measure 5. C2 is the mechanism: the log is append-only with monotonic versions, so for a record
    // at sequence N everything below N was available to it, and the split is that comparison rather
    // than an opinion.
    //
    // The verdict here is a reasonable revision, and it always will be for a claim refuted by a
    // record naming it: evidence cannot cite a claim that does not yet exist, so the refutation
    // stands above the claim by construction. That structural limit is reported as a degradation
    // rather than left for a reader to infer from a column of identical verdicts.
    [Fact]
    public void AClaimRefutedByEvidenceRecordedAfterItIsAReasonableRevision()
    {
        var loop = new Loop();
        loop.Append(new ClaimAdded(Claim("C1")));
        loop.Append(new EvidenceAdded(Evidence("E1", refutes: "C1")));
        loop.Append(new ClaimResolved(new ClaimId("C1"), ClaimStatus.Rejected, [new EvidenceId("E1")]));

        var report = loop.Build();

        var check = Assert.Single(report.ReversedClaims);
        Assert.Equal("C1", check.RecordId);
        Assert.Equal("reasonableRevision", check.Avoidability);
        Assert.Equal("E1", check.EvidenceId);
        Assert.True(check.EvidenceSequence > check.RecordSequence);
        var limit = Assert.Single(
            report.Degradations, entry => entry.Measure == "coordinatorClaimReversed");
        Assert.Contains("cannot cite a claim that does not yet exist", limit.Reason, StringComparison.Ordinal);
    }

    // A refinement sharpens a claim and its dependents stay valid; a correction contradicts it and
    // they do not. Counting a refinement as a reversal would report a coordinator error for the act
    // of improving a claim.
    [Fact]
    public void ARefinementIsNotCountedAsAReversal()
    {
        var loop = new Loop();
        loop.Append(new ClaimAdded(Claim("C1")));
        loop.Append(new EvidenceAdded(Evidence("E1", supports: "C2")));
        loop.Append(new ClaimResolved(
            new ClaimId("C1"), ClaimStatus.Superseded, [new EvidenceId("E1")],
            new ClaimId("C2"), SupersessionOutcome.Refinement));

        Assert.Empty(loop.Build().ReversedClaims);
    }

    // Measure 6, on the same terms and with the avoidable split genuinely reachable: the successor's
    // own supporting evidence is what the supersession rests on, and that evidence can — as here —
    // already have stood in the log when the decision it replaces was taken.
    //
    // Driven through the command handler and not assembled by hand, and that is the point of the
    // test rather than a matter of style. The version of this test that built its own
    // `DecisionProposed` payloads gave them decisions already marked accepted — a shape replay
    // refuses — so it could not distinguish a proposal from a supersession and passed while the
    // projection read the wrong event entirely. The events below are the ones the state machine
    // emits: the replacement is proposed, then resolved accepted, and that acceptance is what emits
    // `DecisionResolved(D1, Superseded)` (ZC1, ZE1).
    [Fact]
    public void ASupersededDecisionIsSplitTheSameWay()
    {
        var governed = new GovernedLoop();
        governed.AddClaim("C9");
        governed.AddEvidence("E1", supports: "C9");
        governed.Propose("D1");
        governed.Accept("D1");
        governed.Propose("D2", supersedes: "D1", dependsOn: "C9");
        governed.Accept("D2");

        var superseded = Assert.Single(governed.Build().SupersededDecisions);

        Assert.Equal("D1", superseded.RecordId);
        Assert.Equal("avoidableError", superseded.Avoidability);
        Assert.Equal("E1", superseded.EvidenceId);
        Assert.True(superseded.EvidenceSequence < superseded.RecordSequence);
    }

    // The case the projection got wrong, and it got it wrong in the direction that matters most: it
    // reported a decision as superseded that the state machine left current. A replacement is only a
    // request until it is accepted, and one resolved superseded is a proposal that was dropped —
    // `DecisionRules.ResolveDecision` marks the predecessor only inside the accepted branch, so no
    // event here takes D1 out of force and there is nothing to report about it.
    [Fact]
    public void AProposedReplacementThatWasNeverAcceptedDoesNotSupersedeItsPredecessor()
    {
        var governed = new GovernedLoop();
        governed.AddClaim("C9");
        governed.AddEvidence("E1", supports: "C9");
        governed.Propose("D1");
        governed.Accept("D1");
        governed.Propose("D2", supersedes: "D1", dependsOn: "C9");
        // The replacement is resolved superseded rather than accepted: it never took force, and D1
        // is still the decision this task is running on.
        governed.Supersede("D2");

        var report = governed.Build();

        Assert.Equal(DecisionStatus.Accepted, governed.State.Decisions[new DecisionId("D1")].Status);
        Assert.Equal(DecisionStatus.Superseded, governed.State.Decisions[new DecisionId("D2")].Status);
        // Neither decision is reported: D1 was never taken out of force, and D2 is a withdrawn
        // proposal rather than a decision that was replaced.
        Assert.Empty(report.SupersededDecisions);
    }

    // Measure 7, and the one place C2's comparison is exact rather than structural: a coordinator can
    // dispose a finding as validated while the record that refutes it already stands in the log. The
    // disposition follows the evidence here, which is the ordering measure 5 can never have.
    [Fact]
    public void AFindingDisposedWhileItsRefutationAlreadyStoodIsAvoidable()
    {
        var loop = new Loop();
        loop.Append(new ClaimAdded(Claim("C1")), actor: "worker");
        loop.Append(new EvidenceAdded(Evidence("E1", refutes: "C1")), actor: "worker");
        loop.Append(new EvidenceAdded(Evidence("E2", supports: "C1")), actor: "worker");
        loop.Append(new ClaimResolved(new ClaimId("C1"), ClaimStatus.Validated, [new EvidenceId("E2")]));
        loop.Append(new ClaimResolved(new ClaimId("C1"), ClaimStatus.Rejected, [new EvidenceId("E1")]));

        var report = loop.Build();

        var reopened = Assert.Single(report.ReopenedFindings);
        Assert.Equal("C1", reopened.RecordId);
        Assert.Equal("avoidableError", reopened.Avoidability);
        Assert.Equal("E1", reopened.EvidenceId);
        Assert.True(reopened.EvidenceSequence < reopened.RecordSequence);
        // The claim itself was raised by the worker, so it is not the coordinator's own reversed
        // claim — the two measures count different records and must not double-count one event.
        Assert.Empty(report.ReversedClaims);
    }

    // And the honest other half: a finding disposed before its refutation existed was reopened for a
    // reason that arrived later, which is a revision and not a mistake.
    [Fact]
    public void AFindingReopenedByEvidenceThatArrivedLaterIsARevision()
    {
        var loop = new Loop();
        loop.Append(new ClaimAdded(Claim("C1")), actor: "worker");
        loop.Append(new EvidenceAdded(Evidence("E2", supports: "C1")), actor: "worker");
        loop.Append(new ClaimResolved(new ClaimId("C1"), ClaimStatus.Validated, [new EvidenceId("E2")]));
        loop.Append(new EvidenceAdded(Evidence("E1", refutes: "C1")), actor: "worker");
        loop.Append(new ClaimResolved(new ClaimId("C1"), ClaimStatus.Rejected, [new EvidenceId("E1")]));

        var reopened = Assert.Single(loop.Build().ReopenedFindings);

        Assert.Equal("reasonableRevision", reopened.Avoidability);
        Assert.True(reopened.EvidenceSequence > reopened.RecordSequence);
    }

    // A reversal that names no evidence at all is unattributable rather than either split. Reporting
    // it as avoidable would be the report inventing a coordinator error out of a missing citation.
    //
    // This is the move that mattered most of the three. The hand-built version rejected the claim
    // with an empty evidence list, which ClaimRules refuses outright — so it demonstrated a branch
    // over a history that cannot exist, and a test like that cannot fail. The branch is genuinely
    // reachable, by the one terminal resolution the kernel accepts with no evidence: a supersession
    // whose replacement is not itself validated, which the kernel derives as a correction rather
    // than a refinement. The code was right and the input was not.
    [Fact]
    public void AReversalWithNoEvidenceIsUnattributableRatherThanAvoidable()
    {
        var governed = new GovernedLoop();
        governed.AddClaim("C1");
        governed.AddClaim("C2");
        governed.ResolveClaim("C1", ClaimStatus.Superseded, [], supersededBy: "C2");

        var check = Assert.Single(governed.Build().ReversedClaims);

        Assert.Equal("unattributable", check.Avoidability);
        Assert.Null(check.EvidenceId);
        Assert.Null(check.EvidenceSequence);
        Assert.Contains("names no ", check.Comparison, StringComparison.Ordinal);
    }

    // Measure 8, partially and deliberately. The pair is observable and the sequences are shown; the
    // avoidability dimension is named as missing rather than filled, because work.abandoned carries
    // a free-text reason and names no coordinator record.
    [Fact]
    public void AMisScopeIsReportedWithItsSequencesAndItsMissingDimension()
    {
        var loop = new Loop();
        loop.Append(new WorkItemAdded(WorkItem("W1", ["src/Core"])));
        loop.Append(new WorkItemAbandoned(new WorkItemId("W1"), "Superseded by a wider item"));
        loop.Append(new WorkItemAdded(WorkItem("W2", ["src/Core", "src/Cli"])));

        var misScope = Assert.Single(loop.Build().MisScopedWorkItems);

        Assert.Equal("W1", misScope.AbandonedWorkItem);
        Assert.Equal("W2", misScope.ReplacedBy);
        Assert.Equal(1, misScope.AreasHeld);
        Assert.Equal(2, misScope.AreasHeldByReplacement);
        Assert.True(misScope.AddedAtSequence < misScope.AbandonedAtSequence);
        Assert.Contains("coordinatorDecisionAttribution", misScope.MissingDimension, StringComparison.Ordinal);
    }

    // The wider successor is not decoration on the row, it is the evidence that the original scope
    // was too narrow. An item is released for many other reasons — the requirement went away, the
    // work was cancelled, a narrower split replaced it — and a release with nothing wider after it
    // was being reported as a mis-scope, which is the classification emitted while its own evidence
    // was absent.
    [Fact]
    public void AnAbandonmentWithNoWiderReplacementIsNotAMisScope()
    {
        var loop = new Loop();
        loop.Append(new WorkItemAdded(WorkItem("W1", ["src/Core"])));
        loop.Append(new WorkItemAbandoned(new WorkItemId("W1"), "The requirement was withdrawn"));
        // A later item holding the same one area is a re-scope and not a widening, and a narrower
        // one is the opposite of the mis-scope this measure names.
        loop.Append(new WorkItemAdded(WorkItem("W2", ["src/Core"])));
        loop.Append(new WorkItemAdded(WorkItem("W3", ["src/Cli"])));

        Assert.Empty(loop.Build().MisScopedWorkItems);
    }

    // The population is the coordinating loop's own rows on both halves of the pair: an item another
    // actor added is not the coordinator's mis-scope, and an item another actor added afterwards is
    // not the coordinator's replacement for one it released.
    [Fact]
    public void AWorkItemAnotherActorAddedIsNotTheCoordinatorsMisScope()
    {
        var byWorker = new Loop();
        byWorker.Append(new WorkItemAdded(WorkItem("W1", ["src/Core"])), actor: "worker");
        byWorker.Append(new WorkItemAbandoned(new WorkItemId("W1"), "Superseded by a wider item"));
        byWorker.Append(new WorkItemAdded(WorkItem("W2", ["src/Core", "src/Cli"])));

        var replacedByWorker = new Loop();
        replacedByWorker.Append(new WorkItemAdded(WorkItem("W1", ["src/Core"])));
        replacedByWorker.Append(new WorkItemAbandoned(new WorkItemId("W1"), "Superseded by a wider item"));
        replacedByWorker.Append(
            new WorkItemAdded(WorkItem("W2", ["src/Core", "src/Cli"])), actor: "worker");

        Assert.Empty(byWorker.Build().MisScopedWorkItems);
        Assert.Empty(replacedByWorker.Build().MisScopedWorkItems);
    }

    // Measure 3's dispatch half. The disposition search always required a coordinator and this one
    // accepted every run start, so an actor holding ManageRuns but coordinating nothing closed the
    // gap and then appeared in byActor as a coordinator of a loop it never ran. Both searches now
    // test membership in the same place.
    [Fact]
    public void ADispatchByANonCoordinatorDoesNotCloseTheGapOrEnterThePartition()
    {
        var loop = new Loop();
        loop.WithRun("R1", session: null, startMinutesIn: 0, endMinutesIn: 60);
        loop.Append(new RunCompleted(new RunId("R1"), AgentRunStatus.Completed, "s", At(60)), minutesIn: 60);
        // Thirty minutes after the run finished, dispatched by the worker: nearer in time, and not a
        // coordinator's dispatch, so it is not the gap this measure closes.
        loop.WithRun("R2", session: null, startMinutesIn: 90, endMinutesIn: null, dispatcher: "worker");
        loop.WithRun("R3", session: null, startMinutesIn: 120, endMinutesIn: null);

        var report = loop.Build();

        Assert.Equal(1, report.Delays.ToNextDispatch.Measured);
        // Sixty minutes to the operator's dispatch, not thirty to the worker's.
        Assert.Equal(60, report.Delays.ToNextDispatch.MedianMinutes);
        Assert.DoesNotContain("worker", report.ByActor.Select(actor => actor.Actor));
        Assert.DoesNotContain("worker", report.CoordinatorActors);
        var byOperator = Assert.Single(report.ByActor, actor => actor.Actor == "operator");
        Assert.Equal(1, byOperator.ToNextDispatch.Measured);
    }

    // Membership in the coordinating set is necessary and not sufficient, and this is the case that
    // shows why. A planning lead and an implementation lead are routinely dispatched agents here:
    // `provider launch --subject codex-plan` opens a run and every record that agent writes lands
    // under its own actor id. Holding the seat now says nothing about which of the two a given row
    // was; the row's own correlation does.
    //
    // Measured on this task's record before the repair: all 14 events its dispatched planning lead
    // wrote carried that agent's run id, and all 14 entered the ratios and measures 5 to 8. A
    // decision the agent proposed inside its run stood as a candidate coordinator decision.
    [Fact]
    public void RowsADispatchedLeadWroteInsideItsOwnRunAreNotTheCoordinatingLoops()
    {
        var inItsRun = new Loop();
        inItsRun.WithCoordinator("codex-plan");
        inItsRun.WithRun("RP1", session: null, startMinutesIn: 0, endMinutesIn: 60, actor: "codex-plan");
        Dispatched(inItsRun, correlation: "RP1");

        // The same three records, written by the same seat outside any run of its own. A lead that
        // genuinely coordinated is still counted, which is why the fix is the correlation and not a
        // shorter list of coordinating roles.
        var coordinating = new Loop();
        coordinating.WithCoordinator("codex-plan");
        Dispatched(coordinating, correlation: "coordinator-loop");

        var dispatchedReport = inItsRun.Build();
        var coordinatingReport = coordinating.Build();

        Assert.Empty(dispatchedReport.ReversedClaims);
        Assert.DoesNotContain("codex-plan", dispatchedReport.ByActor.Select(actor => actor.Actor));
        // Nothing it disposed inside its run is a disposition of the loop's, so the ratio has no
        // denominator rather than a flattering one.
        Assert.Equal("noFindingDisposedYet", dispatchedReport.Ratios.EventsPerFindingDisposed.Absence);
        // And the seat is still admitted: it is the rows that were excluded, not the actor.
        Assert.Contains("codex-plan", dispatchedReport.CoordinatorActors);

        var check = Assert.Single(coordinatingReport.ReversedClaims);
        Assert.Equal("codex-plan", check.Actor);
        Assert.Equal(1, coordinatingReport.Ratios.EventsPerFindingDisposed.Denominator);
    }

    // One claim raised, refuted and rejected by the lead, under whichever correlation the caller
    // gives. The worker's evidence stands between them so the rejection is a legal one.
    private static void Dispatched(Loop loop, string correlation)
    {
        loop.Append(new ClaimAdded(Claim("C1")), minutesIn: 10, actor: "codex-plan", correlation: correlation);
        loop.Append(new EvidenceAdded(Evidence("E1", refutes: "C1")), minutesIn: 20, actor: "worker");
        loop.Append(
            new ClaimResolved(new ClaimId("C1"), ClaimStatus.Rejected, [new EvidenceId("E1")]),
            minutesIn: 30, actor: "codex-plan", correlation: correlation);
    }

    // The hole the correlation test alone leaves open, and the shape that walks through it. A
    // dispatched agent is briefed to correlate every command on its run id, but a command issued
    // without that flag takes a fresh correlation per invocation — which is the ordinary shape of
    // an `artifact record`, so a dispatched planning lead's OrchestrationPlan carried none of the
    // two correlations the first repair excluded and was still counted as coordination. The rule
    // that closes it is where the row stands: between its author's run.started and run.completed.
    //
    // Measured across this repository: 548 such rows on 25 tasks.
    [Fact]
    public void RowsADispatchedLeadWroteUnderAFreshCorrelationAreNotTheCoordinatingLoops()
    {
        var inItsRun = new Loop();
        inItsRun.WithCoordinator("codex-plan");
        // Neither the run id nor the launch correlation appears on any row below, so the first
        // repair's test admits all three.
        inItsRun.WithRun(
            "RP1", session: null, startMinutesIn: 0, endMinutesIn: 60,
            actor: "codex-plan", correlation: "launch-RP1");
        inItsRun.Append(
            new ClaimAdded(Claim("C1")), minutesIn: 10, actor: "codex-plan", correlation: "cmd-1");
        inItsRun.Append(new EvidenceAdded(Evidence("E1", refutes: "C1")), minutesIn: 20, actor: "worker");
        inItsRun.Append(
            new ClaimResolved(new ClaimId("C1"), ClaimStatus.Rejected, [new EvidenceId("E1")]),
            minutesIn: 30, actor: "codex-plan", correlation: "cmd-2");
        inItsRun.Append(new RunCompleted(new RunId("RP1"), AgentRunStatus.Completed, "s", At(60)), minutesIn: 60);

        var report = inItsRun.Build();

        Assert.Empty(report.ReversedClaims);
        Assert.DoesNotContain("codex-plan", report.ByActor.Select(actor => actor.Actor));
        Assert.Equal("noFindingDisposedYet", report.Ratios.EventsPerFindingDisposed.Absence);
        // The seat is still admitted; it is the rows that were excluded, not the actor.
        Assert.Contains("codex-plan", report.CoordinatorActors);
    }

    // The second half of the same hole, on the other measure that read role membership alone. A
    // dispatched lead's rows fall outside every session — nothing brackets a run — so testing the
    // whole log for membership raised the unbracketed flag and put both bracketed measures into the
    // degradation list for a task whose coordinator was bracketed end to end. The degradation was
    // reported against work the coordinator never did.
    [Fact]
    public void ADispatchedLeadsRowsDoNotMakeABracketedCoordinatorUnbracketed()
    {
        var bracketed = new Loop();
        bracketed.WithSession("S1", startMinutesIn: 0, endMinutesIn: 60);
        bracketed.WithCoordinator("codex-plan");
        bracketed.WithRun(
            "RP1", session: null, startMinutesIn: 10, endMinutesIn: 40,
            actor: "codex-plan", correlation: "launch-RP1");
        bracketed.Append(
            new ClaimAdded(Claim("C1")), minutesIn: 20, actor: "codex-plan", correlation: "cmd-1");

        // The control: the same seat writing the same record outside any run of its own. That is
        // coordination nothing brackets, and the flag is supposed to rise for it.
        var unbracketed = new Loop();
        unbracketed.WithSession("S1", startMinutesIn: 0, endMinutesIn: 60);
        unbracketed.WithCoordinator("codex-plan");
        unbracketed.Append(
            new ClaimAdded(Claim("C1")), minutesIn: 20, actor: "codex-plan", correlation: "cmd-1");

        Assert.DoesNotContain(
            bracketed.Build().Degradations, entry => entry.Measure == "sessionWallClock");
        Assert.Contains(
            unbracketed.Build().Degradations, entry => entry.Measure == "sessionWallClock");
    }

    // The findings fallback matches a claim carrying the launch invocation's own correlation, which
    // is how a launched agent's commands are correlated when it does not use the run id. A
    // coordinator that passed an explicit --correlation and reused it across the launch and its own
    // claim adds would have those claims counted as the run's findings, and the disposition delay
    // measured from the wrong event. The author decides it: a coordinator's claim is not a
    // dispatched agent's finding.
    [Fact]
    public void ACoordinatorsOwnClaimOnTheLaunchCorrelationIsNotTheDispatchedRunsFinding()
    {
        var loop = new Loop();
        loop.WithRun("R1", session: null, startMinutesIn: 0, endMinutesIn: 60, correlation: "shared");
        // The coordinator's own claim, carrying the correlation it launched the run under.
        loop.Append(new ClaimAdded(Claim("C1")), minutesIn: 10, correlation: "shared");
        loop.Append(new EvidenceAdded(Evidence("E1", supports: "C1")), minutesIn: 20, actor: "worker");
        loop.Append(new RunCompleted(new RunId("R1"), AgentRunStatus.Completed, "s", At(60)), minutesIn: 60);
        loop.Append(
            new ClaimResolved(new ClaimId("C1"), ClaimStatus.Validated, [new EvidenceId("E1")]),
            minutesIn: 90);

        var delays = loop.Build().Delays;

        // The run delivered no finding of its own, so the gap after it is unmatched rather than
        // closed thirty minutes later by the coordinator disposing its own claim.
        Assert.Equal(0, delays.ToFindingDisposed.Measured);
        Assert.Equal(1, delays.ToFindingDisposed.Unmatched);
    }

    // Measure 9. The first occurrence of a key against an actor is instruction; every later
    // occurrence of the same key by the same actor is avoidable by construction, because the first
    // one taught it.
    //
    // And the population is the coordinating loop's own rows. The worker's refusal below is in the
    // journal and out of this measure: it is that worker's lesson, and counting it reported another
    // actor's behaviour as the coordinator's. This test asserted the defect before it asserted the
    // rule — it put the worker's row in Rows and in FirstTime while the fixture gave that actor the
    // worker role.
    [Fact]
    public void TheFirstRefusalOfAKeyIsInstructionAndEveryLaterOneIsARepeat()
    {
        var loop = new Loop();
        var journal = new RetrospectiveRefusalJournal(
        [
            new RetrospectiveRefusal(new ActorId("operator"), "work add", "service", "Scope is occupied"),
            new RetrospectiveRefusal(new ActorId("operator"), "work add", "service", "Scope is occupied"),
            new RetrospectiveRefusal(new ActorId("operator"), "work add", "service", "Scope is occupied"),
            // Same command, different rule: two lessons, so two keys and no repeat between them.
            new RetrospectiveRefusal(new ActorId("operator"), "work add", "service", "No brief on record"),
            // The worker's own refusal, which this measure must not count.
            new RetrospectiveRefusal(new ActorId("worker"), "work add", "service", "Scope is occupied"),
            new RetrospectiveRefusal(new ActorId("worker"), "work add", "service", "Scope is occupied")
        ], UnreadableRows: 0);

        var refusals = loop.Build(journal).Refusals;

        Assert.NotNull(refusals);
        // Four of the journal's six rows are the coordinator's, and the two the worker hit — one of
        // them a repeat of its own key — are absent from every figure here.
        Assert.Equal(4, refusals!.Rows);
        Assert.Equal(2, refusals.FirstTime);
        Assert.Equal(2, refusals.Repeat);
        var repeated = Assert.Single(refusals.RepeatedKeys);
        Assert.Equal("operator", repeated.Actor);
        Assert.Equal("Scope is occupied", repeated.Message);
        Assert.Equal(3, repeated.Occurrences);
        Assert.Equal(2, repeated.Repeats);
        Assert.DoesNotContain("worker", refusals.RepeatedKeys.Select(row => row.Actor));
        Assert.Contains("coordinating actors", refusals.Population, StringComparison.Ordinal);
    }

    // The same rule refusing the same actor about two different ids is one lesson, not two. Nearly
    // every refusal this kernel raises interpolates an identifier, so keying on the raw text made
    // each occurrence its own key: over this repository's 26 journals the raw key reported 125
    // repeats where the normalised key reports 219, and one task read as 33 rules learned once each
    // when it was 17 rules hit 17 more times after each had taught it.
    [Fact]
    public void TwoRefusalsOfOneRuleNamingDifferentIdsAreOneLessonAndOneRepeat()
    {
        var loop = new Loop();
        var journal = new RetrospectiveRefusalJournal(
        [
            new RetrospectiveRefusal(new ActorId("operator"), "claim resolve", "service", "Unknown claim 'C20'."),
            new RetrospectiveRefusal(new ActorId("operator"), "claim resolve", "service", "Unknown claim 'C21'."),
            // A second rule refusing the same command is still a second lesson: the message is in
            // the key for that reason and normalising it does not take it out.
            new RetrospectiveRefusal(
                new ActorId("operator"), "claim resolve", "service", "A validated claim requires evidence.")
        ], UnreadableRows: 0);

        var refusals = loop.Build(journal).Refusals;

        Assert.NotNull(refusals);
        Assert.Equal(3, refusals!.Rows);
        Assert.Equal(2, refusals.FirstTime);
        Assert.Equal(1, refusals.Repeat);
        var repeated = Assert.Single(refusals.RepeatedKeys);
        Assert.Equal(2, repeated.Occurrences);
        // Keyed on the rule and reported as the kernel said it: a reader is shown the first of the
        // two messages and not the placeholder they were grouped under.
        Assert.Equal("Unknown claim 'C20'.", repeated.Message);
    }

    // An unreadable row carries no actor, so it can be placed on neither side of the filter. It
    // stays reported as the caller found it, which is what makes Rows a floor rather than a total.
    [Fact]
    public void AnUnreadableRowIsStillReportedAfterTheCoordinatorFilter()
    {
        var loop = new Loop();
        var journal = new RetrospectiveRefusalJournal(
            [new RetrospectiveRefusal(new ActorId("worker"), "work add", "service", "Scope is occupied")],
            UnreadableRows: 3);

        var refusals = loop.Build(journal).Refusals;

        Assert.NotNull(refusals);
        Assert.Equal(0, refusals!.Rows);
        Assert.Equal(3, refusals.UnreadableRows);
        Assert.Equal(0, refusals.FirstTime);
    }

    // A task recorded before the refusal journal existed refused nothing that anybody wrote down,
    // which is not the same fact as refusing nothing.
    [Fact]
    public void AnAbsentRefusalJournalIsNullRatherThanZero()
    {
        Assert.Null(new Loop().Build().Refusals);
    }

    // D6, and the rule its caution survives as: where there is no usage record there is an absence,
    // never a zero and never an estimate.
    [Fact]
    public void TokenCostIsAnAbsenceWhenNoTranscriptWasSupplied()
    {
        var loop = new Loop();
        loop.WithSession("S1", startMinutesIn: 0, endMinutesIn: 60);

        var cost = loop.Build().TokenCost;

        Assert.NotNull(cost.Absence);
        Assert.Contains("noCoordinatorUsageSupplied", cost.Absence!, StringComparison.Ordinal);
        Assert.Null(cost.TokensInCacheRead);
        Assert.Null(cost.OutputTokens);
    }

    // The four buckets AgentRun already uses, so a coordinating session and a dispatched run compare
    // without conversion — and the source named on the measurement, because a number whose
    // provenance is unstated cannot be audited after the fact.
    [Fact]
    public void TokenCostIsReportedInTheFourAgentRunBucketsWithItsSourceNamed()
    {
        var loop = new Loop();
        loop.WithSession("S1", startMinutesIn: 0, endMinutesIn: 60, harnessSessionId: "harness-1");

        var cost = loop.Build(usage: new CoordinatorUsageRead(
            new CoordinatorSessionId("S1"),
            new CoordinatorUsageRecord(
                "/transcripts/harness-1.jsonl", "claude-opus-5", "harness-1",
                5_858, 2_851_285, 8_575_705, 1_076_728_319, 2_929, ControlStatement,
                UnreadableRows: 0),
            null)).TokenCost;

        Assert.Null(cost.Absence);
        Assert.Equal("/transcripts/harness-1.jsonl", cost.Source);
        Assert.Equal("claude-opus-5", cost.Model);
        Assert.Equal("S1", cost.Session);
        Assert.Equal(5_858, cost.TokensInUncached);
        Assert.Equal(2_851_285, cost.OutputTokens);
        Assert.Equal(8_575_705, cost.TokensInCacheWrite);
        Assert.Equal(1_076_728_319, cost.TokensInCacheRead);
        Assert.Equal(2_929, cost.Records);
        // C7: the caller's statement of what admitted the source travels with the figure, unchanged.
        // The projection ran no check on the file and states none of its own.
        Assert.Equal(ControlStatement, cost.Control);
    }

    // C7. An absence already names the control that refused it, so Control belongs to the charged
    // figure alone — a refusal carrying a second statement of provenance would say twice what it did
    // not do.
    [Fact]
    public void AnAbsenceCarriesNoStatementOfControl()
    {
        var loop = new Loop();
        loop.WithSession("S1", startMinutesIn: 0, endMinutesIn: 60, harnessSessionId: "harness-1");

        var cost = loop.Build(usage: new CoordinatorUsageRead(
            new CoordinatorSessionId("S1"),
            new CoordinatorUsageRecord(
                "/transcripts/harness-9.jsonl", "claude-opus-5", "harness-9", 1, 2, 3, 4, 5,
                ControlStatement, UnreadableRows: 0),
            null)).TokenCost;

        Assert.NotNull(cost.Absence);
        Assert.Null(cost.Control);
    }

    private const string ControlStatement =
        "harnessDirectoryProvenance: read from the harness's own transcript directory. Provenance of " +
        "location and not authenticity.";

    // R4 (transcript-identity-mismatch), the half only the ledger side can check: a transcript that
    // belongs to another conversation is refused rather than charged here. Its numbers would be
    // plausible and wrong, which is the worst shape a measurement can take.
    [Fact]
    public void ATranscriptFromAnotherSessionIsRefusedRatherThanCharged()
    {
        var loop = new Loop();
        loop.WithSession("S1", startMinutesIn: 0, endMinutesIn: 60, harnessSessionId: "harness-1");

        var cost = loop.Build(usage: new CoordinatorUsageRead(
            new CoordinatorSessionId("S1"),
            new CoordinatorUsageRecord(
                "/transcripts/harness-9.jsonl", "claude-opus-5", "harness-9", 1, 2, 3, 4, 5,
                ControlStatement, UnreadableRows: 0),
            null)).TokenCost;

        Assert.Contains("transcriptSessionMismatch", cost.Absence!, StringComparison.Ordinal);
        Assert.Null(cost.TokensInCacheRead);
    }

    [Fact]
    public void ASessionWithNoHarnessIdentityCannotHaveATranscriptChargedToIt()
    {
        var loop = new Loop();
        loop.WithSession("S1", startMinutesIn: 0, endMinutesIn: 60, harnessSessionId: null);

        var cost = loop.Build(usage: new CoordinatorUsageRead(
            new CoordinatorSessionId("S1"),
            new CoordinatorUsageRecord(
                "/transcripts/harness-1.jsonl", null, "harness-1", 1, 2, 3, 4, 5, ControlStatement,
                UnreadableRows: 0),
            null)).TokenCost;

        Assert.Contains("sessionRecordsNoHarnessIdentity", cost.Absence!, StringComparison.Ordinal);
    }

    // A total summed over a file with lines the reader could not parse is a floor, and the count is
    // what makes it readable as one — the standard CoordinatorRefusals already holds its own rows
    // to. It used to be dropped the moment a single row parsed, which is every live transcript: the
    // harness is still appending, so the last line is half written.
    [Fact]
    public void ATotalSummedOverAPartlyUnreadableTranscriptCarriesTheCountOfWhatWasSkipped()
    {
        var loop = new Loop();
        loop.WithSession("S1", startMinutesIn: 0, endMinutesIn: 60, harnessSessionId: "harness-1");

        var cost = loop.Build(usage: new CoordinatorUsageRead(
            new CoordinatorSessionId("S1"),
            new CoordinatorUsageRecord(
                "/transcripts/harness-1.jsonl", "claude-opus-5", "harness-1",
                5_858, 2_851_285, 8_575_705, 1_076_728_319, 2_929, ControlStatement,
                UnreadableRows: 3),
            null)).TokenCost;

        Assert.Null(cost.Absence);
        Assert.Equal(1_076_728_319, cost.TokensInCacheRead);
        Assert.Equal(3, cost.UnreadableRows);
    }

    // The absence on the branch a caller reaches by supplying a session and then neither a usage
    // record nor a reason for its absence. It used to say the harness left no usage record, which
    // is a fact about a read that did not happen — no reader in this repository produces this
    // triple, so nothing here ever looked at a harness.
    [Fact]
    public void AUsageReadCarryingNeitherARecordNorAReasonSaysSoRatherThanBlamingTheHarness()
    {
        var loop = new Loop();
        loop.WithSession("S1", startMinutesIn: 0, endMinutesIn: 60, harnessSessionId: "harness-1");

        var cost = loop.Build(usage: new CoordinatorUsageRead(
            new CoordinatorSessionId("S1"), null, null)).TokenCost;

        Assert.Contains("noUsageRecordFound", cost.Absence!, StringComparison.Ordinal);
        Assert.Contains("the caller supplied neither", cost.Absence!, StringComparison.Ordinal);
        Assert.DoesNotContain("the harness left no", cost.Absence!, StringComparison.Ordinal);
        Assert.Null(cost.TokensInCacheRead);
    }

    // An absence the reader itself reported travels through unchanged, so the reason a reader found
    // — a missing file, an unreadable one, an unsupported harness — is the reason the report states.
    [Fact]
    public void AnAbsenceTheReaderReportedIsCarriedThroughVerbatim()
    {
        var loop = new Loop();
        loop.WithSession("S1", startMinutesIn: 0, endMinutesIn: 60, harnessSessionId: "harness-1");

        var cost = loop.Build(usage: new CoordinatorUsageRead(
            new CoordinatorSessionId("S1"), null, "transcriptMissing: no file at '/nowhere.jsonl'."))
            .TokenCost;

        Assert.Equal("transcriptMissing: no file at '/nowhere.jsonl'.", cost.Absence);
    }

    // VC2, first half. Measure 3 as a task-wide figure alone cannot say whose loop stalled, and on a
    // task two coordinators worked that is the only question worth asking of it. So each measured
    // delay is attributed to the actor that closed the gap — the one that disposed the finding, or
    // the one that dispatched the next run — and the partition sits beside the aggregate rather than
    // replacing it.
    //
    // Driven through the command handler for the same reason as the three tests above it, and it
    // was the fourth of that set: the hand-built version disposed both findings with a validated
    // resolution naming no evidence, which `ClaimRules.ResolveClaim` refuses. Every event below is
    // one the kernel accepted, so the two dispositions carry the evidence a real one carries, and
    // the second gap is closed by a lead that genuinely held `ManageRuns` and `ResolveClaim`.
    //
    // Only an operator may dispatch for another subject, so the lead opens a run naming itself.
    // That is a legal shape and it is also the harder one for the projection: the lead is the
    // subject of R2 for the length of it, so its own rows inside that span are that run's work and
    // not the coordinating loop's — while the dispatch that opened the span, and the disposition
    // after it closed, are still the lead's coordination and are still measured here.
    [Fact]
    public void MeasureThreeIsPartitionedByTheActorThatClosedEachGap()
    {
        var governed = new GovernedLoop();
        var worker = governed.WithWorker("bench-worker");
        var lead = governed.WithCoordinator("planning-lead");

        // The operator dispatches the first run and disposes its finding, so the first gap is its own.
        governed.Dispatch("R1", worker, correlation: "R1");
        governed.AddClaim("C1", actor: worker, correlation: "R1");
        governed.AddEvidence("E1", supports: "C1", actor: worker, correlation: "R1");
        governed.CloseRun("R1");
        // One coordinator record between the run finishing and its finding being disposed, so the
        // two delays differ and the aggregate median is neither of them.
        governed.AddClaim("C3");
        governed.ResolveClaim("C1", ClaimStatus.Validated, ["E1"]);

        // The lead opens the next run, so the dispatch gap after R1 is the lead's.
        governed.Dispatch("R2", lead, correlation: "R2", actor: lead);
        governed.AddClaim("C2", actor: lead, correlation: "R2");
        governed.AddEvidence("E2", supports: "C2", actor: lead, correlation: "R2");
        governed.CloseRun("R2");
        governed.ResolveClaim("C2", ClaimStatus.Validated, ["E2"], actor: lead);

        var report = governed.Build();

        var operatorDisposal = governed.MinutesBetween(
            data => data is RunCompleted completed && completed.RunId.Value == "R1",
            data => data is ClaimResolved resolved && resolved.ClaimId.Value == "C1");
        var leadDisposal = governed.MinutesBetween(
            data => data is RunCompleted completed && completed.RunId.Value == "R2",
            data => data is ClaimResolved resolved && resolved.ClaimId.Value == "C2");
        var leadDispatch = governed.MinutesBetween(
            data => data is RunCompleted completed && completed.RunId.Value == "R1",
            data => data is RunStarted started && started.Run.Id.Value == "R2");

        // The task-wide figure holds both delays and the dispatch nobody made.
        Assert.Equal(2, report.Delays.ToFindingDisposed.Measured);
        Assert.Equal(
            (operatorDisposal + leadDisposal) / 2, report.Delays.ToFindingDisposed.MedianMinutes);
        Assert.Equal(1, report.Delays.ToNextDispatch.Measured);
        Assert.Equal(1, report.Delays.ToNextDispatch.Unmatched);

        var byOperator = Assert.Single(report.ByActor, actor => actor.Actor == "operator");
        var byLead = Assert.Single(report.ByActor, actor => actor.Actor == "planning-lead");
        // Each seat's own delay, and the aggregate above belongs to neither of them.
        Assert.Equal(1, byOperator.ToFindingDisposed.Measured);
        Assert.Equal(operatorDisposal, byOperator.ToFindingDisposed.MedianMinutes);
        Assert.Equal(1, byLead.ToFindingDisposed.Measured);
        Assert.Equal(leadDisposal, byLead.ToFindingDisposed.MedianMinutes);
        Assert.NotEqual(operatorDisposal, leadDisposal);
        Assert.NotEqual(operatorDisposal, report.Delays.ToFindingDisposed.MedianMinutes);
        Assert.NotEqual(leadDisposal, report.Delays.ToFindingDisposed.MedianMinutes);
        // The one dispatch that followed a finished run was the lead's, so the operator's dispatch
        // population is empty rather than zero-valued: no minimum, no median, no maximum.
        Assert.Equal(1, byLead.ToNextDispatch.Measured);
        Assert.Equal(leadDispatch, byLead.ToNextDispatch.MedianMinutes);
        Assert.Equal(0, byOperator.ToNextDispatch.Measured);
        Assert.Null(byOperator.ToNextDispatch.MedianMinutes);
        // The gap nobody ever closed is counted once, on the task-wide figure, because it belongs to
        // no actor. Charging it to one would name a coordinator for a run it never reached.
        Assert.Equal(0, byOperator.ToNextDispatch.Unmatched);
        Assert.Equal(0, byLead.ToNextDispatch.Unmatched);
    }

    // VC2, second half. An avoidability row without its author cannot be attributed once it is read
    // out of the list it came from, so the actor travels on the row — and the partition beside it
    // groups those rows by actor as record ids rather than as counts, because a bare count is what D4
    // refuses and an id is the handle back to the comparison a reader checks.
    [Fact]
    public void MeasureSevenCarriesItsActorAndIsPartitionedByIt()
    {
        var loop = new Loop();
        loop.WithCoordinator("planning-lead");
        // The operator disposes C1 as validated while the record refuting it already stands.
        loop.Append(new ClaimAdded(Claim("C1")), actor: "worker");
        loop.Append(new EvidenceAdded(Evidence("E1", refutes: "C1")), actor: "worker");
        loop.Append(new EvidenceAdded(Evidence("E2", supports: "C1")), actor: "worker");
        loop.Append(new ClaimResolved(new ClaimId("C1"), ClaimStatus.Validated, [new EvidenceId("E2")]));
        // The lead disposes C3 before anything refuted it, and the refutation arrives afterwards.
        loop.Append(new ClaimAdded(Claim("C3")), actor: "worker");
        loop.Append(new EvidenceAdded(Evidence("E4", supports: "C3")), actor: "worker");
        loop.Append(
            new ClaimResolved(new ClaimId("C3"), ClaimStatus.Validated, [new EvidenceId("E4")]),
            actor: "planning-lead");
        loop.Append(new EvidenceAdded(Evidence("E3", refutes: "C3")), actor: "worker");
        loop.Append(new ClaimResolved(new ClaimId("C1"), ClaimStatus.Rejected, [new EvidenceId("E1")]));
        loop.Append(
            new ClaimResolved(new ClaimId("C3"), ClaimStatus.Rejected, [new EvidenceId("E3")]),
            actor: "planning-lead");

        var report = loop.Build();

        // The task-wide list still holds both rows, each now naming its author.
        Assert.Equal(2, report.ReopenedFindings.Count);
        var avoidable = Assert.Single(report.ReopenedFindings, check => check.RecordId == "C1");
        var revision = Assert.Single(report.ReopenedFindings, check => check.RecordId == "C3");
        Assert.Equal("operator", avoidable.Actor);
        Assert.Equal("avoidableError", avoidable.Avoidability);
        Assert.Equal("planning-lead", revision.Actor);
        Assert.Equal("reasonableRevision", revision.Avoidability);

        var byOperator = Assert.Single(report.ByActor, actor => actor.Actor == "operator");
        var byLead = Assert.Single(report.ByActor, actor => actor.Actor == "planning-lead");
        // Record ids, so a reader follows each one back to the row where both sequences and the
        // evidence id stand and checks the comparison rather than trusting a tally.
        Assert.Equal(["C1"], byOperator.ReopenedFindings.AvoidableErrors);
        Assert.Empty(byOperator.ReopenedFindings.ReasonableRevisions);
        Assert.Equal(["C3"], byLead.ReopenedFindings.ReasonableRevisions);
        Assert.Empty(byLead.ReopenedFindings.AvoidableErrors);
        // Neither coordinator's own claim was reversed; the claims were the worker's, and the two
        // measures must not double-count one event.
        Assert.Empty(byOperator.ReversedClaims.AvoidableErrors);
        Assert.Empty(byLead.ReversedClaims.AvoidableErrors);
    }

    // A superseded decision is partitioned the same way, so measure 6 is not left as the one
    // avoidability measure a reader cannot attribute.
    [Fact]
    public void MeasureSixIsPartitionedByTheActorThatProposedTheDecision()
    {
        var governed = new GovernedLoop();
        var lead = governed.WithCoordinator("planning-lead");
        governed.AddClaim("C9");
        governed.AddEvidence("E1", supports: "C9");
        governed.Propose("D1", actor: lead);
        governed.Accept("D1", actor: lead);
        governed.Propose("D2", supersedes: "D1", dependsOn: "C9");
        governed.Accept("D2");

        var report = governed.Build();

        var superseded = Assert.Single(report.SupersededDecisions);
        Assert.Equal("planning-lead", superseded.Actor);
        var byLead = Assert.Single(report.ByActor, actor => actor.Actor == "planning-lead");
        Assert.Equal(["D1"], byLead.SupersededDecisions.AvoidableErrors);
    }

    // And the same measure with the seat that authored the decision outside the coordinating set. A
    // worker holding ProposeDecision is not a coordinator, so its superseded decision is not this
    // loop's record — the filter is one place and every measure passes through it.
    [Fact]
    public void ADecisionAnotherActorProposedIsNotTheCoordinatorsSupersededDecision()
    {
        var governed = new GovernedLoop();
        var worker = governed.WithWorker("bench-worker");
        governed.AddClaim("C9");
        governed.AddEvidence("E1", supports: "C9");
        governed.Propose("D1", actor: worker);
        governed.Accept("D1", actor: worker);
        governed.Propose("D2", supersedes: "D1", dependsOn: "C9", actor: worker);
        governed.Accept("D2", actor: worker);

        var report = governed.Build();

        Assert.Equal(DecisionStatus.Superseded, governed.State.Decisions[new DecisionId("D1")].Status);
        Assert.Empty(report.SupersededDecisions);
        Assert.DoesNotContain("bench-worker", report.CoordinatorActors);
    }

    private static readonly DateTimeOffset Start = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly TaskId Task = new("coordinator-loop");

    private static DateTimeOffset At(int minutesIn) => Start.AddMinutes(minutesIn);

    private static Claim Claim(string id) =>
        new(new ClaimId(id), $"Claim {id}", ClaimStatus.Open, [], null,
            new Provenance(new ActorId("operator"), Start, "claim.add"));

    private static Evidence Evidence(string id, string? supports = null, string? refutes = null) =>
        new(new EvidenceId(id), "source-read", $"a.cs:{id}", "summary",
            supports is null ? [] : [new ClaimId(supports)],
            refutes is null ? [] : [new ClaimId(refutes)],
            new Provenance(new ActorId("operator"), Start, "evidence.add"));

    // No hand-built Decision helper here on purpose. A decision's supersession is a pair of events
    // the state machine emits together under one rule, and a composed one can state a shape that
    // rule never produces — which is how this file's previous measure 6 tests came to assert a
    // history replay would refuse. Measure 6 is driven through GovernedLoop instead.
    private static WorkItem WorkItem(string id, IReadOnlyList<string> scope) =>
        new(new WorkItemId(id), $"Item {id}", new ActorId("operator"), WorkItemStatus.Active, [], scope);

    // The projection reads three collections off state — roles, runs and sessions — and everything
    // else out of the history it is handed. So this builds those three directly rather than driving
    // the command handler: what is under test is the arithmetic over a log, not the rules that let
    // the log be written.
    private sealed class Loop
    {
        private readonly List<LedgerEvent> _history = [];
        private readonly Dictionary<ActorId, RoleAssignment> _roles;
        private readonly Dictionary<CoordinatorSessionId, CoordinatorSession> _sessions = [];

        public Loop()
        {
            _roles = new Dictionary<ActorId, RoleAssignment>
            {
                [new ActorId("operator")] = new(
                    new ActorId("operator"), RoleKind.Operator, Enum.GetValues<Capability>(),
                    new Provenance(new ActorId("operator"), Start, "task.open")),
                [new ActorId("worker")] = new(
                    new ActorId("worker"), RoleKind.Worker,
                    [Capability.AddClaim, Capability.AddEvidence],
                    new Provenance(new ActorId("operator"), Start, "actor.assign-role"))
            };
        }

        public Dictionary<RunId, AgentRun> Runs { get; } = [];

        // A second coordinating seat, which is the case the partition exists for: two coordinators
        // work one task here and a task-wide figure cannot tell one's record from the other's.
        public void WithCoordinator(string actor, RoleKind role = RoleKind.PlanningLead) =>
            _roles[new ActorId(actor)] = new(
                new ActorId(actor), role, Enum.GetValues<Capability>(),
                new Provenance(new ActorId("operator"), Start, "actor.assign-role"));

        public void WithSession(
            string id,
            int startMinutesIn,
            int? endMinutesIn,
            string actor = "operator",
            string harness = "claude-code",
            string? harnessSessionId = "harness-1")
        {
            var session = new CoordinatorSession(
                new CoordinatorSessionId(id), new ActorId(actor), harness, harnessSessionId,
                At(startMinutesIn), endMinutesIn is null ? null : At(endMinutesIn.Value));
            _sessions[session.Id] = session;
            Append(new SessionStarted(session), startMinutesIn, actor);
        }

        // The run lands in state and its start lands in the log, which is where the launcher puts
        // them: the projection selects a session's children off state and measures the loop's delays
        // off the log. The correlation is the run id, which is what a launched agent carries on every
        // command it issues.
        public void WithRun(
            string id,
            string? session,
            int startMinutesIn,
            int? endMinutesIn,
            string actor = "worker",
            string dispatcher = "operator",
            // The launch invocation's own correlation, when a test needs it to differ from the run
            // id — which is the case the findings fallback exists for.
            string? correlation = null)
        {
            var run = new AgentRun(
                new RunId(id), new ActorId(actor), null, "claude", $"s-{id}",
                endMinutesIn is null ? AgentRunStatus.Active : AgentRunStatus.Completed,
                At(startMinutesIn), endMinutesIn is null ? null : At(endMinutesIn.Value),
                CoordinatorSessionId: session is null ? null : new CoordinatorSessionId(session));
            Runs[run.Id] = run;
            Append(new RunStarted(run), startMinutesIn, dispatcher, correlation ?? id);
        }

        public void Append(
            LedgerEventData data,
            int minutesIn = 0,
            string actor = "operator",
            string correlation = "coordinator-loop") =>
            _history.Add(new LedgerEvent(
                GovernedTaskState.CurrentSchemaVersion,
                new EventId($"{Task.Value}:{_history.Count + 1:D10}"),
                Task, new ActorId(actor), At(minutesIn), null, correlation, data));

        public CoordinatorLoopReport Build(
            RetrospectiveRefusalJournal? refusals = null,
            CoordinatorUsageRead? usage = null) =>
            CoordinatorMeasurement.Build(State(), _history, refusals, usage);

        private GovernedTaskState State() => new()
        {
            TaskId = Task,
            Title = "Coordinator loop",
            Goal = "Measure the loop as a loop",
            Roles = _roles,
            Runs = Runs,
            CoordinatorSessions = _sessions
        };
    }

    // The same projection over events the command handler produced rather than over events a test
    // composed. Measure 6 needs this and the others do not: a decision's supersession is a pair of
    // events the state machine emits together under one rule, and a hand-built log can state a shape
    // that rule never produces — which is how the previous test for this measure came to assert a
    // history replay would refuse, and passed while the projection read the wrong event (ZC1).
    //
    // Everything here goes through TestTask, so the roles, the capability checks and the decision
    // rules are the real ones, and the history is what the handler returned.
    private sealed class GovernedLoop
    {
        private readonly TestTask _task = new("coordinator-loop");

        public GovernedTaskState State => _task.State;

        // A second coordinating seat, which is the case the partition exists for. It holds the two
        // authorities a coordinator closes a gap with — disposing a finding and opening a run — on
        // top of the ones a decision needs, because measure 3's partition is about which of the two
        // seats closed each gap and a seat that cannot close one cannot be measured closing it.
        public ActorId WithCoordinator(string actor, RoleKind role = RoleKind.PlanningLead) =>
            Staff(actor, role,
                [Capability.AddClaim, Capability.AddEvidence, Capability.ProposeDecision,
                 Capability.ResolveDecision, Capability.ResolveClaim, Capability.ManageRuns]);

        // A seat outside the coordinating set that still holds the authority to author the record,
        // which is what makes it the case the filter has to catch.
        public ActorId WithWorker(string actor) => Staff(actor, RoleKind.Worker);

        public void AddClaim(string id, ActorId? actor = null, string? correlation = null) =>
            Apply(new AddClaimCommand(
                actor ?? _task.OperatorId, null, Correlation(correlation),
                new ClaimId(id), $"Claim {id}", null));

        public void AddEvidence(
            string id,
            string? supports = null,
            string? refutes = null,
            ActorId? actor = null,
            string? correlation = null) =>
            Apply(new AddEvidenceCommand(
                actor ?? _task.OperatorId, null, Correlation(correlation),
                new EvidenceId(id), "source-read", $"a.cs:{id}", "summary",
                supports is null ? [] : [new ClaimId(supports)],
                refutes is null ? [] : [new ClaimId(refutes)]));

        // The disposition half of measure 3, and the resolution measures 5 and 7 read. Every legal
        // shape goes through here, including the one this file used to compose by hand and the
        // kernel refuses: a validated or rejected resolution naming no evidence.
        public void ResolveClaim(
            string id,
            ClaimStatus status,
            IReadOnlyList<string> evidence,
            string? supersededBy = null,
            ActorId? actor = null) =>
            Apply(new ResolveClaimCommand(
                actor ?? _task.OperatorId, null, Correlation(null), new ClaimId(id), status,
                [.. evidence.Select(value => new EvidenceId(value))],
                supersededBy is null ? null : new ClaimId(supersededBy)));

        // An operator dispatching a run to another actor, which is the only way a role holding no
        // run authority is launched. The correlation is what a launched agent carries on every
        // command it issues, so a test that wants a finding to belong to a run passes the run id.
        //
        // `actor` is who issues the launch, and it is what measure 3's dispatch half is attributed
        // to. Only an operator may dispatch for a different subject
        // (`RunRules.EnsureDispatchIsPermitted`), so another coordinating seat opens a run by
        // naming itself as the subject.
        public void Dispatch(string runId, ActorId subject, string? correlation = null, ActorId? actor = null) =>
            Apply(new StartRunCommand(
                actor ?? _task.OperatorId, null, Correlation(correlation), new RunId(runId), null, "codex",
                null, null, null, null, subject));

        public void CloseRun(string runId) =>
            Apply(new CompleteRunCommand(
                _task.OperatorId, null, Correlation(null), new RunId(runId),
                AgentRunStatus.Completed, $"session-{runId}"));

        public void AddWork(string id) =>
            Apply(new AddWorkItemCommand(
                _task.OperatorId, null, Correlation(null), new WorkItemId(id), $"Item {id}", null, [], []));

        // Completed on the operator's recorded waiver rather than behind a staged worker and
        // verifier pass. Both are legal histories and this one is four events instead of thirty, so
        // the ratio under test is not buried in the machinery that produced its denominator.
        public void CompleteWork(string id) =>
            Apply(new CompleteWorkItemCommand(
                _task.OperatorId, null, Correlation(null), new WorkItemId(id),
                "Measuring the ratio, not the passes behind it"));

        // How far apart two events the handler produced stand on its clock. The delay measures are
        // asserted against this rather than against a hand-counted number of minutes: what is under
        // test is which two events the projection measured between, and a literal would pin the
        // fixture's command count instead.
        public double MinutesBetween(
            Func<LedgerEventData, bool> from,
            Func<LedgerEventData, bool> to) =>
            (_task.Events.First(@event => to(@event.Data)).RecordedAt -
             _task.Events.First(@event => from(@event.Data)).RecordedAt).TotalMinutes;

        // The handler's clock advances one minute per correlation it is asked for, so a command
        // carrying a caller's own correlation still has to take one — otherwise two commands share
        // a timestamp and every delay between them measures zero.
        private string Correlation(string? supplied)
        {
            var next = _task.NextCorrelation();
            return supplied ?? next;
        }

        public void Propose(
            string id,
            string? supersedes = null,
            string? dependsOn = null,
            ActorId? actor = null) =>
            Apply(new ProposeDecisionCommand(
                actor ?? _task.OperatorId, null, _task.NextCorrelation(),
                new DecisionId(id), $"Decision {id}", "Rationale",
                dependsOn is null ? [] : [new ClaimId(dependsOn)],
                supersedes is null ? null : new DecisionId(supersedes)));

        // Accepting a replacement is the command that supersedes its predecessor, and the only one.
        public void Accept(string id, ActorId? actor = null) => Resolve(id, DecisionStatus.Accepted, actor);

        // Resolving a proposal superseded drops the proposal and leaves any predecessor current.
        public void Supersede(string id, ActorId? actor = null) => Resolve(id, DecisionStatus.Superseded, actor);

        public CoordinatorLoopReport Build(
            RetrospectiveRefusalJournal? refusals = null,
            CoordinatorUsageRead? usage = null) =>
            CoordinatorMeasurement.Build(_task.State, _task.Events, refusals, usage);

        private void Resolve(string id, DecisionStatus status, ActorId? actor) =>
            Apply(new ResolveDecisionCommand(
                actor ?? _task.OperatorId, null, _task.NextCorrelation(), new DecisionId(id), status));

        private ActorId Staff(string actor, RoleKind role, IReadOnlyList<Capability>? capabilities = null)
        {
            var actorId = new ActorId(actor);
            Apply(new AssignRoleCommand(
                _task.OperatorId, null, _task.NextCorrelation(), actorId, role,
                capabilities ??
                [Capability.AddClaim, Capability.AddEvidence, Capability.ProposeDecision,
                 Capability.ResolveDecision]));
            return actorId;
        }

        private void Apply(LedgerCommand command) => _task.Apply(command);
    }
}
