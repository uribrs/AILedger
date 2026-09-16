namespace AILedger.Core.Contracts;

public sealed record StartRunCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    RunId RunId,
    WorkItemId? WorkItemId,
    string Provider,
    string? ProviderSessionId,
    string? Model = null,
    string? ProviderVersion = null,
    string? LaunchTokenHash = null,
    // The actor the run is for, when an operator dispatches on its behalf. ActorId stays the
    // authorising actor; the subject does the work and owns the run's provenance.
    ActorId? SubjectActorId = null,
    // As on AddWorkItemCommand: what the cognitive layer would serve the acting actor now. Read by
    // the gate on a provider launch only — a launch is the moment the dispatcher decomposes the
    // work, and LaunchTokenHash above is what tells a launch from a manually started run (IC3).
    IReadOnlyList<ContextSkill>? SkillsServedNow = null,
    // The two doors, as on AddWorkItemCommand, and read on a provider launch for the same reason
    // SkillsServedNow is: they open the gate this command is subject to, and a manually started run
    // is not subject to it.
    string? WithoutBriefReason = null,
    EvidenceId? StaleBriefEvidenceId = null,
    // The coordinating session dispatching this run, so the session owns its children (D1). Optional
    // and last: a run started outside a session, or before sessions existed, names none, and the
    // kernel refuses no run for its absence.
    CoordinatorSessionId? CoordinatorSessionId = null,
    AssuranceBinding? Assurance = null) : LedgerCommand(ActorId, CausationId, CorrelationId);
