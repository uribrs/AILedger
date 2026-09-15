namespace AILedger.Core.Contracts;

public enum TaskStage
{
    Discovery,
    Research,
    Design,
    Scope,
    Ready,
    Execution,
    Verification,
    Repair,
    Review,
    Learn,
    Archive
}

public enum RoleKind
{
    Operator,
    PlanningLead,
    ImplementationLead,
    Researcher,
    Worker,
    Verifier,
    CodeReviewer
}

public enum Capability
{
    ManageRoles,
    ManageScope,
    AddClaim,
    ResolveClaim,
    AddEvidence,
    ProposeDecision,
    ResolveDecision,
    RaiseChallenge,
    DisposeChallenge,
    ManageWork,
    ManageRuns,
    RequestTransition,
    BuildContext,
    RaiseEscalation,
    ResolveEscalation,
    RecordAlternative,
    ManageConstraints,
    RecordArtifact
}


public enum LessonSourceKind
{
    ValidatedClaim,
    // A belief that was disproved, which is a different record from an approach that was
    // discarded. Only a rejected claim carries the evidence that refuted it, so it is the
    // record a Refuted lesson is minted from; RejectedAlternative carries a rationale for not
    // taking a path and no evidence at all. Conflating them loses the counter-evidence.
    RejectedClaim,
    RejectedAlternative,
    ResolvedEscalation,
    // A lesson carried in from the pre-kernel ledger, where the source was an assumption
    // disposition in a verifier's report rather than a record this kernel holds. No event ever
    // carries it: an imported lesson is written into the cross-repository store and read back by
    // recall, so it is never marked and never minted. 'lesson mark' must refuse it for that reason.
    Imported
}

public enum LessonClass
{
    Refuted,
    Untested,
    Drifted
}

// Which cognition established the lesson, and deliberately not RoleKind. This vocabulary is the
// ledger row format's, and the two do not line up: Executor and Recon have no role here, and
// Operator, PlanningLead, Worker and CodeReviewer mean nothing to a recalled row.
public enum LessonActor
{
    Researcher,
    Executor,
    Verifier,
    Recon
}

// What the lesson is about, which is not the same question as what kind of failure it records
// (LessonClass) or which record it was minted from (LessonSourceKind). Domain is a fact about the
// software the task was building. Workflow is a fact about how this kernel and its pipeline behave,
// which is worth carrying to a later task even when that task is in another repository building
// something unrelated. Absent reads as Domain: every lesson minted before this field existed is one.
public enum LessonKind
{
    Domain,
    Workflow
}

// Which way the verify command has to come out for the lesson to still hold. A verify is required
// to be runnable and was never required to be able to fail, so a grep for a symbol present in both
// the defective and the repaired state re-establishes nothing: it passes either way. The direction
// is what makes the check falsifiable.
public enum VerifyExpectation
{
    Present,
    Absent
}

public sealed record Provenance(ActorId ActorId, DateTimeOffset RecordedAt, string? Source);

public sealed record RoleAssignment(
    ActorId ActorId,
    RoleKind Role,
    IReadOnlyList<Capability> Capabilities,
    Provenance AssignedBy);

public sealed record Lesson(
    LessonId Id,
    TaskId SourceTaskId,
    LessonSourceKind SourceKind,
    string SourceRecordId,
    string Statement,
    string Outcome,
    IReadOnlyList<string> Citations,
    Provenance Provenance,
    LessonId? SupersedesLessonId = null,
    // Nullable only so histories written before lesson classification can still replay. New marks
    // require every metadata field at command time and therefore mint populated lessons.
    LessonClass? Class = null,
    string? Repo = null,
    IReadOnlyList<string>? Tags = null,
    // The three fields that make a recalled row actionable rather than prose: a command that
    // re-establishes the belief today, the prohibition it carries, and who established it.
    string? Verify = null,
    string? DoNot = null,
    LessonActor? Actor = null,
    // What the lesson is about, and not to be read as SourceKind: that names the record this was
    // minted from. Absent reads as Domain, which is what all 166 lessons minted before it are.
    LessonKind? Kind = null,
    // The roles this lesson is addressed to, checked against RoleKind rather than LessonActor
    // because the address is who needs to read it and LessonActor is who established it. Absent
    // reaches every role, which is what every lesson minted before this field existed means.
    IReadOnlyList<RoleKind>? Audience = null,
    // Which way Verify has to come out. Absent means the row records no direction and so cannot be
    // rechecked, which is the state every lesson minted before this field existed is in.
    VerifyExpectation? VerifyExpects = null);

public sealed record LessonMark(
    LessonMarkId Id,
    LessonSourceKind SourceKind,
    string SourceRecordId,
    LessonId? SupersedesLessonId,
    Provenance Provenance,
    LessonClass? Class = null,
    string? Repo = null,
    IReadOnlyList<string>? Tags = null,
    string? Verify = null,
    string? DoNot = null,
    LessonActor? Actor = null,
    LessonKind? Kind = null,
    IReadOnlyList<RoleKind>? Audience = null,
    VerifyExpectation? VerifyExpects = null);

// As on WorkItem above: the door a launch came through is recorded by the waiver event beside this
// run and projected into GovernedTaskState.ContextBriefWaivers, not copied onto the run.

// Who exercised a waiver. The actor remains on the event envelope; this records whether that actor
// was inside a coordinator bracket at the time, which is the distinction retrospective measurement
// needs. Session fields are absent for a manual waiver and for every waiver recorded before this
// field existed.
public enum WaiverOrigin
{
    Manual,
    CoordinatorSession
}

public sealed record WaiverProvenance(
    WaiverOrigin Origin,
    CoordinatorSessionId? CoordinatorSessionId = null,
    string? Harness = null,
    string? HarnessSessionId = null);
