using System.Text.Json.Serialization;
using AILedger.Core.Contracts;

namespace AILedger.Core.Application;

// What governance did on one task and what it cost: counts, durations, and the causal chains the
// event log can already join. No score, no grade, no overall number, anywhere (ALT2). TaskDebt.cs:8
// settled the principle for `status` and this is the same shape over the log instead of over state:
// the moment a projection makes a judgement it becomes a number an agent can move without doing the
// work, which is worse than no number at all.
//
// It reads. It refuses nothing, writes nothing, and has no replay counterpart, for the reason
// TaskDebt has none. It must never be fired from the archive transition either: archiving mints
// lessons inside one event batch, and a detached process on that path buys a half-written
// retrospective on a successfully archived task and a failure mode with nowhere to report (ALT1).
//
// Two things in the output are load-bearing and easy to mistake for polish.
//
// NotMeasured is a required field, not a courtesy. C1: four of self-scoring's ten dimensions are
// answerable from a task's record and six are not, and a projection that omits what it cannot see
// invites a scoring agent to fill the gap from prose instead.
//
// An unmeasured cost is not a zero. C4: only five of 299 runs recorded before build 459b575 carry
// any cost field, because the provider stream was written to stdout and dropped. So every total
// below is null when no run reported it, and each carries the count of the runs it was summed over
// — its own count, because the six cost fields are independent and one run may report one of them
// and none of the rest (VC2). A retrospective that summed nulls to zero would read as evidence that
// governance is cheap when it is evidence that nobody measured; one that stated a total beside a
// population that did not measure it would be the same error with a number attached.
public static class TaskRetrospective
{
    // Below a minute, and paired with an absent manifest, is what a launch that died before its
    // brief was built looks like. The duration is load-bearing rather than decorative: a null
    // manifest hash also means the run predates that field, so the hash alone cannot tell a launch
    // that failed early from one recorded before delivery was tracked at all.
    private static readonly TimeSpan DiedBeforeBriefingCeiling = TimeSpan.FromMinutes(1);

    // The provider string an operator's own filing run carries. It is a real run in the log with no
    // agent behind it, so it holds no manifest and no cost by construction and must not be counted
    // as a launch that died.
    private const string OperatorFilingProvider = "none";

    public static TaskRetrospectiveReport Build(
        GovernedTaskState state,
        IReadOnlyList<LedgerEvent> history,
        IReadOnlyList<RetrospectiveRefusal>? refusals)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(history);

        var debt = TaskDebt.Compute(state);
        var runs = BuildRuns(state);
        var cost = BuildCost(state, runs.Total);

        return new TaskRetrospectiveReport(
            state.TaskId,
            state.Stage,
            state.Version,
            WallClockHours(history),
            AgentMinutes(state),
            CountEvents(history),
            runs,
            cost,
            BuildEpistemic(state, debt),
            BuildCausalChains(state, history),
            CountBy(history, @event => @event.ActorId.Value),
            BuildRefusals(refusals),
            BuildStages(history),
            BuildArtifacts(state),
            BuildEscalations(state, history),
            BuildLessons(state, debt),
            BuildWorkItems(state, history),
            BuildNotMeasured(state, runs, cost, refusals, debt));
    }

    // Two different numbers, and both belong. They differed by 2x on every task the hand run
    // measured. Neither includes the coordinating session, which holds no run at all — that is what
    // `coordinatorCost` in NotMeasured names, and it is the largest single omission here.
    //
    // Min and Max rather than the first and last element: the log is append-only so file order is
    // chronological, but this projection is handed a list by its caller and does not need to trust
    // that. An empty history is a task that was never opened, which the command surface refuses
    // before reaching here; zero is returned rather than throwing over a read.
    private static double WallClockHours(IReadOnlyList<LedgerEvent> history)
    {
        if (history.Count == 0)
        {
            return 0;
        }

        var first = history.Min(@event => @event.RecordedAt);
        var last = history.Max(@event => @event.RecordedAt);
        return Math.Round((last - first).TotalHours, 2);
    }

    // Summed over runs that ended. An active run has no end and contributes nothing, which
    // Runs.ByStatus already states. A run whose end precedes its start describes no elapsed time
    // and contributes zero rather than a negative, so one malformed row cannot pull the total down.
    private static int AgentMinutes(GovernedTaskState state) =>
        (int)Math.Round(state.Runs.Values
            .Where(run => run.EndedAt is not null)
            .Sum(run => Math.Max(0, (run.EndedAt!.Value - run.StartedAt).TotalMinutes)));

    // Only the event types this task actually recorded, sorted. A type absent from the log did not
    // occur, and that is unambiguous — unlike a null cost field, which could mean either "measured
    // nothing" or "not instrumented yet". The ambiguous absences are named in NotMeasured; this map
    // needs no zeroes to be honest.
    private static IReadOnlyDictionary<string, int> CountEvents(IReadOnlyList<LedgerEvent> history) =>
        CountBy(history, @event => EventTypeName(@event.Data.GetType()));

    private static RetrospectiveRuns BuildRuns(GovernedTaskState state)
    {
        var runs = state.Runs.Values.ToArray();
        return new RetrospectiveRuns(
            runs.Length,
            CountBy(runs, run => CamelCase(run.Status.ToString())),
            // A null subject role predates the field rather than meaning no role held the run, so it
            // is keyed as unrecorded and NotMeasured says so when any run carries one. Reading the
            // actor's present role instead would answer a different question, as WorkItemRules:297
            // records for the completion gate.
            CountBy(runs, run => run.SubjectRole is { } role ? CamelCase(role.ToString()) : "unrecorded"),
            // Verbatim, not normalised. One run in this repository carries the provider `claude-code`
            // because it was recorded by hand before `provider launch` existed; that is history, not
            // an inconsistency for a read to reconcile.
            CountBy(runs, run => run.Provider),
            CountBy(runs, run => run.ActorId.Value),
            runs.Count(run => run.ManifestHash is not null),
            runs.Count(HasAnyCostField),
            runs.Count(IsOperatorFilingRun),
            runs.Count(DiedBeforeBriefing));
    }

    private static bool IsOperatorFilingRun(AgentRun run) =>
        string.Equals(run.Provider, OperatorFilingProvider, StringComparison.OrdinalIgnoreCase);

    // An operator filing run is excluded before the test is applied. It holds no manifest and ends
    // in the same instant it started, so it satisfies both halves of the classification while
    // describing the opposite thing: a run nobody launched a provider for.
    private static bool DiedBeforeBriefing(AgentRun run) =>
        !IsOperatorFilingRun(run) &&
        run.ManifestHash is null &&
        run.EndedAt is { } ended &&
        ended - run.StartedAt < DiedBeforeBriefingCeiling;

    private static bool HasAnyCostField(AgentRun run) =>
        run.Turns is not null || run.OutputTokens is not null ||
        run.MillisecondsToFirstLedgerWrite is not null ||
        run.TokensInUncached is not null || run.TokensInCacheWrite is not null ||
        run.TokensInCacheRead is not null;

    // Every total is null when no run reported that field, and every total carries the count of runs
    // that reported it (C4, VC2). The six cost fields are independent — RunRules:242-247 states it
    // and enforces nothing across them — so one count cannot stand for all of them: a run that
    // reported output tokens and no input buckets would otherwise put a bucket total next to a
    // population that never measured it. Turns is the clearest case, because it is not one unit
    // across providers: claude states num_turns on its terminal event and codex states no turn count
    // at all, so a task run by both has a turn total describing only part of itself. That is the
    // two-populations defect RunCostReader:94 records, and the per-field count is what keeps each
    // total readable rather than comparable.
    //
    // RunsMeasured and RunsUnmeasured stay, and describe the any-field population — how many runs
    // reported any cost datum at all, which is `runs.costRecorded` restated with its complement.
    // They are not the denominator of any total below; each total carries its own.
    private static RetrospectiveCost BuildCost(GovernedTaskState state, int totalRuns)
    {
        var runs = state.Runs.Values.ToArray();
        var measured = runs.Count(HasAnyCostField);
        return new RetrospectiveCost(
            measured,
            totalRuns - measured,
            Measure(runs, run => run.Turns),
            Measure(runs, run => run.OutputTokens),
            // VC3: the run record already holds this and the projection used to read it as evidence
            // that a run was measured and then drop it, so the one measurement no provider reports —
            // the launcher takes it, which is why it means the same thing for both — went unstated.
            // Summed like every other field rather than averaged: a mean is a derived statistic that
            // invites comparison between tasks that ran different numbers of agents, and the count
            // beside the sum is what a reader needs to divide it themselves.
            Measure(runs, run => run.MillisecondsToFirstLedgerWrite),
            // Three buckets, never one total: they are billed at roughly 1x, 1.25x and 0.1x, so a
            // sum of them is not proportional to what the run cost (GovernanceModels:381-385).
            Measure(runs, run => run.TokensInUncached),
            Measure(runs, run => run.TokensInCacheWrite),
            Measure(runs, run => run.TokensInCacheRead));
    }

    private static RetrospectiveMeasure Measure(IReadOnlyList<AgentRun> runs, Func<AgentRun, long?> field)
    {
        var reported = runs
            .Select(field)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        return new RetrospectiveMeasure(reported.Length, reported.Length == 0 ? null : reported.Sum());
    }

    private static RetrospectiveEpistemic BuildEpistemic(GovernedTaskState state, TaskDebt debt)
    {
        var evidence = state.Evidence.Values.ToArray();
        return new RetrospectiveEpistemic(
            new RetrospectiveClaims(
                // Both debt figures come from TaskDebt and neither is recomputed here. C3: a second,
                // looser model of a rule that already exists is the defect KC1 and KC2 both were, and
                // VC1 is the same defect a third time — the open count was recomputed while the
                // supported subset beside it was read. The two definitions agree today, which is the
                // point rather than a defence: a change to what `status` calls an open claim would
                // move one of them and leave neither wrong on its own terms.
                debt.OpenClaims,
                // The other three statuses have no counterpart in TaskDebt, which models what a task
                // owes and not how its claims came out. Counting them here is the only model of them,
                // not a second one.
                state.Claims.Values.Count(claim => claim.Status == ClaimStatus.Validated),
                state.Claims.Values.Count(claim => claim.Status == ClaimStatus.Rejected),
                state.Claims.Values.Count(claim => claim.Status == ClaimStatus.Superseded)),
            debt.OpenClaimsWithSupportingEvidence,
            new RetrospectiveEvidence(
                evidence.Length,
                evidence.Count(item => item.Supports.Count > 0 && item.Refutes.Count == 0),
                evidence.Count(item => item.Refutes.Count > 0 && item.Supports.Count == 0),
                evidence.Count(item => item.Supports.Count > 0 && item.Refutes.Count > 0),
                evidence.Count(item => item.Supports.Count == 0 && item.Refutes.Count == 0)),
            new RetrospectiveDecisions(
                state.Decisions.Values.Count(decision => decision.Status == DecisionStatus.Proposed),
                state.Decisions.Values.Count(decision => decision.Status == DecisionStatus.Accepted),
                state.Decisions.Values.Count(decision => decision.Status == DecisionStatus.Superseded),
                state.Decisions.Values.Count(decision => decision.Status == DecisionStatus.Invalidated)),
            state.Alternatives.Count,
            new RetrospectiveChallenges(
                state.Challenges.Values.Count(challenge => challenge.Status == ChallengeStatus.Open),
                state.Challenges.Values.Count(challenge => challenge.Status == ChallengeStatus.Supported),
                state.Challenges.Values.Count(challenge => challenge.Status == ChallengeStatus.Rejected),
                state.Challenges.Values.Count(challenge => challenge.Status == ChallengeStatus.Withdrawn)));
    }

    // The chains come from the events and cannot come from state (C2, E2). decision.invalidated and
    // work.invalidated each carry the id of the claim whose rejection caused them,
    // claim.dependencies-repointed carries both claim ids, and decision.overturned carries the
    // challenge id — while state keeps only the resulting status. A projection built over state
    // alone reports that a decision is invalidated and cannot say what invalidated it, which is the
    // one thing self-scoring calls stronger evidence than counting runs.
    //
    // Emitted in log order, so a reader sees the sequence the task actually went through.
    private static IReadOnlyList<RetrospectiveCausalChain> BuildCausalChains(
        GovernedTaskState state,
        IReadOnlyList<LedgerEvent> history)
    {
        // The supersession outcome sits on claim.resolved, not on the repointing event beside it, so
        // it is joined rather than read. By construction today only a refinement repoints — a
        // correction invalidates instead (ClaimRules.cs:72-81) — but the outcome is read off the log
        // rather than assumed, because that derivation is exactly the kind of thing a later rule
        // change moves without moving this file.
        var supersessionOutcomes = history
            .Select(@event => @event.Data)
            .OfType<ClaimResolved>()
            .Where(resolved => resolved.Outcome is not null)
            .GroupBy(resolved => resolved.ClaimId)
            .ToDictionary(group => group.Key, group => group.Last().Outcome!.Value);

        // Only lessons this task inherited, matching how TaskDebt divides them: a lesson this task
        // minted at archive is citable from then on, and counting a citation of one as learning from
        // an earlier task is the defect KC4 was. Lessons.Recalled and Lessons.Cited below describe
        // the same population, so these chains must too.
        var inherited = state.Lessons.Values
            .Where(lesson => lesson.SourceTaskId != state.TaskId)
            .Select(lesson => lesson.Id)
            .ToHashSet();

        var chains = new List<RetrospectiveCausalChain>();
        foreach (var @event in history)
        {
            switch (@event.Data)
            {
                case DecisionInvalidated invalidated:
                    chains.Add(new RetrospectiveCausalChain(
                        "rejectedClaimInvalidatedDecision",
                        invalidated.RejectedClaimId.Value,
                        invalidated.DecisionId.Value));
                    break;
                case WorkItemInvalidated invalidated:
                    chains.Add(new RetrospectiveCausalChain(
                        "rejectedClaimInvalidatedWorkItem",
                        invalidated.RejectedClaimId.Value,
                        invalidated.WorkItemId.Value,
                        CamelCase(invalidated.Status.ToString())));
                    break;
                case ClaimDependenciesRepointed repointed:
                    chains.Add(new RetrospectiveCausalChain(
                        "claimSupersededRepointedDependents",
                        repointed.SupersededClaimId.Value,
                        repointed.ReplacementClaimId.Value,
                        supersessionOutcomes.TryGetValue(repointed.SupersededClaimId, out var outcome)
                            ? CamelCase(outcome.ToString())
                            : null));
                    break;
                case DecisionOverturned overturned:
                    chains.Add(new RetrospectiveCausalChain(
                        "challengeOverturnedDecision",
                        overturned.ChallengeId.Value,
                        overturned.DecisionId.Value));
                    break;
                case ClaimAdded added when Cited(added.Claim.FromLesson, inherited) is { } lesson:
                    chains.Add(LessonCitation(lesson, added.Claim.Id.Value, "claim"));
                    break;
                case DecisionProposed proposed when Cited(proposed.Decision.FromLesson, inherited) is { } lesson:
                    chains.Add(LessonCitation(lesson, proposed.Decision.Id.Value, "decision"));
                    break;
                case AlternativeRecorded recorded when Cited(recorded.Alternative.FromLesson, inherited) is { } lesson:
                    chains.Add(LessonCitation(lesson, recorded.Alternative.Id.Value, "alternative"));
                    break;
            }
        }

        return chains;
    }

    private static LessonId? Cited(LessonId? fromLesson, IReadOnlySet<LessonId> inherited) =>
        fromLesson is { } lesson && inherited.Contains(lesson) ? lesson : null;

    // One row per citing record, so Lessons.Cited — which counts distinct cited lessons — is the
    // smaller number whenever two records cite one lesson. Both describe the inherited population;
    // only the cardinality differs, and the record kind is carried so a reader can tell which.
    private static RetrospectiveCausalChain LessonCitation(LessonId lesson, string recordId, string recordKind) =>
        new("lessonCitedByRecord", lesson.Value, recordId, recordKind);

    // Null when the caller found no journal, and a zero total when it found an empty one. The
    // distinction is the same one the cost fields keep: a task that predates the refusal journal
    // recorded no refusals because nothing was writing them down, and NotMeasured says so.
    //
    // Sites, actors and commands are counted as the caller spelled them. The site vocabulary belongs
    // to the boundary that refused, not to this projection, so no key is invented for a site this
    // task never hit.
    private static RetrospectiveRefusals? BuildRefusals(IReadOnlyList<RetrospectiveRefusal>? refusals) =>
        refusals is null
            ? null
            : new RetrospectiveRefusals(
                refusals.Count,
                CountBy(refusals, refusal => refusal.Site),
                CountBy(refusals, refusal => refusal.ActorId.Value),
                CountBy(refusals, refusal => refusal.Command));

    // A stage prerequisite waiver leaves no durable trace in state — PendingStagePrerequisiteWaiver
    // is transient and internal, which is why TaskDebt:12-16 declines to count waivers at all. The
    // log is the only place they survive, and the reason is carried verbatim: the operator's own
    // words are the only faithful record of a decision the kernel was told not to enforce.
    private static RetrospectiveStages BuildStages(IReadOnlyList<LedgerEvent> history)
    {
        var data = history.Select(@event => @event.Data).ToArray();
        var waivers = data.OfType<StagePrerequisitesWaived>().ToArray();
        return new RetrospectiveStages(
            data.OfType<StageTransitioned>().Count(),
            waivers.Length,
            waivers.Select(waiver => waiver.Reason).ToArray());
    }

    private static RetrospectiveArtifacts BuildArtifacts(GovernedTaskState state) =>
        new(CountBy(state.Artifacts.Values, artifact => CamelCase(artifact.Kind.ToString())),
            state.Artifacts.Values.Count(artifact => artifact.SupersedesArtifactId is not null));

    // How long the operator was interrupted for. An escalation still open has no end, so its
    // duration is null rather than measured against a clock this projection deliberately does not
    // take: a read that answered differently on every invocation could not be compared with itself.
    private static IReadOnlyList<RetrospectiveEscalation> BuildEscalations(
        GovernedTaskState state,
        IReadOnlyList<LedgerEvent> history)
    {
        var resolvedAt = new Dictionary<EscalationId, DateTimeOffset>();
        foreach (var @event in history)
        {
            if (@event.Data is EscalationResolved resolved)
            {
                resolvedAt[resolved.EscalationId] = @event.RecordedAt;
            }
        }

        return state.Escalations.Values
            .Select(escalation => new RetrospectiveEscalation(
                escalation.Id.Value,
                escalation.Kind,
                escalation.Status,
                resolvedAt.TryGetValue(escalation.Id, out var ended)
                    ? Math.Round(Math.Max(0, (ended - escalation.Provenance.RecordedAt).TotalHours), 2)
                    : null,
                escalation.WorkItemId?.Value))
            .ToArray();
    }

    // Recalled and Cited come from TaskDebt for the reason in C3, and describe inherited lessons
    // only. Minted is this task's own output and is divided the same way TaskDebt divides it: recall
    // refuses a lesson whose source is this task, so the source id is the reliable divider.
    private static RetrospectiveLessons BuildLessons(GovernedTaskState state, TaskDebt debt) =>
        new(debt.LessonsRecalled,
            debt.LessonsCited,
            state.Lessons.Values.Count(lesson => lesson.SourceTaskId == state.TaskId),
            state.LessonMarks.Count);

    // The three verification questions the completion gate itself asks, asked with its own
    // predicates rather than re-derived from roles (C3). HasCompletedWorkingRun travels beside
    // VerifierRanAfterLatestWork because the second reads false for an item that has done no work at
    // all, and without the first that is indistinguishable from missing verification.
    //
    // The waiver reason is in the log and not in state: work.completed carries
    // withoutVerificationReason, and the item it produced records only that it is completed.
    private static IReadOnlyList<RetrospectiveWorkItem> BuildWorkItems(
        GovernedTaskState state,
        IReadOnlyList<LedgerEvent> history)
    {
        var waivedWith = new Dictionary<WorkItemId, string>();
        foreach (var @event in history)
        {
            if (@event.Data is WorkItemCompleted { WithoutVerificationReason: { } reason } completed)
            {
                waivedWith[completed.WorkItemId] = reason;
            }
        }

        return state.WorkItems.Values
            .Select(item => new RetrospectiveWorkItem(
                item.Id.Value,
                item.Status,
                item.ResourceScope.Count,
                WorkItemRules.HasCompletedWorkingRun(state, item.Id),
                WorkItemRules.HasVerifierRunAfterLatestWork(state, item.Id),
                WorkItemRules.ProviderThatVerifiedItsOwnWork(state, item.Id),
                waivedWith.TryGetValue(item.Id, out var reason) ? reason : null))
            .ToArray();
    }

    // Required, and derived rather than declared (C1). Every entry names something this task's own
    // record cannot answer, and each is a state-checkable condition rather than a judgement:
    //
    // coordinatorCost and outcomeQuality are unconditional. The coordinating session holds no run,
    // so it has no start, no end, no provider and no cost — by construction, not by omission — and
    // it is one of the cognitions on the machine. Nothing in the log says whether the delivered
    // result works.
    //
    // The rest are conditional because their absence is ambiguous in exactly one direction: a field
    // that reads empty for every run cannot be told apart from a field that did not exist when those
    // runs were recorded. That is why a zero here is reported as unmeasured and not as a finding.
    private static IReadOnlyList<string> BuildNotMeasured(
        GovernedTaskState state,
        RetrospectiveRuns runs,
        RetrospectiveCost cost,
        IReadOnlyList<RetrospectiveRefusal>? refusals,
        TaskDebt debt)
    {
        var notMeasured = new List<string> { "coordinatorCost", "outcomeQuality" };

        // Asked of the four token fields only, and not of "did any run report any cost datum" (VC2).
        // A run that reported its first-write latency and nothing else measured no tokens, and the
        // any-field question answers yes for it — which took this entry out of the list while every
        // token total below was still null.
        var runsThatMeasuredTokens =
            cost.OutputTokens.RunsMeasured + cost.TokensInUncached.RunsMeasured +
            cost.TokensInCacheWrite.RunsMeasured + cost.TokensInCacheRead.RunsMeasured;
        if (runs.Total > 0 && runsThatMeasuredTokens == 0)
        {
            notMeasured.Add("governanceCost.tokens");
        }

        // Zero briefs delivered across every run is the state a task that predates ManifestHash is
        // in — 0 of 22 on the hand run's oldest task — and it is not distinguishable from a task
        // whose every launch failed before building one.
        if (runs.Total > 0 && runs.BriefDelivered == 0)
        {
            notMeasured.Add("runs.briefDelivered");
        }

        if (state.Runs.Values.Any(run => run.SubjectRole is null))
        {
            notMeasured.Add("runs.bySubjectRole");
        }

        if (refusals is null)
        {
            notMeasured.Add("refusals");
        }

        // A task handed no lessons cannot demonstrate that it learned from any. Citation rate over
        // an empty population is not a low number, it is no number — and a dimension that scores a
        // task that learned and a task that had nothing to learn from identically is measuring
        // whether anyone typed the flag.
        if (debt.LessonsRecalled == 0)
        {
            notMeasured.Add("learningBehaviour");
        }

        return notMeasured;
    }

    private static IReadOnlyDictionary<string, int> CountBy<T>(
        IEnumerable<T> items, Func<T, string> key)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var name = key(item);
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        return counts;
    }

    // The event log's own type names, taken from the discriminators the serializer is registered
    // with rather than from a second table beside them. A table would drift the moment an event type
    // is added, and the count map would then name the CLR type for it while the log named the
    // discriminator.
    private static readonly IReadOnlyDictionary<Type, string> EventTypeNames = typeof(LedgerEventData)
        .GetCustomAttributes(typeof(JsonDerivedTypeAttribute), inherit: false)
        .Cast<JsonDerivedTypeAttribute>()
        .Where(attribute => attribute.TypeDiscriminator is string)
        .ToDictionary(attribute => attribute.DerivedType, attribute => (string)attribute.TypeDiscriminator!);

    // An unregistered event type cannot reach disk — the replay validator's default arm throws and
    // TaskReducer calls it before applying, which EventRegistrationTests pins. So this fallback is
    // unreachable through the command surface, and it is here because a read over telemetry must not
    // be the thing that throws on a history it did not expect.
    private static string EventTypeName(Type dataType) =>
        EventTypeNames.TryGetValue(dataType, out var name) ? name : dataType.Name;

    // Matches what JsonStringEnumConverter with the camel-case policy writes for these names, so a
    // dictionary key reads the same as the enum value it was derived from. Done here rather than
    // left to the serializer because a dictionary key follows DictionaryKeyPolicy and not the
    // property policy, and this projection's output should not change shape with the caller's
    // options.
    private static string CamelCase(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}

// One refused attempt, reduced to the three fields this projection groups by. The journal itself
// lives in AILedger.Storage and carries three more — when, at which task version, and the kernel's
// own refusal text — and Core cannot reference Storage because the dependency runs the other way.
//
// Three fields rather than six on purpose: a wider copy of RefusalRecord here would be a second
// model of a record that already exists, and this one is a parameter list rather than a model.
public sealed record RetrospectiveRefusal(ActorId ActorId, string Command, string Site);

public sealed record RetrospectiveRuns(
    int Total,
    IReadOnlyDictionary<string, int> ByStatus,
    IReadOnlyDictionary<string, int> BySubjectRole,
    IReadOnlyDictionary<string, int> ByProvider,
    IReadOnlyDictionary<string, int> ByActor,
    int BriefDelivered,
    int CostRecorded,
    int OperatorFilingRuns,
    int DiedBeforeBriefing);

// One cost field, summed over the runs that reported it, with that count beside it. Total is null
// when no run reported the field, and RunsMeasured is then zero — the two say the same thing twice
// on purpose, because a serializer that omits null keys would otherwise leave a reader unable to
// tell an absent measurement from an absent field.
//
// RunsMeasured is a population, not a denominator to divide the total by for an average. The
// division is the reader's to make; a mean stated here would be a derived number inviting
// comparison between tasks that ran different numbers of agents.
public sealed record RetrospectiveMeasure(int RunsMeasured, long? Total);

// RunsMeasured and RunsUnmeasured describe the any-field population: how many runs reported any cost
// datum at all. Every total below carries its own count instead, because the six fields are
// independent and a run may report one and none of the others (VC2, RunRules:242-247).
public sealed record RetrospectiveCost(
    int RunsMeasured,
    int RunsUnmeasured,
    RetrospectiveMeasure Turns,
    RetrospectiveMeasure OutputTokens,
    RetrospectiveMeasure MillisecondsToFirstLedgerWrite,
    RetrospectiveMeasure TokensInUncached,
    RetrospectiveMeasure TokensInCacheWrite,
    RetrospectiveMeasure TokensInCacheRead);

public sealed record RetrospectiveClaims(int Open, int Validated, int Rejected, int Superseded);

public sealed record RetrospectiveEvidence(
    int Total, int SupportsOnly, int RefutesOnly, int Both, int Neither);

public sealed record RetrospectiveDecisions(int Proposed, int Accepted, int Superseded, int Invalidated);

public sealed record RetrospectiveChallenges(int Open, int Supported, int Rejected, int Withdrawn);

public sealed record RetrospectiveEpistemic(
    RetrospectiveClaims Claims,
    int OpenClaimsWithSupportingEvidence,
    RetrospectiveEvidence Evidence,
    RetrospectiveDecisions Decisions,
    int Alternatives,
    RetrospectiveChallenges Challenges);

// What caused what, as the event said it. Detail carries the one extra field the causing event held
// — the resulting work item status, the supersession outcome, the kind of record that cited a lesson
// — and is null where the event carried nothing more.
public sealed record RetrospectiveCausalChain(string Kind, string From, string To, string? Detail = null);

public sealed record RetrospectiveRefusals(
    int Total,
    IReadOnlyDictionary<string, int> BySite,
    IReadOnlyDictionary<string, int> ByActor,
    IReadOnlyDictionary<string, int> ByCommand);

public sealed record RetrospectiveStages(
    int Transitions, int Waivers, IReadOnlyList<string> WaiverReasons);

public sealed record RetrospectiveArtifacts(
    IReadOnlyDictionary<string, int> ByKind, int Supersessions);

public sealed record RetrospectiveEscalation(
    string Id, EscalationKind Kind, EscalationStatus Status, double? OpenHours, string? WorkItem);

public sealed record RetrospectiveLessons(int Recalled, int Cited, int Minted, int Marks);

public sealed record RetrospectiveWorkItem(
    string Id,
    WorkItemStatus Status,
    int ScopeCount,
    bool HasCompletedWorkingRun,
    bool VerifierRanAfterLatestWork,
    string? ProviderThatVerifiedItsOwnWork,
    string? CompletedWithoutVerificationReason);

public sealed record TaskRetrospectiveReport(
    TaskId Task,
    TaskStage Stage,
    long Version,
    double WallClockHours,
    int AgentMinutes,
    IReadOnlyDictionary<string, int> Events,
    RetrospectiveRuns Runs,
    RetrospectiveCost Cost,
    RetrospectiveEpistemic Epistemic,
    IReadOnlyList<RetrospectiveCausalChain> CausalChains,
    IReadOnlyDictionary<string, int> Authorship,
    RetrospectiveRefusals? Refusals,
    RetrospectiveStages Stages,
    RetrospectiveArtifacts Artifacts,
    IReadOnlyList<RetrospectiveEscalation> Escalations,
    RetrospectiveLessons Lessons,
    IReadOnlyList<RetrospectiveWorkItem> WorkItems,
    IReadOnlyList<string> NotMeasured);
