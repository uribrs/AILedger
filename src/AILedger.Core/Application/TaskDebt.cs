using AILedger.Core.Contracts;

namespace AILedger.Core.Application;

// What a task still owes: counts only, no score and no judgement.
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
    int LessonsCited)
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
        LessonsCited >= LessonsRecalled;

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

        return new TaskDebt(
            openClaims.Count,
            clearlySupported,
            awaitingVerification,
            recalled,
            cited.Count);
    }
}
