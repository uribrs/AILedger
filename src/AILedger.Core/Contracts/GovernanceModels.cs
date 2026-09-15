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
    // The provider a run declares at start. The literal below is the declaration that no provider
    // process will run at all: the run exists to hold a record — an artifact needs a producer run —
    // and there is no child, no session and nothing to resume. It is a declaration and not a
    // discovery, made before the work and immutable in the log, which is why the completion rule
    // may rely on it: a run cannot claim after the fact that it never had a provider.
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
    long? TokensInCacheRead = null,
    // The coordinating session that dispatched this run, when one was open and named itself. This is
    // a new field rather than LaunchedBy promoted, and the difference is the whole measurement:
    // LaunchedBy names an *actor*, and one actor coordinates many sessions over a task's life, so
    // grouping runs by it would collapse every conversation the operator ever had into one bracket
    // (D1, and the landmine recon recorded at GovernanceModels.cs:377-384).
    //
    // Nullable and trailing, like every field above it. Every run already on disk carries none, and
    // that absence is what "this run was dispatched before sessions existed, or outside one" means.
    // Nothing may refuse a run for lacking it.
    CoordinatorSessionId? CoordinatorSessionId = null,
    // How many provider lines the drain cut to the per-line cap. Two meanings, and they must stay
    // apart, because the whole reason this field exists is that a degraded stream used to be
    // indistinguishable from a whole one:
    //
    //   null  nobody counted — the run predates this field, was started outside provider launch, or
    //         died before its process produced an exit to read the count from.
    //   0     the stream was watched and no line was cut. This is the ordinary value and it is a
    //         measurement, not an absence.
    //
    // Stated here rather than left to a reader because MillisecondsToFirstLedgerWrite's null means
    // four different things, which is a filed defect on another task, and this is the same field
    // shape. A nonzero count means the provider stream on this run is incomplete: some line was cut
    // at one megabyte, so anything read from that line — a cost figure, a final output — is missing
    // its tail. The line itself, cut, is in the sidecar beside the run.
    //
    // Nullable and trailing like every field above it. Nothing refuses a run for lacking it, and
    // TaskTransitionValidator gains no arm that keys on it: every run.completed already on disk
    // carries none, and a replay rule keyed on a field older events lack has twice made a live task
    // permanently unreadable here (R4).
    int? TruncatedLines = null,
    // The limit the launch was given, in seconds. Without it a run that ended at its limit cannot be
    // told from one that failed on its own merits, and that confusion is what made a cancelled run
    // read as a governance failure rather than as a timeout the coordinator set too low. Measure 11
    // is the reader: it compares the run's own elapsed time against this number, which is a
    // comparison a reader can check, rather than matching a failure string.
    //
    //   null  no limit was recorded — the run predates this field, was started by hand rather than
    //         by a launch, or its launch could not parse the value it was given.
    //
    // Recorded at completion and not at start, like ManifestHash above and for the same reason: the
    // launcher composes the request after the kernel has recorded the run, so the number does not
    // exist at run.started (D7, K16).
    int? LaunchTimeoutSeconds = null,
    // Why the run ended other than by completing, in the provider's own words. It is the adapter's
    // own Failure string relayed unaltered, which is the sidecar's own failure field — and C8 is why
    // it must be this and not the status: run LR4 on 2026-09-10_0931-insightvm-throttle-404 is
    // recorded here as failed while its own sidecar reports completed, exit code 0 and no failure,
    // because the agent's verdict was FAIL. A verdict is a finding and not a failure of the run, so a
    // measure that read the status would count a verification that did its job.
    //
    // It is deliberately never composed from Status, and never set on a status the launcher itself
    // decided. A run the launcher re-closed as failed for filing no output artifact carries none: the
    // provider completed, and the field says what the provider said.
    //
    //   null  the provider reported no failure. Either the run completed on its own terms, or the
    //         run predates this field.
    //
    // MC3 is that this one field subsumes the exit code C8 also names: the adapter returns a reason
    // for every nonzero exit before it examines anything else, so a non-null reason is a superset of
    // a nonzero exit and a second field would record the same signal twice.
    //
    // Both fields are nullable and trailing like every field above them. Nothing refuses a run for
    // lacking either and no replay rule keys on either.
    //
    // MC4 with ME4 recorded that all three readers had been checked and that the memory projector
    // "needs no arm". The arm was indeed already there and that half was right; what the check
    // missed is that an arm which exists can still be incomplete, and this one was — it copied
    // neither field, so the memory projection reported both as absent while the log held them
    // (VC3, VE7). RunCompletionProjection now holds the mapping once for every reader, which is why
    // no reader is named here any more: there is nothing left to check per reader.
    string? TerminalFailureReason = null,
    // Which condition ended the run, established rather than inferred. This is the field measure 11
    // attributes on, and it exists because elapsed time cannot carry that fact: the kernel's own
    // run.started-to-run.completed interval brackets manifest construction before the adapter is
    // called and result persistence after it returns, so it is strictly wider than the provider's,
    // and a provider that failed on its own terms just short of an 1800-second limit fell on the
    // wrong side of the comparison and was reported as ended by the coordinator's limit (VC6, VE10).
    //
    //   true   the launch's own deadline expired and the process runner killed the provider. The
    //          runner observes which cancellation source fired; nothing here reads a clock.
    //   false  the run ended some other way, and the launcher watched it end.
    //   null   nobody observed which condition ended it — the run predates this field, was started
    //          by hand rather than by a launch, or its launch died before the adapter returned.
    //          Measure 11 leaves such a row unjudged; it never fills the gap by inference.
    //
    // Nullable and trailing like every field above it, for the same reason: every run.completed
    // already on disk carries none, and a replay rule keyed on a field older events lack has twice
    // made a live task permanently unreadable here.
    bool? EndedAtTheLaunchTimeout = null)
{
    /// <summary>
    /// The <see cref="Provider"/> value declaring that no provider process runs for this run. A run
    /// that declares it has no session to record and none to lose, which is what exempts it from
    /// the resumability rule on completion. It is the value 27 operator filing runs already carried
    /// before the rule read it.
    /// </summary>
    public const string NoProvider = "none";

    /// <summary>
    /// True when this run never had a provider process: it declared <see cref="NoProvider"/> at
    /// start and was not dispatched through the kernel's launch path. Both halves come from the
    /// <c>run.started</c> event, so neither can be asserted after the run has ended.
    /// </summary>
    public bool HasNoProviderSessionByDeclaration =>
        string.Equals(Provider, NoProvider, StringComparison.OrdinalIgnoreCase) &&
        LaunchTokenHash is null;
}
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

// One coordinating conversation, bracketed. A session has a start, an end, an actor and a harness
// identity, and it owns the runs it dispatched — which is the hierarchy the measurement needs:
// session, dispatched run, finding, disposition, next dispatch (D1).
//
// It is deliberately not an AgentRun. ALT1 records why: provider, model, provider session id and
// timeout are all meaningless here, and `cancelled` already carries four meanings on a run.
//
// Harness names the tool the coordinator is hosted in — `claude-code`, `codex-cli` — and
// HarnessSessionId is that tool's own identity for the conversation. The second is optional because
// a harness need not expose one, and it is the only thing a transcript can be identity-checked
// against: charging one session's usage to another is exactly the failure R4 names (D6, PD2).
public sealed record CoordinatorSession(
    CoordinatorSessionId Id,
    ActorId ActorId,
    string Harness,
    string? HarnessSessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt);
