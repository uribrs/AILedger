using AILedger.Core.Contracts;

namespace AILedger.Core.Application;

// What the coordinating loop cost and how well it was run — session, dispatched run, finding,
// disposition, next dispatch — measured as a loop rather than as a coordinator pretending to be
// another provider run (D1).
//
// Five things here are load-bearing and easy to mistake for shape.
//
// It is derived, not reported. Every measure comes from actorId, sequence and timestamp over events
// that already exist; the session bracket adds a boundary and a parent link and nothing else. A
// self-reported measure would depend on the coordinator being honest about itself, which is the
// thing under measurement (D2).
//
// It never infers a bracket. Measures 1 and 2 report `unbracketedHistory` for work done before a
// session existed, and no boundary is ever guessed from a time gap, a task lifetime or an actor
// lifetime. Multiple conversations would collapse into one bracket and every duration would be
// wrong while looking precise, which is PALT3 and attention item R2 — and in a report a task scores
// itself with, a fabricated number is worse than a stated gap because it reads as evidence (D7).
//
// It reports no raw event count. Volume appears only as a ratio against delivered work, and never
// as a bare numerator (D4). That is why CoordinatorRatio carries its denominator and its ratio and
// not the count they came from.
//
// Every avoidability verdict shows the comparison that produced it. The log is append-only with
// monotonic versions, so for a coordinator record at sequence N everything below N was available to
// it: a claim later refuted is a reasonable revision when the refuting evidence arrived after N, and
// an avoidable error when it already stood below N (C2). Emitting the label alone would make the
// verdict an opinion a reader has to trust, which is attention item R5 — so the record's sequence,
// the evidence's sequence and the evidence id travel with it.
//
// And every population it counts is the coordinating set's own. That is the fifth thing and it is
// the one a review found broken in four places at once: a refusal total, a dispatch delay, a
// mis-scope and a decision supersession all read plausible while describing rows the coordinator
// never authored. So membership is decided in exactly one place — CoordinatorSet — and no measure
// reaches a row except through it. A measure added later cannot forget the filter because there is
// no unfiltered list for it to reach.
public static class CoordinatorMeasurement
{
    // The three values an avoidability can take, named once so that the comparison that produces them
    // and the partition that groups by them cannot drift apart into two spellings of one fact.
    private const string AvoidableError = "avoidableError";
    private const string ReasonableRevision = "reasonableRevision";
    private const string Unattributable = "unattributable";

    // Which seats coordinate: the three that decompose work and dispatch it. A worker, a verifier, a
    // code reviewer and a researcher hold none of those authorities, so nothing they author is a
    // coordinator's record and nothing they author may appear in this report.
    private static readonly RoleKind[] CoordinatingRoles =
        [RoleKind.Operator, RoleKind.PlanningLead, RoleKind.ImplementationLead];

    public static CoordinatorLoopReport Build(
        GovernedTaskState state,
        IReadOnlyList<LedgerEvent> history,
        RetrospectiveRefusalJournal? refusals,
        CoordinatorUsageRead? usage)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(history);

        var coordinators = CoordinatorSet.Of(state, history);
        var sessions = BuildSessions(state);
        // The one filtered walk of the log. Every record-shaped measure below reads this and not the
        // history, so the coordinator filter is applied once rather than restated at each measure —
        // which is how four measures came to count other actors' rows while each looked correct on
        // its own. The bracket test reads it too, for the same reason and after a second repair.
        var authored = coordinators.Authored(history);
        var unbracketed = HasUnbracketedActivity(state, authored);
        // Collected once and used twice, as the task-wide figure and as the partition beneath it. A
        // partition recomputed from a second walk of the log could disagree with the aggregate it
        // sits under, and two numbers that should be the same and are not is the shape a reader
        // cannot resolve from the output alone.
        var delays = CollectDelays(history, coordinators);
        var index = new SequenceIndex(history);
        var reversedClaims = BuildReversedClaims(authored, index);
        var supersededDecisions = BuildSupersededDecisions(authored, index);
        var reopenedFindings = BuildReopenedFindings(authored, index);
        // Measures 10 to 12. Built after the three avoidability lists because measure 10 reads them:
        // a dispatch is rework when the record it names as its cause is one of those already found
        // reversed, so the verdict is carried across rather than decided a second time.
        var dispatches = CollectDispatches(state, authored, index);
        var dispatchMeasures = new CoordinatorDispatches(
            BuildRework(dispatches, index, reversedClaims, supersededDecisions, reopenedFindings),
            BuildFailedDispatches(dispatches, coordinators, refusals),
            BuildWastedDispatches(dispatches, coordinators, refusals));

        return new CoordinatorLoopReport(
            coordinators.Actors,
            sessions,
            unbracketed,
            BuildDegradations(state, sessions, unbracketed),
            BuildDelays(delays),
            BuildRatios(authored),
            reversedClaims,
            supersededDecisions,
            reopenedFindings,
            BuildMisScopes(authored),
            BuildRefusals(coordinators, refusals),
            BuildTokenCost(state, usage),
            dispatchMeasures,
            BuildForwardOnly(dispatchMeasures),
            BuildByActor(delays, reversedClaims, supersededDecisions, reopenedFindings));
    }

    // Who coordinated, and the only thing in this file that decides it. Every population the report
    // counts is obtained from here, so a measure cannot be written against an unfiltered list by
    // accident — there is none to reach.
    //
    // An actor enters the set two ways, and both are state and not inference:
    //
    //   - it holds one of the three coordinating roles now, read off the task's own role
    //     assignments rather than off a list of names this file would have to keep current;
    //   - or it opened a coordinator session on this task. That second door matters because a role
    //     assignment is current and a record is historical: an actor reassigned after coordinating
    //     still authored the records this report measures, and dropping it would silently shrink the
    //     population rather than say it had.
    //
    // Nothing else admits an actor. In particular holding ManageRuns does not: a launcher that
    // dispatches on a coordinator's behalf is not itself the coordinating seat, and reading a
    // dispatch as membership is how a delay came to be attributed to an actor that coordinated
    // nothing.
    //
    // Membership is necessary and not sufficient, and the second half is the one a review found
    // broken. A planning lead and an implementation lead are routinely *dispatched agents* here —
    // `provider launch --subject codex-plan` opens a run and every record that agent writes lands
    // under its own actor id — so a seat that coordinates on one task works inside a run on the
    // next. Holding a coordinating role now says nothing about which of the two a given row was.
    //
    // The rule that does tell them apart is one sentence: an event authored by an actor while that
    // actor was the subject of a run is that run's work and not coordination. It is applied two
    // ways, because a row can show its run either by where it stands or by what it names.
    //
    //   - By position. The run's own start and completion stand in this log, so every row an actor
    //     wrote between them was written inside that actor's run whatever correlation it carries.
    //     This is the test that closes the hole a correlation test cannot: an agent is briefed to
    //     correlate on its run id, but a command it issues without that flag takes a fresh
    //     correlation per invocation, which is the ordinary shape of an `artifact record`. On this
    //     corpus that is 548 rows across 25 tasks — a dispatched planning lead's whole OrchestrationPlan
    //     among them — every one of which a role test alone reads as coordination.
    //   - By correlation. A row naming the run id, or the launch invocation's own correlation, is
    //     that run's work even if it stands outside the pair — which a record filed on a run id
    //     after the launcher closed the run does.
    //
    // Neither subsumes the other, so a row is the run's if either says so.
    private sealed class CoordinatorSet
    {
        private readonly IReadOnlySet<ActorId> _actors;
        // The correlations that mark an event as written from inside a dispatched run, keyed by the
        // actor that run was dispatched to. Two forms, because a run is correlated two ways: the
        // launched agent is briefed to correlate on the run id, and a command issued under the
        // launch's own correlation belongs to the same run.
        private readonly IReadOnlySet<(ActorId Actor, string Correlation)> _dispatched;
        // The span of the log each actor spent as the subject of a run, as positions in this
        // history: from its run.started through its run.completed, and to the end of the log for a
        // run still active. Both ends are inside the span — the launcher's own start and completion
        // rows are that run's work too whenever the launcher and the subject are the same actor,
        // which is what an operator-held filing run is.
        private readonly IReadOnlyDictionary<ActorId, IReadOnlyList<(int From, int To)>> _insideRun;

        private CoordinatorSet(
            IReadOnlySet<ActorId> actors,
            IReadOnlySet<(ActorId Actor, string Correlation)> dispatched,
            IReadOnlyDictionary<ActorId, IReadOnlyList<(int From, int To)>> insideRun)
        {
            _actors = actors;
            _dispatched = dispatched;
            _insideRun = insideRun;
            Actors = [.. actors.Select(actor => actor.Value).OrderBy(value => value, StringComparer.Ordinal)];
        }

        public IReadOnlyList<string> Actors { get; }

        public static CoordinatorSet Of(GovernedTaskState state, IReadOnlyList<LedgerEvent> history)
        {
            var actors = state.Roles.Values
                .Where(assignment => CoordinatingRoles.Contains(assignment.Role))
                .Select(assignment => assignment.ActorId)
                .ToHashSet();
            foreach (var session in state.CoordinatorSessions.Values)
            {
                actors.Add(session.ActorId);
            }

            var dispatched = new HashSet<(ActorId, string)>();
            foreach (var run in state.Runs.Values)
            {
                dispatched.Add((run.ActorId, run.Id.Value));
            }

            var spans = new Dictionary<ActorId, List<(int From, int To)>>();
            var open = new Dictionary<RunId, (ActorId Actor, int From)>();
            for (var position = 0; position < history.Count; position++)
            {
                switch (history[position].Data)
                {
                    case RunStarted started:
                        dispatched.Add((started.Run.ActorId, history[position].CorrelationId));
                        open[started.Run.Id] = (started.Run.ActorId, position);
                        break;
                    case RunCompleted completed when open.Remove(completed.RunId, out var opened):
                        Span(spans, opened.Actor).Add((opened.From, position));
                        break;
                }
            }

            // A run the log never closes was still running when this log ended, so its subject was
            // inside it for the rest of the record. Closing the span at the last row rather than
            // dropping it keeps an active run's rows out of the coordinating population, which is
            // what the run the reader is reading this from is.
            foreach (var (actor, from) in open.Values)
            {
                Span(spans, actor).Add((from, history.Count - 1));
            }

            return new CoordinatorSet(
                actors,
                dispatched,
                spans.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<(int, int)>)entry.Value));
        }

        private static List<(int From, int To)> Span(
            Dictionary<ActorId, List<(int From, int To)>> spans,
            ActorId actor)
        {
            if (!spans.TryGetValue(actor, out var list))
            {
                list = [];
                spans[actor] = list;
            }

            return list;
        }

        public bool Holds(ActorId actor) => _actors.Contains(actor);

        // True when this row was written from inside a run its own author was dispatched to. Such a
        // row is the agent's, whatever seat that actor holds on the task now.
        public bool WroteInsideItsOwnRun(int position, LedgerEvent @event) =>
            _dispatched.Contains((@event.ActorId, @event.CorrelationId)) ||
            (_insideRun.TryGetValue(@event.ActorId, out var spans) &&
             spans.Any(span => position >= span.From && position <= span.To));

        // The coordinator-authored rows of the log, each with its position in the whole log — because
        // a sequence comparison is made against everything that stood below the record, coordinator
        // or not, and a filtered list renumbered from one would compare the wrong two numbers.
        //
        // Both tests, and the second is not a refinement of the first. Without it, all 14 events a
        // dispatched planning lead wrote on this very task entered the ratios and measures 5 to 8:
        // a decision that agent proposed inside its run was a candidate "coordinator decision
        // superseded", and the loop's event count read 173 where the loop authored 157.
        public IReadOnlyList<AuthoredEvent> Authored(IReadOnlyList<LedgerEvent> history)
        {
            var authored = new List<AuthoredEvent>();
            for (var position = 0; position < history.Count; position++)
            {
                if (Holds(history[position].ActorId) && !WroteInsideItsOwnRun(position, history[position]))
                {
                    authored.Add(new AuthoredEvent(position, history[position]));
                }
            }

            return authored;
        }

        // The refusal journal's coordinator rows. A refusal is telemetry beside the log and carries
        // the actor the kernel refused, so the same membership test applies to it — a worker's
        // refusals are that worker's lesson and not the coordinating loop's.
        public IReadOnlyList<RetrospectiveRefusal> Refusals(RetrospectiveRefusalJournal journal) =>
            [.. journal.Rows.Where(row => Holds(row.ActorId))];
    }

    // One coordinator-authored event and where it stands in the whole log.
    private sealed record AuthoredEvent(int Position, LedgerEvent Event);

    // Measures 1 and 2, per session. Wall clock is the bracket; idle time is the part of it during
    // which no child run was active, and it is the single most important figure here — it was 74% on
    // 2026-09-09.
    //
    // An open session has no end, so its duration is absent rather than measured against a clock
    // this projection deliberately does not take: a read that answered differently on every
    // invocation could not be compared with itself. The absence is named on the session rather than
    // left as a null the reader has to interpret.
    private static IReadOnlyList<CoordinatorSessionMeasure> BuildSessions(GovernedTaskState state) =>
        [.. state.CoordinatorSessions.Values
            .OrderBy(session => session.StartedAt)
            .ThenBy(session => session.Id.Value, StringComparer.Ordinal)
            .Select(session => MeasureSession(state, session))];

    private static CoordinatorSessionMeasure MeasureSession(GovernedTaskState state, CoordinatorSession session)
    {
        var children = state.Runs.Values
            .Where(run => run.CoordinatorSessionId == session.Id)
            .ToArray();
        var degradations = new List<string>();
        if (session.EndedAt is not { } endedAt)
        {
            degradations.Add("sessionStillOpen");
            return new CoordinatorSessionMeasure(
                session.Id.Value, session.ActorId.Value, session.Harness, session.HarnessSessionId,
                session.StartedAt, null, null, null, null, children.Length,
                children.Count(run => run.EndedAt is null), degradations);
        }

        // A child that outlived its session is clamped to the bracket rather than extending it, and
        // the clamp is stated: a run still active at the session's end contributed busy time up to
        // the boundary and nothing beyond it, which is the honest reading of an interval whose other
        // end is not yet known.
        if (children.Any(run => run.EndedAt is null || run.EndedAt > endedAt))
        {
            degradations.Add("childRunOutlivedSessionAndWasClampedToIt");
        }

        var intervals = children
            .Select(run => (
                Start: Later(run.StartedAt, session.StartedAt),
                End: Earlier(run.EndedAt ?? endedAt, endedAt)))
            .Where(interval => interval.End > interval.Start)
            .OrderBy(interval => interval.Start)
            .ToArray();

        var busy = TimeSpan.Zero;
        DateTimeOffset? mergedStart = null;
        var mergedEnd = session.StartedAt;
        foreach (var interval in intervals)
        {
            if (mergedStart is null)
            {
                mergedStart = interval.Start;
                mergedEnd = interval.End;
                continue;
            }

            if (interval.Start <= mergedEnd)
            {
                mergedEnd = Later(mergedEnd, interval.End);
                continue;
            }

            busy += mergedEnd - mergedStart.Value;
            mergedStart = interval.Start;
            mergedEnd = interval.End;
        }

        if (mergedStart is not null)
        {
            busy += mergedEnd - mergedStart.Value;
        }

        var duration = endedAt - session.StartedAt;
        var idle = duration - busy;
        if (idle < TimeSpan.Zero)
        {
            idle = TimeSpan.Zero;
        }

        return new CoordinatorSessionMeasure(
            session.Id.Value, session.ActorId.Value, session.Harness, session.HarnessSessionId,
            session.StartedAt, endedAt,
            Hours(duration), Hours(idle),
            duration <= TimeSpan.Zero ? null : Math.Round(idle / duration, 4),
            children.Length, children.Count(run => run.EndedAt is null), degradations);
    }

    // True when a coordinator authored anything this projection cannot place inside a bracket. It is
    // a boolean and not a count on purpose: a count of coordinator events outside a session would be
    // a raw event count reported as a measure, which D4 forbids, and the ratio in measure 4 is where
    // volume belongs.
    //
    // A task with no session at all is the ordinary case today — all 7,019 events in this repository
    // were written without one.
    //
    // It reads the coordinator-authored rows rather than the log, and that is the whole of what a
    // second repair changed here. Testing role membership alone let a dispatched lead's own rows
    // raise this flag, which put both bracketed measures into the degradation list for a task whose
    // coordinator was in fact bracketed end to end — a degradation reported against work the
    // coordinator never did.
    private static bool HasUnbracketedActivity(
        GovernedTaskState state,
        IReadOnlyList<AuthoredEvent> authored)
    {
        var brackets = state.CoordinatorSessions.Values
            .Select(session => (session.ActorId, session.StartedAt, session.EndedAt))
            .ToArray();
        return authored.Any(row =>
            !brackets.Any(bracket =>
                bracket.ActorId == row.Event.ActorId &&
                row.Event.RecordedAt >= bracket.StartedAt &&
                (bracket.EndedAt is null || row.Event.RecordedAt <= bracket.EndedAt)));
    }

    // Which measures answer a narrower question than the contract asked, and why. Each entry is a
    // state-checkable condition rather than a judgement, and each says what a reader may not
    // conclude from the figure beside it.
    private static IReadOnlyList<CoordinatorAbsence> BuildDegradations(
        GovernedTaskState state,
        IReadOnlyList<CoordinatorSessionMeasure> sessions,
        bool unbracketed)
    {
        var degraded = new List<CoordinatorAbsence>();
        if (state.CoordinatorSessions.Count == 0)
        {
            degraded.Add(new CoordinatorAbsence("sessionWallClock", UnbracketedHistory));
            degraded.Add(new CoordinatorAbsence("timeWithNoAgentRunning", UnbracketedHistory));
        }
        else if (unbracketed)
        {
            degraded.Add(new CoordinatorAbsence(
                "sessionWallClock",
                "Some coordinator activity on this task falls outside every recorded session, so the " +
                "brackets below describe part of the work and not all of it. " + UnbracketedHistory));
            degraded.Add(new CoordinatorAbsence(
                "timeWithNoAgentRunning",
                "Idle time is measured inside the recorded brackets only. Coordinator activity " +
                "outside them is unbracketed and contributes to neither the duration nor the idle span."));
        }

        if (sessions.Any(session => session.Degradations.Count > 0))
        {
            degraded.Add(new CoordinatorAbsence(
                "sessionDurations",
                "At least one session is still open or held a run that outlived it; the affected " +
                "sessions name the condition in their own degradations list."));
        }

        // Session attribution is what every remaining measure lacks, and it lacks it for all history:
        // no claim, decision, work item or refusal row carries a session id, and none may be given one
        // retroactively. So these compute at task and actor level, which is what the record supports.
        degraded.Add(new CoordinatorAbsence(
            "sessionAttribution",
            "Delays, ratios, avoidability and refusals are computed at task and actor level. Claims, " +
            "decisions, work items and refusal rows carry no session id, so none of them can be " +
            "partitioned between two sessions of the same actor without inventing the split."));
        degraded.Add(new CoordinatorAbsence(
            "eventsPerDeliveredWorkItem",
            "The ratio is an actor-and-task figure. Its numerator is deliberately not reported: a raw " +
            "event count is never a measure on its own (D4)."));
        // The structural limit on measure 5, stated rather than left for a reader to discover. An
        // evidence record cannot name a claim it refutes until that claim exists, so the record that
        // substantively refutes a coordinator's claim always stands above it and that half of the
        // split is reachable only through older evidence the reversal also cited. The measure that
        // does bite is the reopened finding below it: a coordinator can validate a claim while
        // refuting evidence already stands in the log, and that comparison is exact.
        degraded.Add(new CoordinatorAbsence(
            "coordinatorClaimReversed",
            "The refuting record is added after the claim it names, because evidence cannot cite a " +
            "claim that does not yet exist. So a reversal here reads as a reasonable revision unless " +
            "the reversal also cited older evidence, and the exact form of this comparison is " +
            "reopened findings, where the disposition follows the evidence rather than preceding it."));
        degraded.Add(new CoordinatorAbsence(
            "misScopedWorkItems",
            "An abandoned item and the wider item that replaced it are both in the record, but the " +
            "abandon reason is free text and names no coordinator record, so the mis-scope is reported " +
            "without an avoidability verdict. And only the pair is reported: an item released with no " +
            "wider item after it is not counted here at all, because the wider successor is the " +
            "evidence that the original scope was too narrow and a release has many other reasons."));
        return degraded;
    }

    private const string UnbracketedHistory =
        "unbracketedHistory: this work was recorded before a coordinator session existed, and no " +
        "bracket is ever inferred from a time gap, a task lifetime or an actor lifetime (D7, PALT3).";

    // Measure 3. Two delays per finished run: to the coordinator event that disposed one of that
    // run's findings, and to the next dispatch. The distribution and not only the mean, because the
    // tail is where the stalls live — and the unmatched population beside it, because a run whose
    // findings were never disposed is the case a mean silently drops.
    //
    // Each measured delay carries the actor that ended it, which is what makes the partition in
    // BuildByActor possible: the gap was closed by whoever disposed the finding or dispatched the
    // next run, and that is the coordinator the delay belongs to. An unmatched row has no such actor
    // — nobody ever closed it — so it is counted on the task-wide figure only.
    private static DelayRows CollectDelays(
        IReadOnlyList<LedgerEvent> history,
        CoordinatorSet coordinators)
    {
        var runCorrelations = new Dictionary<RunId, string>();
        foreach (var @event in history)
        {
            if (@event.Data is RunStarted started)
            {
                runCorrelations[started.Run.Id] = @event.CorrelationId;
            }
        }

        var toDisposition = new List<DelayRow>();
        var toDispatch = new List<DelayRow>();
        var dispositionUnmatched = 0;
        var dispatchUnmatched = 0;

        for (var index = 0; index < history.Count; index++)
        {
            if (history[index].Data is not RunCompleted completed)
            {
                continue;
            }

            var findings = FindingsOf(history, completed.RunId, runCorrelations, coordinators);
            var disposal = FirstDelay(history, index, coordinators, @event =>
                @event.Data is ClaimResolved resolved &&
                IsTerminal(resolved.Status) &&
                findings.Contains(resolved.ClaimId));
            if (disposal is { } disposalRow)
            {
                toDisposition.Add(disposalRow);
            }
            else
            {
                dispositionUnmatched++;
            }

            var dispatch = FirstDelay(history, index, coordinators, @event => @event.Data is RunStarted);
            if (dispatch is { } dispatchRow)
            {
                toDispatch.Add(dispatchRow);
            }
            else
            {
                dispatchUnmatched++;
            }
        }

        return new DelayRows(toDisposition, dispositionUnmatched, toDispatch, dispatchUnmatched);
    }

    private static CoordinatorDelays BuildDelays(DelayRows rows) =>
        new(Distribution([.. rows.ToDisposition.Select(row => row.Minutes)], rows.DispositionUnmatched),
            Distribution([.. rows.ToDispatch.Select(row => row.Minutes)], rows.DispatchUnmatched),
            "One row per run.completed in this task's log. A finding belongs to a run when the event " +
            "that added it carries the run's id or the run's own correlation id, which is the " +
            "convention the launcher briefs an agent with. The figures here are the whole task; the " +
            "partition in byActor holds the same rows keyed by the actor that closed each gap, and " +
            "an unmatched row appears here only, because a gap nobody closed has no actor to attribute.");

    // The claims a run authored, matched on correlation rather than on actor: an actor works many
    // runs, and the correlation is what a launched agent carries on every command it issues.
    //
    // This is the one population here that must NOT be coordinator-filtered, and it is deliberate:
    // these are the dispatched agent's findings, and the measure is how long the coordinator took to
    // dispose them. Filtering them to the coordinating set would leave nothing to dispose.
    //
    // The second branch is the inverse of that, and it is why the coordinating set is read here at
    // all. The launch invocation's own correlation belongs to the launching command, so a
    // coordinator that passed an explicit `--correlation` and reused it across the launch and its
    // own `claim add` commands would see its own claims counted as that run's findings, and the
    // disposition delay measured from the wrong event. Requiring a non-coordinator author closes
    // that without touching the run-id branch, which no coordinator command carries.
    private static IReadOnlySet<ClaimId> FindingsOf(
        IReadOnlyList<LedgerEvent> history,
        RunId runId,
        IReadOnlyDictionary<RunId, string> runCorrelations,
        CoordinatorSet coordinators)
    {
        runCorrelations.TryGetValue(runId, out var startCorrelation);
        return history
            .Where(@event =>
                string.Equals(@event.CorrelationId, runId.Value, StringComparison.Ordinal) ||
                (startCorrelation is not null &&
                 string.Equals(@event.CorrelationId, startCorrelation, StringComparison.Ordinal) &&
                 !coordinators.Holds(@event.ActorId)))
            .Select(@event => @event.Data)
            .OfType<ClaimAdded>()
            .Select(added => added.Claim.Id)
            .ToHashSet();
    }

    // Both halves of measure 3 close their gap here, and both require the closing event to be a
    // coordinator's. The membership test is in this loop rather than in either caller's predicate on
    // purpose: it was in one predicate and not the other, so a run started by any actor holding
    // ManageRuns closed a dispatch gap and then appeared in byActor as a coordinator of a loop it
    // never coordinated.
    private static DelayRow? FirstDelay(
        IReadOnlyList<LedgerEvent> history,
        int fromIndex,
        CoordinatorSet coordinators,
        Func<LedgerEvent, bool> matches)
    {
        for (var index = fromIndex + 1; index < history.Count; index++)
        {
            if (coordinators.Holds(history[index].ActorId) && matches(history[index]))
            {
                return new DelayRow(
                    history[index].ActorId,
                    Math.Max(0, (history[index].RecordedAt - history[fromIndex].RecordedAt).TotalMinutes));
            }
        }

        return null;
    }

    // Nearest-rank percentiles over the measured values, and null everywhere when nothing was
    // measured. A zero here would say every dispatch followed instantly; the count beside it is what
    // keeps that readable.
    private static CoordinatorDelayDistribution Distribution(List<double> values, int unmatched)
    {
        if (values.Count == 0)
        {
            return new CoordinatorDelayDistribution(0, unmatched, null, null, null, null);
        }

        values.Sort();
        var median = values.Count % 2 == 1
            ? values[values.Count / 2]
            : (values[(values.Count / 2) - 1] + values[values.Count / 2]) / 2;
        var rank = (int)Math.Ceiling(0.95 * values.Count) - 1;
        return new CoordinatorDelayDistribution(
            values.Count,
            unmatched,
            Math.Round(values[0], 2),
            Math.Round(median, 2),
            Math.Round(values[Math.Clamp(rank, 0, values.Count - 1)], 2),
            Math.Round(values[^1], 2));
    }

    // Measure 4, as ratios only. The numerator — coordinator-authored events — never appears on its
    // own, because a task can raise its event count without delivering anything and D4 refuses to
    // give that a number. A ratio over an empty denominator is no number rather than a large one.
    //
    // Numerator and both denominators come from the same filtered walk, so this measure has no
    // population of its own to get wrong: it is the coordinating loop's own rows over the
    // deliveries and dispositions that loop recorded. Reading the denominators from the whole log
    // instead would count another actor's delivery against a coordinator's event count, and it
    // would do it in the direction that flatters — a larger denominator is a smaller ratio.
    private static CoordinatorRatios BuildRatios(IReadOnlyList<AuthoredEvent> authored)
    {
        var delivered = authored.Count(row => row.Event.Data is WorkItemCompleted);
        var disposed = authored.Count(row =>
            row.Event.Data is ClaimResolved resolved && IsTerminal(resolved.Status));
        return new CoordinatorRatios(
            Ratio(authored.Count, delivered, "noWorkItemDeliveredYet"),
            Ratio(authored.Count, disposed, "noFindingDisposedYet"));
    }

    private static CoordinatorRatio Ratio(int numerator, int denominator, string absence) =>
        denominator == 0
            ? new CoordinatorRatio(null, 0, absence)
            : new CoordinatorRatio(Math.Round((double)numerator / denominator, 2), denominator, null);

    // Measure 5. A claim the coordinator raised that was later rejected, or corrected by a
    // successor. The split is a sequence comparison and never a judgement: the refuting or successor
    // evidence either already stood below the claim's own sequence or it did not (C2).
    private static IReadOnlyList<AvoidabilityCheck> BuildReversedClaims(
        IReadOnlyList<AuthoredEvent> authored,
        SequenceIndex index)
    {
        var checks = new List<AvoidabilityCheck>();
        foreach (var row in authored)
        {
            if (row.Event.Data is not ClaimAdded added)
            {
                continue;
            }

            var reversal = index.FirstReversalOf(added.Claim.Id, row.Position);
            if (reversal is not { } resolved)
            {
                continue;
            }

            checks.Add(index.Compare(
                "coordinatorClaimReversed",
                added.Claim.Id.Value,
                row.Position,
                index.CounterEvidenceFor(added.Claim.Id, resolved.Resolved, resolved.Position)));
        }

        return checks;
    }

    // Measure 6. A decision the coordinator proposed and that was later superseded, invalidated or
    // overturned, compared against the earliest evidence the successor rests on.
    private static IReadOnlyList<AvoidabilityCheck> BuildSupersededDecisions(
        IReadOnlyList<AuthoredEvent> authored,
        SequenceIndex index)
    {
        var checks = new List<AvoidabilityCheck>();
        foreach (var row in authored)
        {
            if (row.Event.Data is not DecisionProposed proposed)
            {
                continue;
            }

            if (index.FirstOverturnOf(proposed.Decision.Id, row.Position) is not { } overturn)
            {
                continue;
            }

            checks.Add(index.Compare(
                "coordinatorDecisionSuperseded",
                proposed.Decision.Id.Value,
                row.Position,
                index.SuccessorEvidenceFor(proposed.Decision.Id, overturn)));
        }

        return checks;
    }

    // Measure 7. A finding the coordinator disposed as validated and that was later reopened —
    // rejected, or superseded by a correction. The same comparison, against the evidence the reversal
    // rests on.
    private static IReadOnlyList<AvoidabilityCheck> BuildReopenedFindings(
        IReadOnlyList<AuthoredEvent> authored,
        SequenceIndex index)
    {
        var checks = new List<AvoidabilityCheck>();
        foreach (var row in authored)
        {
            if (row.Event.Data is not ClaimResolved { Status: ClaimStatus.Validated } validated)
            {
                continue;
            }

            if (index.FirstReversalOf(validated.ClaimId, row.Position) is not { } reversal)
            {
                continue;
            }

            checks.Add(index.Compare(
                "coordinatorDispositionReopened",
                validated.ClaimId.Value,
                row.Position,
                index.CounterEvidenceFor(validated.ClaimId, reversal.Resolved, reversal.Position)));
        }

        return checks;
    }

    // Measure 8, partially. The evidence of a mis-scope is a **pair**: an item the coordinator added,
    // later released, and a later item of its own holding more areas that cover the released ones.
    // The sequences are shown, so a reader can check the order. The avoidability verdict is not
    // emitted: the abandon reason is free text and names no coordinator record, so an attribution
    // here would be a guess wearing a verdict's clothes. That missing dimension is named rather than
    // filled.
    //
    // No successor, no row. An item is released for many reasons — the requirement went away, the
    // work was cancelled, a narrower split replaced it — and a release with nothing wider after it is
    // simply a release. Emitting one as a mis-scope was reporting the negative classification while
    // the structural evidence the classification rests on was absent, which is the same defect as a
    // measurement without a population.
    private static IReadOnlyList<ScopeReplacement> BuildMisScopes(IReadOnlyList<AuthoredEvent> authored)
    {
        var added = new Dictionary<WorkItemId, (int Position, WorkItem Item)>();
        var replacements = new List<ScopeReplacement>();
        for (var row = 0; row < authored.Count; row++)
        {
            switch (authored[row].Event.Data)
            {
                case WorkItemAdded item:
                    added[item.WorkItem.Id] = (authored[row].Position, item.WorkItem);
                    break;
                case WorkItemAbandoned abandoned when added.TryGetValue(abandoned.WorkItemId, out var origin):
                    if (FirstWiderItem(authored, row, origin.Item) is not { } successor)
                    {
                        break;
                    }

                    replacements.Add(new ScopeReplacement(
                        origin.Item.Id.Value,
                        origin.Position + 1,
                        authored[row].Position + 1,
                        successor.Id.Value,
                        origin.Item.ResourceScope.Count,
                        successor.ResourceScope.Count,
                        abandoned.Reason,
                        "coordinatorDecisionAttribution: work.abandoned carries a free-text reason and " +
                        "names no coordinator record, so this pair is reported without an avoidability verdict."));
                    break;
            }
        }

        return replacements;
    }

    // The first later item that covers every area the released one held and holds more of them. More
    // areas alone is not a replacement, and the same areas re-declared is a re-scope rather than a
    // widening — the containment is what makes the second item the successor of the first.
    //
    // Searched within the coordinator's own rows, like everything else here: an item added by another
    // actor is not the coordinator's replacement for the one it released.
    private static WorkItem? FirstWiderItem(
        IReadOnlyList<AuthoredEvent> authored,
        int fromRow,
        WorkItem released)
    {
        for (var row = fromRow + 1; row < authored.Count; row++)
        {
            if (authored[row].Event.Data is WorkItemAdded added &&
                added.WorkItem.ResourceScope.Count > released.ResourceScope.Count &&
                released.ResourceScope.All(area => added.WorkItem.ResourceScope.Contains(area)))
            {
                return added.WorkItem;
            }
        }

        return null;
    }

    // Measure 9. The first occurrence of a refusal key against an actor is instruction; every later
    // occurrence of the same key by the same actor is avoidable by construction, because the first
    // one taught it. The key is the actor, the command, the site and the kernel's own message — the
    // message included, because two refusals of one command by different rules are two lessons.
    //
    // The message is normalised before it is keyed on, and that is the difference between a measure
    // and a flattering one. Nearly every refusal this kernel raises interpolates an identifier —
    // "Unknown claim 'C20'.", "Actor 'claude-impl' lacks capability 'RecordAlternative'." — so the
    // raw text made one rule refusing one actor about two ids into two keys, two first-time rows and
    // no repeat at all. Measured over the 318 rows in this repository's 26 refusal journals: the raw
    // key reports 193 distinct keys and 125 repeats, and the same key with quoted literals
    // normalised reports 99 and 219. One task read as 33 lessons learned once each; it was 17 rules
    // hit 17 more times after each had already taught it. Repeat is the one figure the goal calls
    // avoidable by construction, so it is the one that must not be wrong in the flattering
    // direction. RefusalRecord carries no rule id, so the message is the only handle there is.
    //
    // Null when the caller found no journal, and a zero total when it found an empty one. A journal
    // whose rows did not all parse makes every count below a floor, which UnreadableRows states.
    //
    // The journal holds every actor's refusals and this measure is about one loop's, so the rows are
    // filtered to the coordinating set before anything is counted or grouped. Unfiltered, a
    // worker-heavy task reported the workers' lessons as the coordinator's — and on a task where a
    // dispatched agent hits a refusal the coordinator never could, the report was dominated by
    // behaviour the coordinator did not author.
    private static CoordinatorRefusals? BuildRefusals(
        CoordinatorSet coordinators,
        RetrospectiveRefusalJournal? refusals)
    {
        if (refusals is null)
        {
            return null;
        }

        var rows = coordinators.Refusals(refusals);
        var groups = rows
            .GroupBy(row => (row.ActorId.Value, row.Command, row.Site, Rule: RefusalRule(row.Message)))
            // The raw message of the first row in the group, so a reader still sees what the kernel
            // said rather than the placeholder it was keyed on. The first is the one that taught the
            // actor, and every later row in the group is the same rule with other identifiers in it.
            .Select(group => new CoordinatorRepeatedRefusal(
                group.Key.Value, group.Key.Command, group.Key.Site, group.First().Message,
                group.Count(), group.Count() - 1))
            .OrderByDescending(row => row.Repeats)
            .ThenBy(row => row.Actor, StringComparer.Ordinal)
            .ThenBy(row => row.Command, StringComparer.Ordinal)
            .ToArray();

        return new CoordinatorRefusals(
            rows.Count,
            // Reported as the caller found it, and deliberately not filtered: a row that did not
            // parse carries no actor to test, so it can be neither admitted nor excluded. It makes
            // every count above a floor, which is the honest reading of an unreadable row and the
            // reason it is not silently dropped.
            refusals.UnreadableRows,
            groups.Length,
            groups.Sum(row => row.Repeats),
            [.. groups.Where(row => row.Repeats > 0)],
            "The coordinating actors' rows only; a worker's or a verifier's refusals are that " +
            "actor's lesson and not this loop's. Refusal rows carry no session id, so this is an " +
            "actor-level population for the whole task and cannot be partitioned between two " +
            "sessions of one actor.");
    }

    // Which rule refused, as far as the message can say. Every quoted literal becomes one
    // placeholder, so two refusals differing only in the id they name key as the one lesson they
    // are. Nothing narrower is available: RefusalRecord carries the kernel's text and no rule id,
    // and giving it one would change a record written on a failure path by every command in the
    // kernel. The raw message is kept on the row this key produces.
    private static readonly System.Text.RegularExpressions.Regex QuotedLiteral =
        new("'[^']*'", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static string RefusalRule(string message) => QuotedLiteral.Replace(message, "'X'");

    // Measures 10, 11 and 12. Three questions about one population — the dispatches this coordinator
    // made — grouped here because they are the three the record could not answer at all until it
    // carried three inputs, and because each has to be able to say "none" and "I could not tell" as
    // two different answers.
    //
    // The inputs are D7's and they arrive going forward only: the causationId on run.started naming
    // the coordinator record that prompted the dispatch, the limit the launch was given, and the
    // provider's own reason for a run that ended other than by completing. Nothing is backfilled and
    // nothing is inferred. A dispatch made before those inputs existed leaves its measure at the
    // named absence below, because an invented count of a coordinator's own mistakes is the one
    // number this report must never produce.
    //
    // None of the three reads run.status, and C8 is why. Run LR4 on
    // 2026-09-10_0931-insightvm-throttle-404 is recorded in the ledger as failed while its own
    // sidecar reports completed, exit code 0 and no failure at all: the agent's verdict was FAIL, and
    // a verdict is a finding rather than a failure of the run. Separately, AgentAdapterBase marks a
    // run failed when any event carries isError and at least one provider types an informational
    // notice that way. So the status is two things at once, and what these measures read instead is
    // AgentRun.TerminalFailureReason — the provider's own Failure string, relayed onto the run by the
    // launcher from the same result object the sidecar is written from, so the run record and its
    // sidecar make one statement rather than two that can disagree.
    //
    // A dispatch that carries neither new field predates them and is not judged. That is the
    // discriminator, and it is a state check rather than a date comparison: a run recorded before
    // this change carries no limit and no reason, and no clock is consulted to establish it.
    private const string ReworkMeasure = "reworkCausedByCoordinatorDecisions";
    private const string FailedDispatchMeasure = "failedDispatchesAttributableToCoordinator";
    private const string WastedDispatchMeasure = "wastedDispatches";

    // What ended a failed dispatch. Three values, and the third is the one that keeps the other two
    // honest: it is emitted whenever the record does not establish which condition ended the run, and
    // the row is then left unjudged rather than attributed on a guess.
    private const string ReachedItsLimit = "reachedTheLimitTheCoordinatorSet";
    private const string EndedInsideItsLimit = "endedInsideTheLimitTheCoordinatorSet";
    private const string TerminationNotRecorded = "whatEndedThisDispatchWasNotRecorded";

    // The one ground on which this projection will call a dispatch wasted, and it is a state check:
    // the run carries a provider reason and no manifest hash, so the adapter never received a brief.
    // AgentRun.ManifestHash is assigned only once the request is fully built and immediately before
    // the adapter is called, so its absence beside a failure is the launch dying ahead of the brief.
    private const string NoBriefReachedTheAdapter = "noBriefReachedTheAdapter";

    // The refusal site the CLI journals a refused launch under. The constant itself lives in
    // RefusalSite in AILedger.Storage, which Core cannot reference, so the string is repeated here
    // with its source named rather than the dependency being inverted for one literal.
    private const string ProviderLaunchSite = "provider-launch";

    // Every coordinator-authored dispatch, with the run as the record now holds it and the cause the
    // dispatch named. The run is read from state rather than from the run.started payload, because
    // the two fields these measures need are recorded at completion and the payload is the run as it
    // began.
    //
    // The population is the coordinator's own dispatches and not the log's, and one consequence of
    // that is worth stating plainly because it surprises a reader. A run.started an actor wrote while
    // it was itself the subject of a run is that run's work, so it is already out of the authored
    // list — and a launch an actor makes *for itself* has exactly that shape. So a self-dispatch is
    // not counted here at all, which also takes an operator's own filing runs out of the measure.
    //
    // That is the population rule CoordinatorSet settled for measures 4 to 8 rather than anything
    // these three introduce, and MC5 measured it on a live launch: three real provider launches with
    // no --subject reported no dispatches, and the same three with --subject reported three. A
    // coordinator dispatching to another actor — which is what `provider launch --subject` is for —
    // is measured.
    private static IReadOnlyList<DispatchRow> CollectDispatches(
        GovernedTaskState state,
        IReadOnlyList<AuthoredEvent> authored,
        SequenceIndex index)
    {
        var dispatches = new List<DispatchRow>();
        foreach (var row in authored)
        {
            if (row.Event.Data is not RunStarted started)
            {
                continue;
            }

            dispatches.Add(new DispatchRow(
                row.Position,
                state.Runs.TryGetValue(started.Run.Id, out var current) ? current : started.Run,
                index.DispatchCause(row.Event)));
        }

        return dispatches;
    }

    // Measure 10. A dispatch is rework attributable to a coordinator record when the record it names
    // as its cause is one this report has already found reversed — a claim later rejected or
    // corrected, a decision later superseded, or a disposition later reopened. The dispatch was spent
    // on a premise that did not hold.
    //
    // The avoidability travels from the reversal row rather than being decided again here, so measure
    // 10 makes no judgement of its own and the row leads back to the two sequences and the evidence id
    // the comparison was made from (R5). Nothing requires the reversal to follow the dispatch: a
    // dispatch made on a record that had already been reversed is worse and not better, and the row
    // shows the order.
    //
    // Three populations and not one count. "No rework" and "no dispatch here names anything" are
    // different answers, and a report that gave them one number would read as the first while meaning
    // the second — which is the whole reason this measure was absent rather than zero until now.
    private static CoordinatorRework BuildRework(
        IReadOnlyList<DispatchRow> dispatches,
        SequenceIndex index,
        IReadOnlyList<AvoidabilityCheck> reversedClaims,
        IReadOnlyList<AvoidabilityCheck> supersededDecisions,
        IReadOnlyList<AvoidabilityCheck> reopenedFindings)
    {
        var reversals = reversedClaims
            .Concat(supersededDecisions)
            .Concat(reopenedFindings)
            .ToLookup(check => check.RecordSequence);
        var joined = 0;
        var rows = new List<CoordinatorReworkDispatch>();
        foreach (var dispatch in dispatches)
        {
            if (dispatch.Cause is not { } cause || index.PositionOf(cause) is not { } position)
            {
                continue;
            }

            joined++;
            foreach (var check in reversals[position + 1])
            {
                rows.Add(new CoordinatorReworkDispatch(
                    dispatch.Run.Id.Value,
                    dispatch.Position + 1,
                    cause.Value,
                    check.Kind,
                    check.RecordId,
                    check.RecordSequence,
                    check.Avoidability,
                    check.Comparison));
            }
        }

        return new CoordinatorRework(
            dispatches.Count,
            joined,
            dispatches.Count - joined,
            [.. rows.OrderBy(row => row.DispatchSequence)],
            "Every dispatch a coordinating actor recorded on this task, whether by 'provider launch' " +
            "or by 'run start'. A dispatch is joined when it names a cause and that cause is an event " +
            "in this log; naming nothing, and naming an id this log does not hold, are both counted as " +
            "unjoined, because neither can be compared against anything.",
            joined == 0
                ? "No dispatch on this task names the coordinator record that prompted it, so no " +
                  "dispatch can be joined to a reversed record. The link is a causationId on " +
                  "run.started, set by passing '--cause' on the launch; it is recorded going forward " +
                  "and never backfilled, because an inferred causal link is a fabricated measurement " +
                  "(D7, PALT3)."
                : null);
    }

    // Measure 11. A dispatch that ended other than by completing, and whether the coordinator's own
    // decision is what ended it.
    //
    // The attribution is a recorded observation and neither a reading of the reason's text nor a
    // comparison of durations. AgentRun.EndedAtTheLaunchTimeout is set by the launcher from what the
    // process runner observed at the moment it ended the process — which cancellation source fired —
    // so this measure reports a fact the record establishes rather than deriving one.
    //
    // It used to be a duration comparison, and that was the defect. The interval available here is
    // the kernel's own, run.started to run.completed, and it is strictly wider than the provider's:
    // the ledger run opens before the manifest is built and the adapter is called, and closes after
    // the result is persisted. A provider that failed on its own terms at 1799.5 seconds of an
    // 1800-second launch therefore crossed the limit on the kernel's clock and was reported as ended
    // by the coordinator's limit, scoring the coordinator for a decision the record did not establish
    // (VC6, VE10). Rounding made it worse by pulling the boundary in further still.
    //
    // The two numbers stay on the row because they are worth reading, and the elapsed figure is no
    // longer rounded. Neither decides anything: a reader comparing them will find rows where the
    // interval crossed the limit and the verdict is not the timeout, and that disagreement is the
    // measure working rather than failing.
    //
    // Refused launches are counted here because the contract names them, and they are the same rows
    // measure 9 reports under the provider-launch site — stated so that a reader does not add the two
    // measures together and count them twice. A refused launch has no run to compare, which is
    // exactly why it is a count here and not a row.
    private static CoordinatorFailedDispatches BuildFailedDispatches(
        IReadOnlyList<DispatchRow> dispatches,
        CoordinatorSet coordinators,
        RetrospectiveRefusalJournal? refusals)
    {
        var launches = dispatches.Where(dispatch => dispatch.IsLaunch).ToArray();
        var judged = launches.Where(dispatch => dispatch.CarriesTheNewInputs).ToArray();
        var rows = new List<CoordinatorDispatchFailure>();
        foreach (var dispatch in judged)
        {
            if (dispatch.Run.TerminalFailureReason is not { } reason)
            {
                continue;
            }

            rows.Add(Classify(dispatch, reason));
        }

        // Rows the record leaves unjudged are still rows: the failure happened and its reason is
        // reported. What is withheld is the attribution, and when every row is withheld the measure
        // says so at its own level rather than leaving a reader to notice it row by row.
        var attributed = rows.Count(row => !string.Equals(
            row.Attribution, TerminationNotRecorded, StringComparison.Ordinal));

        return new CoordinatorFailedDispatches(
            launches.Length,
            judged.Length,
            [.. rows.OrderBy(row => row.Run, StringComparer.Ordinal)],
            RefusedLaunches(coordinators, refusals),
            "The coordinating actors' provider launches on this task. A launch is judged when it " +
            "carries the limit it was given, the provider's own terminal reason, or the launcher's " +
            "observation of what ended it; one carrying none of the three was recorded before those " +
            "inputs existed. Whether a dispatch failed is read from the provider's reason and never " +
            "from run.status, because a run whose agent returned an adverse verdict is a run that did " +
            "its job (C8). Whether the coordinator's own limit ended it is read from that recorded " +
            "observation and never from elapsed time, because the interval this projection can see is " +
            "wider than the provider's run (VC6). Refused launches are the same rows measure 9 reports " +
            "under the provider-launch site and must not be added to it twice.",
            judged.Length == 0
                ? "No launch on this task carries the limit it was given, the provider's own terminal " +
                  "reason, or what ended it, so a run that died at a limit the coordinator set too low " +
                  "cannot be told from one that failed on its own merits. All three are recorded going " +
                  "forward and none is ever backfilled (D7)."
                : rows.Count > 0 && attributed == 0
                    ? "Every failed dispatch here is reported and none is attributed: no row records " +
                      "which condition ended its run, and this measure will not supply that from " +
                      "elapsed time. The launcher records the observation going forward; until a " +
                      "dispatch carries one, whether the coordinator's limit ended a run is unknown " +
                      "rather than negative (VC6)."
                    : null);
    }

    private static CoordinatorDispatchFailure Classify(DispatchRow dispatch, string reason)
    {
        var limit = dispatch.Run.LaunchTimeoutSeconds;
        var elapsed = dispatch.ElapsedSeconds;
        if (dispatch.Run.EndedAtTheLaunchTimeout is not { } endedAtTheLimit)
        {
            return new CoordinatorDispatchFailure(
                dispatch.Run.Id.Value, limit, elapsed, reason,
                TerminationNotRecorded,
                "Nobody observed which condition ended this run: it was recorded before the launcher " +
                "carried that observation, was started by hand rather than by a launch, or its " +
                "launch died before the adapter returned. The row is left unjudged. Its elapsed time " +
                "is not consulted, because the interval here is run.started to run.completed — wider " +
                "than the provider's own run — so a run that failed on its own terms just short of " +
                "its limit crosses that limit on this clock (VC6).");
        }

        return new CoordinatorDispatchFailure(
            dispatch.Run.Id.Value, limit, elapsed, reason,
            endedAtTheLimit ? ReachedItsLimit : EndedInsideItsLimit,
            endedAtTheLimit
                ? "The process runner ended this provider because the deadline the launch was given " +
                  "expired, and recorded that it had. So the limit the coordinator set is what ended " +
                  "the run, and the provider's reason beside it is the account of a run that was cut " +
                  "off rather than of one that failed."
                : "The launcher watched this run end some way other than at its deadline, and " +
                  "recorded that. So it failed on its own terms and the limit the coordinator set is " +
                  "not what ended it — whatever the elapsed figure beside it looks like against the " +
                  "limit, since that interval is wider than the provider's own run.");
    }

    // Measure 12. A dispatch is wasted only when the run could not do the work it was sent to do —
    // and a verifier that finds nothing is not waste. That exclusion is not a caveat here, it is the
    // rule: nothing this method reads can classify a completed run at all, whatever its agent
    // concluded, because the only input it looks at is a provider failure on a launch that received
    // no brief.
    //
    // Two grounds, and both are state checks. A launch the kernel refused never became a run, so the
    // work was certainly not done; and a launched run that failed with no manifest hash is one whose
    // adapter never received a brief, which is the contract's own first example of waste.
    //
    // Everything else with a provider reason is counted and named rather than classified. Telling a
    // wrong scope or an unsatisfiable contract from an ordinary provider fault would mean reading the
    // reason's prose, and a projection that graded free text would be making the judgement this whole
    // file exists to avoid making.
    private static CoordinatorWastedDispatches BuildWastedDispatches(
        IReadOnlyList<DispatchRow> dispatches,
        CoordinatorSet coordinators,
        RetrospectiveRefusalJournal? refusals)
    {
        var judged = dispatches
            .Where(dispatch => dispatch.IsLaunch && dispatch.CarriesTheNewInputs)
            .ToArray();
        var rows = new List<CoordinatorWastedDispatch>();
        var unclassified = 0;
        foreach (var dispatch in judged)
        {
            if (dispatch.Run.TerminalFailureReason is not { } reason)
            {
                continue;
            }

            if (dispatch.Run.ManifestHash is null)
            {
                rows.Add(new CoordinatorWastedDispatch(
                    dispatch.Run.Id.Value, reason, NoBriefReachedTheAdapter,
                    "The run records a provider failure and no manifest hash. The hash is set only " +
                    "once the request is fully built and immediately before the adapter is called, so " +
                    "its absence means the adapter never received this run's brief and the run could " +
                    "not have done the work it was sent to do."));
                continue;
            }

            unclassified++;
        }

        return new CoordinatorWastedDispatches(
            judged.Length,
            [.. rows.OrderBy(row => row.Run, StringComparer.Ordinal)],
            unclassified,
            RefusedLaunches(coordinators, refusals),
            "The coordinating actors' provider launches that carry the inputs this measure needs. A " +
            "completed run is never counted here, whatever its agent concluded: a verifier that finds " +
            "nothing did the work it was sent to do. A launch that failed with a brief delivered is " +
            "counted under the unclassified figure and not as waste, because separating a wrong scope " +
            "from an ordinary provider fault would mean grading the reason's prose. Refused launches " +
            "are the same rows measure 9 reports under the provider-launch site.",
            judged.Length == 0
                ? "No launch on this task carries the provider's own terminal reason, so a run that " +
                  "could not do the work it was sent to do cannot be told from one that failed part " +
                  "way through doing it. The reason is recorded going forward and never backfilled (D7)."
                : null);
    }

    // The coordinating actors' refused launches, from the journal rather than from the log: a launch
    // the kernel refused appended no event, which is why the journal exists at all. Null when the
    // caller found no journal, and it stays null rather than becoming zero — a file nobody found and
    // a file holding nothing are different facts, which is the standard CoordinatorRefusals already
    // holds itself to.
    private static int? RefusedLaunches(
        CoordinatorSet coordinators,
        RetrospectiveRefusalJournal? refusals) =>
        refusals is null
            ? null
            : coordinators.Refusals(refusals)
                .Count(row => string.Equals(row.Site, ProviderLaunchSite, StringComparison.Ordinal));

    // Which of measures 10 to 12 still answers nothing, and why. This replaces the fixed list the
    // report used to carry: the three were unconditionally absent because nothing recorded their
    // inputs, and now each one is absent exactly when the dispatches on this task carry none of what
    // it needs. So the same three keys are still named for a task whose dispatches predate the
    // change, and a task with one dispatch after it reports figures instead.
    private static IReadOnlyList<CoordinatorAbsence> BuildForwardOnly(CoordinatorDispatches dispatches) =>
    [
        .. new (string Measure, string? Absence)[]
        {
            (ReworkMeasure, dispatches.Rework.Absence),
            (FailedDispatchMeasure, dispatches.FailedDispatches.Absence),
            (WastedDispatchMeasure, dispatches.WastedDispatches.Absence)
        }
        .Where(entry => entry.Absence is not null)
        .Select(entry => new CoordinatorAbsence(entry.Measure, entry.Absence!))
    ];

    // One dispatch, as measures 10 to 12 read it.
    //
    // IsLaunch keys on LaunchTokenHash because that is what tells a launch from a run started by
    // hand: the launcher is the only caller that sets it (IC3). Measures 11 and 12 are about launches
    // — a limit and a provider reason exist only for one — while measure 10's causal link is recorded
    // the same way for both, so it reads every dispatch.
    private sealed record DispatchRow(int Position, AgentRun Run, EventId? Cause)
    {
        public bool IsLaunch => Run.LaunchTokenHash is not null;

        // Whether this dispatch was recorded after these fields existed. A state check and not a date
        // comparison: a run recorded before them carries none of them, and no clock decides it.
        public bool CarriesTheNewInputs =>
            Run.LaunchTimeoutSeconds is not null ||
            Run.TerminalFailureReason is not null ||
            Run.EndedAtTheLaunchTimeout is not null;

        // The kernel's own interval for this run, unrounded. Null while the run is still open.
        //
        // Reported and never compared. It brackets the launcher's whole turn rather than the
        // provider's run, so it cannot decide whether a limit was reached; rounding it made that
        // worse by moving the boundary half a second, which is how a provider failing at 1799.5
        // seconds of an 1800-second launch was read as having reached the limit (VC6). The number is
        // kept on the row for a reader, at the precision the record actually holds.
        public double? ElapsedSeconds => Run.EndedAt is { } ended
            ? (ended - Run.StartedAt).TotalSeconds
            : null;
    }

    // Coordinator token cost, best-effort, from the harness transcript, in the same four buckets
    // AgentRun uses so that a coordinating session and a dispatched run compare without conversion
    // (D6). D3's premise — that a coordinator cannot observe its own token use — was false; its
    // caution survives in the form that matters, which is the two rules below.
    //
    // The source is named on the measurement. And where there is no record there is an absence, never
    // a zero and never an estimate: a codex-hosted coordinator writes no usage record at all, and a
    // zero would read as a coordinator that cost nothing.
    //
    // The identity check is the half only this side can do. The reader parses a file and reports what
    // it found there; this compares what it found against the session the report is about, so a
    // transcript from another conversation cannot be charged to this one — attention item R4.
    //
    // And the charged figure carries the caller's statement of what admitted its source, relayed
    // unchanged. The two checks establish where a transcript came from and not who wrote it, so a
    // reader who takes the four buckets for an audited total has read more into them than they hold;
    // the statement travels with the number so that reader does not have to find this source to learn
    // it (C7).
    private static CoordinatorTokenCost BuildTokenCost(
        GovernedTaskState state,
        CoordinatorUsageRead? usage)
    {
        if (usage is null ||
            (usage.SessionId is null && usage.Usage is null && usage.AbsenceReason is null))
        {
            return CoordinatorTokenCost.Absent(
                "noCoordinatorUsageSupplied: no harness transcript was named, so nothing was read. " +
                "This is an absence of a measurement, not a measurement of zero.");
        }

        if (usage.AbsenceReason is { } reason)
        {
            return CoordinatorTokenCost.Absent(reason);
        }

        if (usage.SessionId is not { } sessionId)
        {
            return CoordinatorTokenCost.Absent(
                "noCoordinatorSessionSelected: a usage record was read but no session was named, so " +
                "there is nothing to check its identity against.");
        }

        if (!state.CoordinatorSessions.TryGetValue(sessionId, out var session))
        {
            return CoordinatorTokenCost.Absent(
                $"coordinatorSessionUnknown: this task holds no session '{sessionId}'.");
        }

        // Reached when a caller supplied a session and neither a usage record nor a reason for its
        // absence, which no reader in this repository produces: every failure path in
        // CoordinatorUsageReader names its own reason. So this says what is true of the input and
        // makes no claim about the harness — the read that would have established what the harness
        // wrote is exactly the one that did not happen here.
        if (usage.Usage is not { } read)
        {
            return CoordinatorTokenCost.Absent(
                "noUsageRecordFound: the caller supplied neither a usage record nor a reason for its " +
                "absence, so nothing was read for this session and nothing is known about what the " +
                "harness wrote.");
        }

        if (session.HarnessSessionId is not { } expected)
        {
            return CoordinatorTokenCost.Absent(
                $"sessionRecordsNoHarnessIdentity: session '{sessionId}' was opened without a harness " +
                "session id, so a transcript cannot be shown to belong to it.");
        }

        if (!string.Equals(read.HarnessSessionId, expected, StringComparison.Ordinal))
        {
            return CoordinatorTokenCost.Absent(
                $"transcriptSessionMismatch: the transcript at '{read.SourcePath}' carries harness " +
                $"session '{read.HarnessSessionId ?? "none"}' while session '{sessionId}' recorded " +
                $"'{expected}'. Charging it here would attribute another conversation's cost to this one.");
        }

        return new CoordinatorTokenCost(
            read.SourcePath, read.Control, read.Model, sessionId.Value, read.HarnessSessionId,
            read.TokensInUncached, read.OutputTokens, read.TokensInCacheWrite, read.TokensInCacheRead,
            read.Records, read.UnreadableRows, null);
    }

    // The per-actor partition of measures 3, 5, 6 and 7, beside the task-wide figures rather than
    // instead of them. Both are wanted and they answer different questions: the task total says what
    // the loop cost, and the partition says whose record it was — which is the whole point of
    // measuring a coordinator rather than a task, because two coordinators work one task here and a
    // task-wide figure cannot tell one's record from the other's (D7, K2, VC2).
    //
    // The avoidability members carry record ids and not counts, for two reasons that agree. A bare
    // count is what D4 refuses; and an id is the more useful thing anyway, because it is the handle a
    // reader follows back to the row in the task-wide list, where both sequences and the evidence id
    // stand and the verdict can be checked rather than trusted (R5).
    private static IReadOnlyList<CoordinatorActorMeasures> BuildByActor(
        DelayRows delays,
        IReadOnlyList<AvoidabilityCheck> reversedClaims,
        IReadOnlyList<AvoidabilityCheck> supersededDecisions,
        IReadOnlyList<AvoidabilityCheck> reopenedFindings)
    {
        var actors = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var row in delays.ToDisposition.Concat(delays.ToDispatch))
        {
            actors.Add(row.Actor.Value);
        }

        foreach (var check in reversedClaims.Concat(supersededDecisions).Concat(reopenedFindings))
        {
            actors.Add(check.Actor);
        }

        return
        [
            .. actors.Select(actor => new CoordinatorActorMeasures(
                actor,
                // Unmatched is zero on both distributions here by construction, and it says something
                // true rather than standing in for a gap: this actor's population is the delays it
                // closed, and every one of those is matched. The rows nobody closed are counted on
                // the task-wide figure, which is the only place they can be counted honestly.
                Distribution([.. Minutes(delays.ToDisposition, actor)], 0),
                Distribution([.. Minutes(delays.ToDispatch, actor)], 0),
                Partition(reversedClaims, actor),
                Partition(supersededDecisions, actor),
                Partition(reopenedFindings, actor)))
        ];
    }

    private static IEnumerable<double> Minutes(IReadOnlyList<DelayRow> rows, string actor) =>
        rows.Where(row => string.Equals(row.Actor.Value, actor, StringComparison.Ordinal))
            .Select(row => row.Minutes);

    private static CoordinatorActorAvoidability Partition(
        IReadOnlyList<AvoidabilityCheck> checks,
        string actor)
    {
        var mine = checks
            .Where(check => string.Equals(check.Actor, actor, StringComparison.Ordinal))
            .ToArray();
        return new CoordinatorActorAvoidability(
            [.. Ids(mine, AvoidableError)],
            [.. Ids(mine, ReasonableRevision)],
            [.. Ids(mine, Unattributable)]);
    }

    private static IEnumerable<string> Ids(IReadOnlyList<AvoidabilityCheck> checks, string avoidability) =>
        checks.Where(check => string.Equals(check.Avoidability, avoidability, StringComparison.Ordinal))
            .Select(check => check.RecordId);

    private static bool IsTerminal(ClaimStatus status) =>
        status is ClaimStatus.Validated or ClaimStatus.Rejected or ClaimStatus.Superseded;

    private static DateTimeOffset Later(DateTimeOffset left, DateTimeOffset right) =>
        left > right ? left : right;

    private static DateTimeOffset Earlier(DateTimeOffset left, DateTimeOffset right) =>
        left < right ? left : right;

    private static double Hours(TimeSpan span) => Math.Round(span.TotalHours, 2);

    // The one place a sequence comparison is made, so that every avoidability verdict in the report
    // is made the same way and shows the same three things: the record's own sequence, the sequence
    // of the evidence the reversal rests on, and that evidence's id.
    //
    // Sequence is the record's position in the log the caller supplied, one-based. The log is
    // append-only and each event increments the task version, so position is a total order over what
    // was knowable at each point (C2, E2).
    private sealed class SequenceIndex
    {
        private readonly IReadOnlyList<LedgerEvent> _history;
        private readonly Dictionary<EvidenceId, (int Position, Evidence Evidence)> _evidence = [];
        // Where each event stands, so a causationId can be turned into a position without parsing the
        // id. EventId happens to encode the task version it was written at, and reading the number
        // out of the string would be a second, undocumented model of how ids are composed.
        private readonly Dictionary<EventId, int> _positions = [];
        private readonly Dictionary<DecisionId, Decision> _proposals = [];
        // Keyed by the predecessor: which replacement's acceptance is what took it out of force, and
        // where that acceptance stands. Built from the two events the state machine actually pairs,
        // so this holds a supersession that happened and never one that was only proposed.
        private readonly Dictionary<DecisionId, (int Position, Decision Replacement)> _accepted = [];

        public SequenceIndex(IReadOnlyList<LedgerEvent> history)
        {
            _history = history;
            for (var position = 0; position < history.Count; position++)
            {
                _positions[history[position].EventId] = position;
                switch (history[position].Data)
                {
                    case EvidenceAdded added:
                        _evidence[added.Evidence.Id] = (position, added.Evidence);
                        break;
                    case DecisionProposed proposed:
                        _proposals[proposed.Decision.Id] = proposed.Decision;
                        break;
                }
            }

            // A second pass, because a resolution names a decision by id and the proposal carrying
            // what it supersedes may be read after it in a partial log.
            for (var position = 0; position < history.Count; position++)
            {
                if (history[position].Data is not DecisionResolved { Status: DecisionStatus.Accepted } accepted ||
                    !_proposals.TryGetValue(accepted.DecisionId, out var replacement) ||
                    replacement.Supersedes is not { } predecessor)
                {
                    continue;
                }

                // Only one replacement can be accepted against a predecessor — the kernel refuses a
                // competing one once the predecessor is superseded — so the first is the one.
                if (!_accepted.ContainsKey(predecessor))
                {
                    _accepted[predecessor] = (position, replacement);
                }
            }
        }

        // Where an event stands in this log, or null when the id names no event here. Null is a real
        // case and not a defensive one: nothing validates that a --cause names an existing event, so
        // a dispatch can name an id this log does not hold, and measure 10 has to report such a
        // dispatch as one it could not join rather than as one that named nothing.
        public int? PositionOf(EventId id) =>
            _positions.TryGetValue(id, out var position) ? position : null;

        // The coordinator record that caused a dispatch, following the launch command's own internal
        // chain where it has to.
        //
        // A launch let through the brief-waiver door emits context.brief-waived first and run.started
        // second, and CommandHandler chains a command's second event to its first — so the record
        // named by --cause reaches the waiver and the run names the waiver. MC2 with ME2 is that
        // check, and ME2 is why it matters here rather than in theory: the one causationId on any of
        // the 433 run.started events in this ledger is exactly that waiver chain.
        //
        // The walk follows context.brief-waived and nothing else, because that is the only event
        // RunRules.StartRun emits ahead of the run. A chain that later grows a different second link
        // therefore stops at it and is reported as naming it, rather than being walked through
        // silently on the assumption that anything before a run is bookkeeping.
        public EventId? DispatchCause(LedgerEvent started)
        {
            var cause = started.CausationId;
            while (cause is { } named &&
                   PositionOf(named) is { } position &&
                   _history[position].Data is ContextBriefWaived)
            {
                cause = _history[position].CausationId;
            }

            return cause;
        }

        public AvoidabilityCheck Compare(
            string kind,
            string recordId,
            int recordPosition,
            (EvidenceId Id, int Position)? counterEvidence)
        {
            var recordSequence = recordPosition + 1;
            // The actor that wrote the record is read off the record's own event rather than passed
            // in, so the row cannot be attributed to anyone but its author.
            var actor = _history[recordPosition].ActorId.Value;
            if (counterEvidence is not { } evidence)
            {
                return new AvoidabilityCheck(
                    kind, actor, recordId, recordSequence, null, null, Unattributable,
                    $"The record stands at sequence {recordSequence} and the reversal names no " +
                    "evidence, so nothing can be compared against it. Reported as unattributable " +
                    "rather than as either of the two splits.");
            }

            var evidenceSequence = evidence.Position + 1;
            var avoidable = evidenceSequence < recordSequence;
            return new AvoidabilityCheck(
                kind, actor, recordId, recordSequence, evidence.Id.Value, evidenceSequence,
                avoidable ? AvoidableError : ReasonableRevision,
                avoidable
                    ? $"Evidence {evidence.Id.Value} stands at sequence {evidenceSequence}, below the " +
                      $"record's sequence {recordSequence}, so it was already in the log and available " +
                      "when the record was written."
                    : $"Evidence {evidence.Id.Value} stands at sequence {evidenceSequence}, above the " +
                      $"record's sequence {recordSequence}, so it arrived after the record was written.");
        }

        // The first later resolution that reverses a claim: a rejection, or a supersession the kernel
        // itself classified as a correction. A refinement is not a reversal — it sharpens the claim
        // and its dependents stay valid — so counting it as one would report a coordinator error for
        // the act of improving a claim.
        public (ClaimResolved Resolved, int Position)? FirstReversalOf(ClaimId claimId, int afterPosition)
        {
            for (var position = afterPosition + 1; position < _history.Count; position++)
            {
                if (_history[position].Data is ClaimResolved resolved && resolved.ClaimId == claimId &&
                    (resolved.Status == ClaimStatus.Rejected ||
                     (resolved.Status == ClaimStatus.Superseded &&
                      resolved.Outcome != SupersessionOutcome.Refinement)))
                {
                    return (resolved, position);
                }
            }

            return null;
        }

        // The evidence a reversal rests on, in the order of how directly it bears on the claim: the
        // records that refute it by name first, then anything else the reversal cited, then the
        // successor's own supporting evidence. The earliest of the chosen group is taken, because
        // availability is what is being asked — the first moment the refutation stood in the log.
        //
        // The order matters and is not a preference. A record that refutes the claim by name is the
        // substantive refutation, so it is what the comparison should be made against wherever one
        // exists; falling back to other cited records only where none does keeps the comparison
        // honest rather than merely non-empty.
        public (EvidenceId Id, int Position)? CounterEvidenceFor(
            ClaimId claimId,
            ClaimResolved resolved,
            int resolvedPosition)
        {
            var cited = resolved.EvidenceIds
                .Where(id => _evidence.ContainsKey(id))
                .Where(id => _evidence[id].Evidence.Refutes.Contains(claimId))
                .ToArray();
            if (cited.Length == 0)
            {
                cited = [.. resolved.EvidenceIds.Where(id => _evidence.ContainsKey(id))];
            }

            if (cited.Length == 0 && resolved.SupersededByClaimId is { } successor)
            {
                cited = [.. _evidence
                    .Where(entry => entry.Value.Evidence.Supports.Contains(successor))
                    .Select(entry => entry.Key)];
            }

            _ = resolvedPosition;
            return Earliest(cited);
        }

        // The first later record that takes a decision out of force: the resolution that marks it
        // superseded, an invalidation caused by a rejected claim, or a challenge that overturned it.
        //
        // Supersession is read from the resolution and not from the proposal, and that is the whole
        // of the fix. A proposal naming Supersedes is a request; the state machine takes the
        // predecessor out of force only in DecisionRules.ResolveDecision, and only when the
        // replacement is resolved **accepted** — the same command emits DecisionResolved for the
        // replacement and then DecisionResolved(predecessor, Superseded). A replacement that is
        // itself resolved superseded emits neither, and its predecessor stays current. Reading the
        // proposal as the overturn therefore reported a decision that was never superseded, and did
        // it in the one output where a wrong row is a false judgement about a coordinator (ZC1).
        //
        // The predecessor's own resolution is required and not merely the acceptance, because a
        // resolution marking a decision superseded is the event that ends it: without the pair, the
        // acceptance alone cannot distinguish a supersession from a proposal that was withdrawn.
        public DecisionOverturn? FirstOverturnOf(DecisionId decisionId, int afterPosition)
        {
            for (var position = afterPosition + 1; position < _history.Count; position++)
            {
                var data = _history[position].Data;
                if (data is DecisionResolved { Status: DecisionStatus.Superseded } superseded &&
                    superseded.DecisionId == decisionId)
                {
                    if (_accepted.TryGetValue(decisionId, out var replacement) &&
                        replacement.Position < position)
                    {
                        return new DecisionOverturn(position, replacement.Replacement, null, null);
                    }

                    // A decision resolved superseded with no accepted replacement behind it is a
                    // proposal that was dropped rather than a decision that was replaced. Nothing
                    // was taken out of force, so there is nothing to compare and no row to emit.
                    continue;
                }

                if (data is DecisionInvalidated invalidated && invalidated.DecisionId == decisionId)
                {
                    return new DecisionOverturn(position, null, invalidated.RejectedClaimId, null);
                }

                if (data is DecisionOverturned overturned && overturned.DecisionId == decisionId)
                {
                    return new DecisionOverturn(position, null, null, overturned.ChallengeId);
                }
            }

            return null;
        }

        // What the record that ended a decision rests on. A successor decision rests on the evidence
        // supporting the claims it depends on; an invalidation rests on the evidence that refuted the
        // claim it names; an overturn rests on the challenge's own evidence.
        public (EvidenceId Id, int Position)? SuccessorEvidenceFor(DecisionId decisionId, DecisionOverturn overturn)
        {
            _ = decisionId;
            if (overturn.Successor is { } successor)
            {
                return Earliest(_evidence
                    .Where(entry => entry.Value.Evidence.Supports
                        .Any(claim => successor.DependsOnClaims.Contains(claim)))
                    .Select(entry => entry.Key));
            }

            if (overturn.RejectedClaimId is { } rejected)
            {
                return Earliest(_evidence
                    .Where(entry => entry.Value.Evidence.Refutes.Contains(rejected))
                    .Select(entry => entry.Key));
            }

            if (overturn.ChallengeId is { } challengeId)
            {
                var challenge = _history
                    .Select(@event => @event.Data)
                    .OfType<ChallengeRaised>()
                    .FirstOrDefault(raised => raised.Challenge.Id == challengeId);
                return challenge is null ? null : Earliest(challenge.Challenge.EvidenceIds);
            }

            return null;
        }

        private (EvidenceId Id, int Position)? Earliest(IEnumerable<EvidenceId> candidates)
        {
            (EvidenceId Id, int Position)? earliest = null;
            foreach (var candidate in candidates)
            {
                if (!_evidence.TryGetValue(candidate, out var entry))
                {
                    continue;
                }

                if (earliest is null || entry.Position < earliest.Value.Position)
                {
                    earliest = (candidate, entry.Position);
                }
            }

            return earliest;
        }
    }

    // Which record ended a decision, reduced to the three shapes the log can produce. Exactly one of
    // the three is set, because a successor, an invalidation and an overturn rest on different
    // evidence and a record carrying two would say which of neither.
    internal sealed record DecisionOverturn(
        int Position,
        Decision? Successor,
        ClaimId? RejectedClaimId,
        ChallengeId? ChallengeId);

    // One measured delay and the actor that ended it. Internal to the walk: what leaves this class is
    // a distribution and never a row, because a list of individual delays is a raw population and the
    // measure is its shape.
    private sealed record DelayRow(ActorId Actor, double Minutes);

    private sealed record DelayRows(
        IReadOnlyList<DelayRow> ToDisposition,
        int DispositionUnmatched,
        IReadOnlyList<DelayRow> ToDispatch,
        int DispatchUnmatched);
}

// What the harness's own transcript said, handed across the boundary by the caller. Core reads no
// file under a user's home directory — nothing in this kernel does — so the read happens at the CLI
// and arrives here as data (PD2).
//
// Exactly one of Usage and AbsenceReason is set. A missing, unreadable, identity-mismatched or
// unsupported source is an absence with its reason named, never a zero and never an estimate.
public sealed record CoordinatorUsageRead(
    CoordinatorSessionId? SessionId,
    CoordinatorUsageRecord? Usage,
    string? AbsenceReason);

// The four buckets AgentRun already uses, so a coordinating session and a dispatched run are
// comparable without conversion, plus the source that was read and the harness identity that source
// claims. Records is how many per-request usage rows the total was summed over — a population, not a
// denominator.
//
// Control is what admitted the file, in the supplier's own words, and it is required rather than
// optional. This projection cannot state it: the check runs on the other side of the boundary and
// only the caller that ran it knows what it proved. A figure that reached the report without saying
// what admitted it would be read as stronger than it is, which is the whole of C7.
//
// UnreadableRows is how many lines of that source the caller could not parse, and it is required
// for the same reason CoordinatorRefusals carries one: a total summed over a file with unreadable
// lines in it is a floor and not a measurement. A live transcript with a half-written final line is
// the ordinary case — the harness is still appending to it while the report is being built — so the
// count travels rather than being dropped once at least one row parsed.
public sealed record CoordinatorUsageRecord(
    string SourcePath,
    string? Model,
    string? HarnessSessionId,
    long TokensInUncached,
    long OutputTokens,
    long TokensInCacheWrite,
    long TokensInCacheRead,
    int Records,
    string Control,
    int UnreadableRows);

// One measure this projection cannot answer, and why. Every entry is a state-checkable condition
// rather than a judgement, and the reason says what a reader may not conclude.
public sealed record CoordinatorAbsence(string Measure, string Reason);

public sealed record CoordinatorSessionMeasure(
    string Session,
    string Actor,
    string Harness,
    string? HarnessSessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    double? WallClockHours,
    double? IdleHours,
    double? IdleShare,
    int ChildRuns,
    int ChildRunsStillOpen,
    IReadOnlyList<string> Degradations);

// Measured is the population each figure came from and Unmatched is the population it could not
// describe — a finished run whose findings nobody disposed, or one after which nothing was ever
// dispatched again. Both are needed: a distribution over the matched rows alone would report a task
// that stalled forever as a task with no delay.
public sealed record CoordinatorDelayDistribution(
    int Measured,
    int Unmatched,
    double? MinMinutes,
    double? MedianMinutes,
    double? P95Minutes,
    double? MaxMinutes);

public sealed record CoordinatorDelays(
    CoordinatorDelayDistribution ToFindingDisposed,
    CoordinatorDelayDistribution ToNextDispatch,
    string Population);

// The ratio and its denominator, and deliberately not its numerator (D4). Absence is set instead of
// a ratio when nothing has been delivered or disposed yet, because a ratio over an empty denominator
// is no number rather than a large one.
public sealed record CoordinatorRatio(double? Ratio, int Denominator, string? Absence);

public sealed record CoordinatorRatios(
    CoordinatorRatio EventsPerDeliveredWorkItem,
    CoordinatorRatio EventsPerFindingDisposed);

// The avoidability split and the comparison that produced it. RecordSequence and EvidenceSequence
// are the two numbers it is derived from, and Comparison states the derivation in words — so a
// reader checks the split rather than trusting it (R5, C2).
//
// Avoidability is deliberately not called a verdict here, and the distinction is not cosmetic. This
// projection makes no judgement: it reports whether the refuting evidence already stood in the log
// below the record's own sequence, which is a fact about the log and is checkable from the two
// numbers beside it. ALT2 is that a projection which grades becomes a number an agent can move
// without doing the work.
public sealed record AvoidabilityCheck(
    string Kind,
    // The coordinator that wrote the record, on the row itself rather than only in the partition
    // below. A row that travels without its author cannot be attributed once it is quoted out of the
    // list it came from, and attributing one coordinator's reversal to another is the same class of
    // mistake as charging one session's tokens to another (VC2).
    string Actor,
    string RecordId,
    long RecordSequence,
    string? EvidenceId,
    long? EvidenceSequence,
    string Avoidability,
    string Comparison);

// The pair, and both halves are required. ReplacedBy and AreasHeldByReplacement are not nullable
// because a row cannot exist without the wider successor that makes it a mis-scope: a release with
// nothing after it is a release, and reporting one under this name emitted the classification while
// its own evidence was absent.
public sealed record ScopeReplacement(
    string AbandonedWorkItem,
    long AddedAtSequence,
    long AbandonedAtSequence,
    string ReplacedBy,
    int AreasHeld,
    int AreasHeldByReplacement,
    string AbandonReason,
    string MissingDimension);

// Message is the kernel's own text from the first row of the group, kept raw. The grouping key is
// that text with every quoted literal replaced by a placeholder, so a rule that names the id it
// refused is one lesson and not one per id — but a reader is shown what the kernel actually said,
// because a placeholder is not a refusal anybody read.
public sealed record CoordinatorRepeatedRefusal(
    string Actor,
    string Command,
    string Site,
    string Message,
    int Occurrences,
    int Repeats);

// FirstTime is the number of distinct refusal keys, which is what an actor was taught. Repeat is
// every later occurrence of a key that actor already hit, and it is avoidable by construction. Rows
// is a floor rather than a measurement whenever UnreadableRows is above zero.
//
// Rows counts the coordinating actors' rows and not the journal's, because this measure is about one
// loop's refusals. UnreadableRows is the journal's own count, which is what makes Rows a floor: a
// row nobody could parse carries no actor and so cannot be placed on either side of the filter.
public sealed record CoordinatorRefusals(
    int Rows,
    int UnreadableRows,
    int FirstTime,
    int Repeat,
    IReadOnlyList<CoordinatorRepeatedRefusal> RepeatedKeys,
    string Population);

// Control is set on every charged figure and on no absence: an absence already names the control
// that refused it, and a figure that named none would be read as resting on more than it does. It
// says what admitted the source — where the file came from, not who wrote it (C7).
//
// UnreadableRows makes the four buckets a floor whenever it is above zero, which is the standard
// CoordinatorRefusals already holds its own count to. Reporting the total alone while the reader
// had skipped lines is the same defect as reporting a partial population as a whole one, and the
// case that produces it is ordinary rather than exotic: a transcript the harness is still writing
// ends in a half-written line.
public sealed record CoordinatorTokenCost(
    string? Source,
    string? Control,
    string? Model,
    string? Session,
    string? HarnessSessionId,
    long? TokensInUncached,
    long? OutputTokens,
    long? TokensInCacheWrite,
    long? TokensInCacheRead,
    int? Records,
    int? UnreadableRows,
    string? Absence)
{
    public static CoordinatorTokenCost Absent(string reason) =>
        new(null, null, null, null, null, null, null, null, null, null, null, reason);
}

// Measures 10 to 12, together, because they read one population and one set of inputs. Each carries
// its own Absence, so a task can report one of the three and name the other two as absent — which is
// the state of a task whose dispatches straddle the change that added the inputs.
public sealed record CoordinatorDispatches(
    CoordinatorRework Rework,
    CoordinatorFailedDispatches FailedDispatches,
    CoordinatorWastedDispatches WastedDispatches);

// Measure 10. Dispatches is the population, Joined is the part of it that named a cause this log
// holds, and NamingNothing is the rest — a dispatch that named no cause, or one whose cause is an id
// this log does not carry. The three are reported together because an empty Rows list means "no
// dispatch rested on a record that was reversed" only when Joined is above zero; with Joined at zero
// it means nothing at all, and Absence says so.
//
// These are dispatch populations and not event counts: D4 refuses a raw event count as a measure, and
// what it refuses is volume standing in for work done. A population a measure was computed over is
// the opposite of that — it is what makes the measure checkable.
public sealed record CoordinatorRework(
    int Dispatches,
    int Joined,
    int NamingNothing,
    IReadOnlyList<CoordinatorReworkDispatch> Rows,
    string Population,
    string? Absence);

// One dispatch that rested on a coordinator record later found reversed. Avoidability and Comparison
// are copied from the reversal row rather than decided here, so this measure adds no judgement of its
// own and Record leads back to the row where both sequences and the evidence id stand (R5).
public sealed record CoordinatorReworkDispatch(
    string Run,
    long DispatchSequence,
    string Cause,
    string Kind,
    string Record,
    long RecordSequence,
    string Avoidability,
    string Comparison);

// Measure 11. Launches is every launch the coordinator made, Judged is the part carrying the inputs
// this measure needs, and Rows is the failures among those. RefusedLaunches is null when no journal
// was found and is deliberately not folded into the rows: a refused launch has no run and so no pair
// of numbers to compare.
public sealed record CoordinatorFailedDispatches(
    int Launches,
    int Judged,
    IReadOnlyList<CoordinatorDispatchFailure> Rows,
    int? RefusedLaunches,
    string Population,
    string? Absence);

// One failed dispatch, and what the record establishes about the coordinator's part in ending it.
//
// Reason is the provider's own text, verbatim, and is the field this measure treats as the truth about
// whether the dispatch failed — never run.status (C8). Attribution is one of three values, and the
// third says the record does not establish what ended the run; that row is unjudged and stays that way
// (VC6).
//
// LimitSeconds and ElapsedSeconds are context and not the basis. They used to be the basis, and the
// comparison between them was unsound because the second is wider than the provider's own run. They
// are still on the row because a reader wants them, and Basis says what the attribution actually
// rests on.
public sealed record CoordinatorDispatchFailure(
    string Run,
    int? LimitSeconds,
    double? ElapsedSeconds,
    string Reason,
    string Attribution,
    string Basis);

// Measure 12. Unclassified is the launches that failed with a brief delivered: counted, named, and
// deliberately not called waste, because telling a wrong scope from an ordinary provider fault would
// mean grading the reason's prose. A completed run never appears here at all, whatever its agent
// concluded.
public sealed record CoordinatorWastedDispatches(
    int Judged,
    IReadOnlyList<CoordinatorWastedDispatch> Rows,
    int Unclassified,
    int? RefusedLaunches,
    string Population,
    string? Absence);

// One dispatch that could not do the work it was sent to do, with the state check that establishes it
// rather than a reading of the reason's text.
public sealed record CoordinatorWastedDispatch(
    string Run,
    string Reason,
    string Ground,
    string Comparison);

public sealed record CoordinatorLoopReport(
    IReadOnlyList<string> CoordinatorActors,
    IReadOnlyList<CoordinatorSessionMeasure> Sessions,
    bool HasUnbracketedCoordinatorActivity,
    // Named Degradations rather than anything shorter on purpose: the projection carries no field
    // whose name a reader could take for a grade, and the guard in TaskRetrospectiveTests walks
    // every property name in this graph looking for exactly that.
    IReadOnlyList<CoordinatorAbsence> Degradations,
    CoordinatorDelays Delays,
    CoordinatorRatios Ratios,
    IReadOnlyList<AvoidabilityCheck> ReversedClaims,
    IReadOnlyList<AvoidabilityCheck> SupersededDecisions,
    IReadOnlyList<AvoidabilityCheck> ReopenedFindings,
    IReadOnlyList<ScopeReplacement> MisScopedWorkItems,
    CoordinatorRefusals? Refusals,
    CoordinatorTokenCost TokenCost,
    // Measures 10 to 12, which read the three inputs D7 added going forward. Each names its own
    // absence, and NotMeasured below carries the same absences as keys so a reader of that list alone
    // still learns which of the three answered nothing.
    CoordinatorDispatches Dispatches,
    IReadOnlyList<CoordinatorAbsence> NotMeasured,
    // Measures 3, 5, 6 and 7 again, partitioned by the coordinating actor, beside the task-wide
    // figures above rather than instead of them (D7, K2). Empty when one actor coordinated alone,
    // which is the ordinary case and reads correctly as one row.
    IReadOnlyList<CoordinatorActorMeasures> ByActor);

// Measures 3, 5, 6 and 7 for one coordinating actor. The task-wide figure says what the loop cost;
// this says whose record it was, which is what the measurement is for — two coordinators work one
// task here, and a task-wide number cannot tell one's record from the other's.
//
// The two distributions are the delays this actor closed: the gaps it ended by disposing a finding
// or by dispatching the next run. Their Unmatched is zero by construction, because a gap nobody
// closed belongs to no actor and is counted on the task-wide figure only.
public sealed record CoordinatorActorMeasures(
    string Actor,
    CoordinatorDelayDistribution ToFindingDisposed,
    CoordinatorDelayDistribution ToNextDispatch,
    CoordinatorActorAvoidability ReversedClaims,
    CoordinatorActorAvoidability SupersededDecisions,
    CoordinatorActorAvoidability ReopenedFindings);

// One actor's share of one avoidability measure, as the record ids and not as counts. A bare count
// is what D4 refuses, and an id is the more useful thing anyway: it is the handle a reader follows
// back to the row in the task-wide list, where both sequences and the evidence id stand and the
// comparison can be checked rather than trusted (R5).
public sealed record CoordinatorActorAvoidability(
    IReadOnlyList<string> AvoidableErrors,
    IReadOnlyList<string> ReasonableRevisions,
    IReadOnlyList<string> Unattributable);
