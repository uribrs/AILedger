using System.Text.Json.Serialization;

namespace AILedger.Core.Contracts;

public sealed record LedgerEvent(
    int SchemaVersion,
    EventId EventId,
    TaskId TaskId,
    ActorId ActorId,
    DateTimeOffset RecordedAt,
    EventId? CausationId,
    string CorrelationId,
    LedgerEventData Data);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "eventType")]
[JsonDerivedType(typeof(TaskOpened), "task.opened")]
[JsonDerivedType(typeof(RoleAssigned), "actor.role-assigned")]
[JsonDerivedType(typeof(ClaimAdded), "claim.added")]
[JsonDerivedType(typeof(ClaimResolved), "claim.resolved")]
[JsonDerivedType(typeof(EvidenceAdded), "evidence.added")]
[JsonDerivedType(typeof(DecisionProposed), "decision.proposed")]
[JsonDerivedType(typeof(DecisionResolved), "decision.resolved")]
[JsonDerivedType(typeof(DecisionInvalidated), "decision.invalidated")]
[JsonDerivedType(typeof(ChallengeRaised), "challenge.raised")]
[JsonDerivedType(typeof(ChallengeDisposed), "challenge.disposed")]
[JsonDerivedType(typeof(WorkItemAdded), "work.added")]
[JsonDerivedType(typeof(WorkItemInvalidated), "work.invalidated")]
[JsonDerivedType(typeof(RunStarted), "run.started")]
[JsonDerivedType(typeof(RunCompleted), "run.completed")]
[JsonDerivedType(typeof(StagePrerequisitesWaived), "stage.prerequisites-waived")]
[JsonDerivedType(typeof(StageTransitioned), "stage.transitioned")]
[JsonDerivedType(typeof(EscalationRaised), "escalation.raised")]
[JsonDerivedType(typeof(EscalationResolved), "escalation.resolved")]
[JsonDerivedType(typeof(AlternativeRecorded), "alternative.recorded")]
[JsonDerivedType(typeof(ConstraintAdded), "constraint.added")]
[JsonDerivedType(typeof(ConstraintSuperseded), "constraint.superseded")]
[JsonDerivedType(typeof(WorkItemCompleted), "work.completed")]
[JsonDerivedType(typeof(WorkItemBlocked), "work.blocked")]
[JsonDerivedType(typeof(WorkItemUnblocked), "work.unblocked")]
[JsonDerivedType(typeof(WorkItemAbandoned), "work.abandoned")]
[JsonDerivedType(typeof(ClaimDependenciesRepointed), "claim.dependencies-repointed")]
[JsonDerivedType(typeof(DecisionOverturned), "decision.overturned")]
[JsonDerivedType(typeof(LessonMinted), "lesson.minted")]
[JsonDerivedType(typeof(LessonRecalled), "lesson.recalled")]
[JsonDerivedType(typeof(LessonMarked), "lesson.marked")]
[JsonDerivedType(typeof(ArtifactRecorded), "artifact.recorded")]
[JsonDerivedType(typeof(ContextBuilt), "context.built")]
[JsonDerivedType(typeof(ContextBriefWaived), "context.brief-waived")]
[JsonDerivedType(typeof(SessionStarted), "session.started")]
[JsonDerivedType(typeof(SessionCompleted), "session.completed")]
public abstract record LedgerEventData;

// Tags are optional and trail the original two fields: every task opened before they existed
// carries none, and replay must keep reading those histories.
public sealed record TaskOpened(string Title, string Goal, IReadOnlyList<string>? Tags = null) : LedgerEventData;
public sealed record RoleAssigned(RoleAssignment Assignment) : LedgerEventData;
public sealed record ClaimAdded(Claim Claim) : LedgerEventData;
public sealed record ClaimResolved(ClaimId ClaimId, ClaimStatus Status, IReadOnlyList<EvidenceId> EvidenceIds, ClaimId? SupersededByClaimId = null, SupersessionOutcome? Outcome = null) : LedgerEventData;
public sealed record EvidenceAdded(Evidence Evidence) : LedgerEventData;
public sealed record DecisionProposed(Decision Decision) : LedgerEventData;
public sealed record DecisionResolved(DecisionId DecisionId, DecisionStatus Status) : LedgerEventData;
public sealed record DecisionInvalidated(DecisionId DecisionId, ClaimId RejectedClaimId) : LedgerEventData;
public sealed record ChallengeRaised(Challenge Challenge) : LedgerEventData;
public sealed record ChallengeDisposed(ChallengeId ChallengeId, ChallengeStatus Status) : LedgerEventData;
public sealed record WorkItemAdded(WorkItem WorkItem) : LedgerEventData;
public sealed record WorkItemInvalidated(WorkItemId WorkItemId, ClaimId RejectedClaimId, WorkItemStatus Status) : LedgerEventData;
public sealed record RunStarted(AgentRun Run) : LedgerEventData;
public sealed record RunCompleted(
    RunId RunId,
    AgentRunStatus Status,
    string? ProviderSessionId,
    DateTimeOffset EndedAt,
    bool LauncherAuthorized = false,
    string? ManifestHash = null,
    int? ManifestArtifactCount = null,
    // What the run cost, learned later still than the manifest pair: the provider reports most of it
    // on its terminal event and the launcher measures the first-write time itself. Nullable and
    // trailing for the reason the manifest pair is — every run.completed already on disk carries
    // none, and replay must keep reading those.
    int? Turns = null,
    long? OutputTokens = null,
    long? MillisecondsToFirstLedgerWrite = null,
    // Which cognition the provider actually served, when it differs from the one asked for. Absent
    // means the requested model on run.started stands.
    string? Model = null,
    // Input tokens in three buckets rather than one total. An earlier revision left them off,
    // reasoning that codex reports cached reads inside input_tokens while claude reports them
    // outside it, so one field would compare two populations. C6 refuted the premise that made that
    // a reason to omit them: the two semantics are documented and simply opposite, so the mapping is
    // well-defined. C7 is why three fields and not one — the buckets are billed at roughly 1x, 1.25x
    // and 0.1x, so their sum is not proportional to cost. RunCostReader holds the mapping (D3, D4).
    //
    // 64-bit, like OutputTokens above. One codex run in E5 already reported 8796519 input tokens,
    // and RC2 is that a 32-bit counter would silently record nothing for the runs that cross its
    // limit — which are the expensive ones. Widening a persisted field after release is itself a
    // compatibility change, so it is done before any of these has ever been written.
    long? TokensInUncached = null,
    long? TokensInCacheWrite = null,
    long? TokensInCacheRead = null,
    // How many provider lines the drain cut to the per-line cap, learned the same way the cost
    // fields are: the launcher reads it off the adapter's result once the run has ended. Null means
    // nobody counted and zero means nothing was cut; AgentRun.TruncatedLines states why that
    // distinction is load-bearing. Nullable and trailing for the reason the cost fields are — every
    // run.completed already on disk carries none, and replay must keep reading those.
    int? TruncatedLines = null,
    // The limit the launch was given and why the run ended other than by completing, both learned
    // the way the cost fields are: the launcher holds the request it built and the result the adapter
    // returned, and neither exists at run.started. AgentRun states what each null means and why the
    // reason is the provider's own Failure string rather than anything derived from Status (C8, MC3).
    // Nullable and trailing for the reason the cost fields are — every run.completed already on disk
    // carries neither, and replay must keep reading those.
    int? LaunchTimeoutSeconds = null,
    string? TerminalFailureReason = null,
    // Which condition ended the run, as the launcher observed it. AgentRun states what each of the
    // three states means and why measure 11 attributes on this rather than on elapsed time. Nullable
    // and trailing for the reason the two above it are — every run.completed already on disk carries
    // none, and replay must keep reading those.
    bool? EndedAtTheLaunchTimeout = null) : LedgerEventData;
public sealed record StagePrerequisitesWaived(TaskStage TargetStage, string Reason) : LedgerEventData;
public sealed record StageTransitioned(TaskStage Previous, TaskStage Current) : LedgerEventData;
public sealed record EscalationRaised(Escalation Escalation) : LedgerEventData;
public sealed record EscalationResolved(EscalationId EscalationId, EscalationStatus Status, string? Resolution, ActorId ResolvedBy) : LedgerEventData;
public sealed record AlternativeRecorded(Alternative Alternative) : LedgerEventData;
public sealed record ConstraintAdded(Constraint Constraint) : LedgerEventData;
public sealed record ConstraintSuperseded(ConstraintId ConstraintId) : LedgerEventData;
public sealed record WorkItemCompleted(WorkItemId WorkItemId, string? WithoutVerificationReason = null) : LedgerEventData;
public sealed record WorkItemBlocked(WorkItemId WorkItemId, string Reason, EscalationId? EscalationId) : LedgerEventData;
public sealed record WorkItemUnblocked(WorkItemId WorkItemId) : LedgerEventData;
public sealed record WorkItemAbandoned(WorkItemId WorkItemId, string Reason) : LedgerEventData;
public sealed record ClaimDependenciesRepointed(ClaimId SupersededClaimId, ClaimId ReplacementClaimId) : LedgerEventData;
public sealed record DecisionOverturned(DecisionId DecisionId, ChallengeId ChallengeId) : LedgerEventData;
public sealed record LessonMinted(Lesson Lesson) : LedgerEventData;
public sealed record LessonRecalled(Lesson Lesson) : LedgerEventData;
public sealed record LessonMarked(LessonMark Mark) : LedgerEventData;
public sealed record ArtifactRecorded(GovernedArtifact Artifact) : LedgerEventData;
// Which brief was assembled, for whom, and what it carried. The actor is not repeated here: it is
// the envelope's ActorId on the same line of events.jsonl, and two copies of one fact are two
// things that can disagree. Skills are ordered as served, and each carries the hash of the content
// that was served — an id alone would leave an event that proves nothing after the skill is edited.
public sealed record ContextBuilt(
    RoleKind Role,
    WorkItemId? WorkItemId,
    IReadOnlyList<ContextSkill> Skills) : LedgerEventData;
// One command proceeded past the context gate without a current brief, and what carried it. Emitted
// before the event it accompanies, the way StagePrerequisitesWaived precedes its transition, so a
// later reader sees which gate was opened rather than only that work was added.
//
// Exactly one of the two justifications is set, because an absent brief and a stale one are
// different failures (C7). An absent brief can only be carried by an operator's decision — there is
// no evidence that an unread brief was read — and a stale one by an evidence record naming what
// changed. A waiver carrying both would say which was true of neither.
public sealed record ContextBriefWaived(
    string Action,
    string? OperatorReason = null,
    EvidenceId? StaleBriefEvidenceId = null) : LedgerEventData;
// The two ends of a coordinating session's bracket. They are the only new persisted facts this
// measurement needs: everything else it reports is derived from actorId, sequence and timestamp over
// events that already exist (D2).
//
// Optional for the whole existing history, and that is not a courtesy either. All 7,019 events in
// this repository were written with no session, no command may be refused for lacking one, and no
// boundary is ever inferred from a time gap, a task lifetime or an actor lifetime — an inferred
// bracket is a fabricated measurement, which is PALT3 and attention item R2 (D7).
//
// Three readers of the log get an arm for these in the same change: CommandHandler at command time,
// TaskTransitionValidator at replay, and the memory index's own HistoricalLedgerProjector, whose
// switch throws on event data it has no arm for and which silently stopped that index rebuilding on
// 2026-09-10 for exactly that reason. The third reader is named by role rather than by project
// here, because the kernel may not so much as mention that project's name — R1 in the shadow
// boundary tests scans every kernel source for it.
public sealed record SessionStarted(CoordinatorSession Session) : LedgerEventData;
public sealed record SessionCompleted(CoordinatorSessionId SessionId, DateTimeOffset EndedAt) : LedgerEventData;
