using AILedger.Core.Contracts;

namespace AILedger.Core.Application;

// What a task still owes: counts, and one fact. No score and no judgement.
//
// The fact is whether an archived task carries a workflow retrospective. It is a boolean rather
// than a count because a count that can only be zero or one is a fact wearing the shape of a
// measure, and the boolean is the honest shape for it. The invariant worth keeping here was never
// that every member is a number; it is that no member is a judgement.
//
// This has no replay counterpart on purpose. It is a projection over state that is already in
// memory, it is written by nothing and read by `status`, and it refuses nothing. The moment it
// makes a judgement it becomes a number an agent can move without doing the work, which is worse
// than no number at all.
//
// The waiver count that belongs on this list is deliberately absent: a stage prerequisite waiver
// leaves no durable trace in GovernedTaskState — PendingStagePrerequisiteWaiver is transient and
// internal — so counting waivers requires reading the event log, which `status` does not do. That
// is recorded as its own claim rather than fixed by widening state, because state.json is
// byte-compared against a fresh replay and every field added to it changes every task on disk.
public sealed record TaskDebt(
    int OpenClaims,
    int OpenClaimsWithSupportingEvidence,
    int WorkItemsAwaitingVerification,
    int LessonsRecalled,
    int LessonsCited,
    // A fact about artifacts, not a count: a task either carries a retrospective or it does not, and
    // a count that can only be zero or one would read as a measure of something. Appended last so
    // that nothing already reading this record by position moves.
    bool RetrospectiveOwed)
{
    // KC3: not serialised. The owed block is written only when the debt is not clear, so the field
    // could only ever read false in the output an operator sees.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsClear =>
        OpenClaims == 0 && WorkItemsAwaitingVerification == 0 &&
        // VC1: one citation used to clear the whole lesson debt, so a task that recalled six lessons
        // and cited one reported nothing owed while five went unexamined. The debt is per lesson,
        // not per task, and LessonsCited counts distinct cited ids — so it is cleared only when every
        // inherited lesson has been cited by something.
        LessonsCited >= LessonsRecalled &&
        // IC1: unconditional, and that is the choice rather than the default. It makes the owed block
        // appear on all thirty currently archived tasks, because none carries a retrospective and
        // none can until the calibration gate passes (C1). The two conditions that would suppress it
        // both cost more than the noise: the gate is a command-time entry condition on another task
        // and is not in any state this projection may read, and an "archived after" cutoff is a
        // judgement about timeliness rather than a fact about this task's artifacts. Either buys a
        // shorter status by making the projection assert something it did not establish. The
        // dilution argument that deferred the cross-task watcher is about a list of thirty read at
        // once; `status` answers about the one task the operator already named, and the debt is true
        // of it.
        !RetrospectiveOwed;

    public static TaskDebt Compute(GovernedTaskState state)
    {
        var openClaims = state.Claims.Values
            .Where(claim => claim.Status == ClaimStatus.Open)
            .Select(claim => claim.Id)
            .ToHashSet();

        // The batch a person can actually review in one pass: evidence points at it, and nothing
        // points the other way. A claim with evidence on both sides needs a judgement and is not
        // counted here.
        var supported = state.Evidence.Values.SelectMany(evidence => evidence.Supports).ToHashSet();
        var refuted = state.Evidence.Values.SelectMany(evidence => evidence.Refutes).ToHashSet();
        var clearlySupported = openClaims.Count(claim => supported.Contains(claim) && !refuted.Contains(claim));

        // Asked with the completion gate's own predicates rather than re-derived from roles. KC1 and
        // KC2 were both the same defect: a second, looser model of what work complete requires. An
        // item counts as awaiting verification when it has done work and would still be refused —
        // which covers a verifier that ran before the latest work, and one from the same provider
        // that wrote it, not only a missing verifier.
        var awaitingVerification = state.WorkItems.Values.Count(item =>
            item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Abandoned or WorkItemStatus.Stale) &&
            WorkItemRules.HasCompletedWorkingRun(state, item.Id) &&
            (!WorkItemRules.HasVerifierRunAfterLatestWork(state, item.Id) ||
             WorkItemRules.ProviderThatVerifiedItsOwnWork(state, item.Id) is not null));

        // state.Lessons holds both kinds and only one of them can be cited as an influence. A lesson
        // minted by this task at archive was produced by it, not handed to it, so counting those as
        // uncited debt makes every closed task look like it owes something forever. Recall refuses a
        // lesson whose source is this task, so the source id is the reliable divider.
        var inherited = state.Lessons.Values
            .Where(lesson => lesson.SourceTaskId != state.TaskId)
            .Select(lesson => lesson.Id)
            .ToHashSet();

        // KC4: the cited set was unfiltered while the recalled count excluded minted lessons, so a
        // citation of a self-minted lesson paid off an inherited lesson's debt. Both counts must
        // describe the same population or comparing them means nothing. A minted lesson enters
        // state.Lessons during the archive transition and is citable from then on, so this is
        // reachable rather than theoretical.
        var cited = state.Claims.Values.Select(claim => claim.FromLesson)
            .Concat(state.Decisions.Values.Select(decision => decision.FromLesson))
            .Concat(state.Alternatives.Values.Select(alternative => alternative.FromLesson))
            .Where(lesson => lesson is not null)
            .Select(lesson => lesson!.Value)
            .Where(inherited.Contains)
            .ToHashSet();

        var recalled = inherited.Count;

        // Read off the kind the state already holds, never off the body. Before Archive nothing is
        // owed on this count, because the entry condition at ArtifactRules.cs:236 refuses a
        // retrospective at any earlier stage, so a task that has not reached closeout cannot be in
        // arrears for one. IC2: asking whether any exists is the same question as asking whether a
        // current one exists, because a supersession must carry the predecessor's kind, so a kind
        // ever filed always has at least one current member.
        var retrospectiveOwed = state.Stage == TaskStage.Archive &&
            !state.Artifacts.Values.Any(artifact =>
                artifact.Kind == GovernedArtifactKind.WorkflowRetrospective);

        return new TaskDebt(
            openClaims.Count,
            clearlySupported,
            awaitingVerification,
            recalled,
            cited.Count,
            retrospectiveOwed);
    }
}
