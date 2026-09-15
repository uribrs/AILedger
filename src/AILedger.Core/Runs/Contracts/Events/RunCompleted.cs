namespace AILedger.Core.Contracts;

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
