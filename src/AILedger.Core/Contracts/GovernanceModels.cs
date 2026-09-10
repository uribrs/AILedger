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

public enum GovernedArtifactKind
{
    UserRequest,
    PromptContract,
    OrchestrationPlan,
    VerifierOutput,
    CodeReviewOutput
}

// Only two things may interrupt the operator: a tradeoff no amount of research
// settles, and a question the code and the sources cannot answer.
public enum EscalationKind
{
    BusinessDecision,
    TrueUnknown
}

public enum EscalationStatus
{
    Open,
    Resolved,
    Withdrawn
}

public enum ConstraintStatus
{
    Active,
    Superseded
}

public enum ClaimStatus
{
    Open,
    Validated,
    Rejected,
    Superseded
}

public enum DecisionStatus
{
    Proposed,
    Accepted,
    Superseded,
    Invalidated
}

public enum ChallengeStatus
{
    Open,
    Supported,
    Rejected,
    Withdrawn
}

public enum WorkItemStatus
{
    Proposed,
    Active,
    Paused,
    Blocked,
    Stale,
    Completed,
    // Work is released as well as finished. A dead end that can never be completed used to hold its
    // directory area forever, so nothing else could claim it.
    Abandoned
}

public enum AgentRunStatus
{
    Active,
    Completed,
    Failed,
    Cancelled,
    ProtocolError
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

// Superseding is not one act. A refinement sharpens a claim and its dependents stay
// valid; a correction narrows or contradicts it and they do not. The kernel derives
// which from state, never from a flag set by the actor doing the superseding.
public enum SupersessionOutcome
{
    Correction,
    Refinement
}

public sealed record Claim(
    ClaimId Id,
    string Statement,
    ClaimStatus Status,
    IReadOnlyList<EvidenceId> EvidenceIds,
    string? ConsequenceIfWrong,
    Provenance Provenance,
    ClaimId? SupersededByClaimId = null,
    // The lesson that prompted this record, when one did. Optional on purpose: the honest answer is
    // usually that no lesson caused it, and a required field would collect a plausible id rather
    // than a true one. An optional citation that is sometimes used is data; a mandatory one is a
    // field. Validated against the lessons this task recalled, never against the foreign store.
    LessonId? FromLesson = null);

public sealed record Evidence(
    EvidenceId Id,
    string SourceType,
    string Citation,
    string Summary,
    IReadOnlyList<ClaimId> Supports,
    IReadOnlyList<ClaimId> Refutes,
    Provenance Provenance);

public sealed record Decision(
    DecisionId Id,
    string Statement,
    DecisionStatus Status,
    string Rationale,
    IReadOnlyList<ClaimId> DependsOnClaims,
    DecisionId? Supersedes,
    Provenance Provenance,
    // The lesson that prompted this record, when one did. Optional on purpose: the honest answer is
    // usually that no lesson caused it, and a required field would collect a plausible id rather
    // than a true one. An optional citation that is sometimes used is data; a mandatory one is a
    // field. Validated against the lessons this task recalled, never against the foreign store.
    LessonId? FromLesson = null);

public sealed record Challenge(
    ChallengeId Id,
    string TargetType,
    string TargetId,
    string Reason,
    ChallengeStatus Status,
    IReadOnlyList<EvidenceId> EvidenceIds,
    Provenance Provenance);

public sealed record WorkItem(
    WorkItemId Id,
    string Title,
    ActorId? Owner,
    WorkItemStatus Status,
    IReadOnlyList<ClaimId> DependsOnClaims,
    IReadOnlyList<string> ResourceScope,
    string? BlockReason = null,
    string? AbandonReason = null,
    // Carried onto the item so the justification for holding several areas lands in the log. Kept
    // only on the command, the decision the kernel demanded would be checked and then thrown away.
    AlternativeId? NotSplitJustification = null,
    // The commit the item's work starts from, captured in the item's own scope directory rather than
    // the shell's, because a scope points into whichever repository holds the work and that is rarely
    // this one. Both the verifier and the code reviewer need it to scope a diff: the skills name it as
    // `state.json.baseRef`, and two verifier runs in a row fell back to comparing the change against
    // the goal and the constraints because the kernel had nowhere to put it. Null when the scope is
    // not a git work tree, which is the state every item recorded before this field existed is in.
    string? BaseRef = null);
// No field here for the door the context gate was opened through to add this item. The waiver is its
// own event immediately before this one and is the sole record of the justification; copying it here
// would be two records of one fact that can disagree. GovernedTaskState.ContextBriefWaivers is the
// projection that joins the two, and it is what `status` reads (GX1).

public sealed record Escalation(
    EscalationId Id,
    EscalationKind Kind,
    string Question,
    EscalationStatus Status,
    WorkItemId? WorkItemId,
    IReadOnlyList<string> Options,
    string? Recommendation,
    IReadOnlyList<EvidenceId> AttemptEvidenceIds,
    string? Resolution,
    ActorId? ResolvedBy,
    Provenance Provenance);

public sealed record Alternative(
    AlternativeId Id,
    string Statement,
    string RejectionRationale,
    DecisionId? ReplacedByDecisionId,
    Provenance Provenance,
    // The lesson that prompted this record, when one did. Optional on purpose: the honest answer is
    // usually that no lesson caused it, and a required field would collect a plausible id rather
    // than a true one. An optional citation that is sometimes used is data; a mandatory one is a
    // field. Validated against the lessons this task recalled, never against the foreign store.
    LessonId? FromLesson = null);

public sealed record Constraint(
    ConstraintId Id,
    string Statement,
    string Source,
    IReadOnlyList<string> Scope,
    ConstraintStatus Status,
    Provenance Provenance);

public sealed record GovernedArtifact(
    ArtifactId ArtifactId,
    GovernedArtifactKind Kind,
    string Title,
    string Content,
    WorkItemId? WorkItemId,
    RunId? ProducerRunId,
    ArtifactId? SupersedesArtifactId,
    Provenance Provenance);

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

public sealed record AgentRun(
    RunId Id,
    ActorId ActorId,
    WorkItemId? WorkItemId,
    string Provider,
    string? ProviderSessionId,
    AgentRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    // Which cognition actually ran. A score or a lesson earned against one model and CLI version
    // says nothing about another, and the adapter knew both and used to discard them.
    string? Model = null,
    string? ProviderVersion = null,
    // A run launched by the kernel is closed by the launcher, not by the agent inside it. The
    // launcher holds a secret for the run's lifetime and only its hash is recorded, so the agent
    // — which shares the run's actor identity — cannot authorise its own completion.
    string? LaunchTokenHash = null,
    // Who authorised this run, when that is not the actor doing the work. A role that must not hold
    // run authority — verifier, code reviewer — can only run if someone else dispatches for it, and
    // the ledger has to say who. Null is the ordinary case: the actor started its own run.
    ActorId? LaunchedBy = null,
    // The role the subject held when the run started. A role assignment can change afterwards, so
    // asking "was this work verified" of the current assignment answers a different question than
    // the one being asked. Null on every run recorded before this field existed.
    RoleKind? SubjectRole = null,
    // Which brief this run was handed to the adapter. The launcher builds the manifest after
    // starting the run, so these are set at completion, not at start.
    //
    // What the pair establishes is delivery, not reading: the manifest was fully built and passed to
    // the adapter for this run. It cannot show that the child consumed it, and no field here can.
    // Null means no brief reached the adapter — a launch that failed before or during request
    // construction, or a run started outside provider launch. That absence is the measurement, so
    // it must stay distinguishable from a manifest of zero artifacts.
    string? ManifestHash = null,
    int? ManifestArtifactCount = null,
    // What the run cost. Null is a third state and not a zero: it means nobody measured this, which
    // is true of every run recorded before these fields existed and of a launch that died before its
    // provider stream produced a terminal event.
    //
    // Turns is not one unit across providers. Claude states num_turns on its result event; codex
    // states no turn count at all, and counting its turn.completed events yields one by construction
    // (IC1). Null for codex is therefore the honest value, not a gap to be filled.
    int? Turns = null,
    // The provider's own output_tokens, unaltered, and one field for both providers. Codex
    // additionally reports reasoning_output_tokens; C9 settles that it is a subset breakdown of
    // output_tokens rather than an addend, and that both providers count every generated token here
    // and bill it at the output rate. So the number is comparable across providers, and adding the
    // reasoning count to it would double-count (E13, E14).
    //
    // 64-bit, and so are the three buckets below. Turns is not: a turn count is a provider's own
    // iteration count in the tens to low hundreds, while E5 already recorded 8796519 input tokens on
    // one run. RC2 is that a 32-bit token counter records nothing above its limit, and the runs that
    // cross it are the expensive ones, so the loss falls exactly where the measurement matters.
    long? OutputTokens = null,
    // How long the run took to reach the ledger for the first time, measured by the launcher rather
    // than reported by the provider, so it means the same thing for both. Null means the run made no
    // ledger write at all — the observability hole this measures — and is deliberately distinct from
    // zero, which means it wrote within the first millisecond.
    long? MillisecondsToFirstLedgerWrite = null,
    // Input tokens in three buckets, never one total: C7 measured them billed at roughly 1x, 1.25x
    // and 0.1x, so a sum is not proportional to what the run cost. Each provider reports them under
    // its own property names and its own convention about what the input total contains, and
    // RunCostReader holds that mapping in one place with C6 beside it (D3, D4).
    long? TokensInUncached = null,
    long? TokensInCacheWrite = null,
    long? TokensInCacheRead = null);
// As on WorkItem above: the door a launch came through is recorded by the waiver event beside this
// run and projected into GovernedTaskState.ContextBriefWaivers, not copied onto the run.
