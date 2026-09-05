namespace AILedger.Core.Contracts;

public enum AgentLaunchMode
{
    New,
    Resume
}

public enum PermissionProfile
{
    WorkspaceGoverned
}

public sealed record AgentLaunchRequest(
    RunId RunId,
    TaskId TaskId,
    ActorId ActorId,
    WorkItemId? WorkItemId,
    AgentLaunchMode Mode,
    string Provider,
    string ExecutablePath,
    string WorkingDirectory,
    string StandardInput,
    string? ProviderSessionId,
    PermissionProfile PermissionProfile,
    string? Model,
    string? OutputSchema,
    IReadOnlyList<string> AdditionalDirectories,
    IReadOnlyDictionary<string, string> Environment,
    TimeSpan Timeout);

public sealed record ProviderEvent(
    long Sequence,
    string Type,
    string RawJson,
    string? SessionId,
    bool IsTerminal,
    bool IsError);

public sealed record AgentRunResult(
    RunId RunId,
    string Provider,
    string? ProviderSessionId,
    AgentRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int ExitCode,
    string? FinalOutput,
    IReadOnlyList<ProviderEvent> Events,
    string StandardError,
    string DetectedVersion,
    IReadOnlyList<string> EffectiveArguments,
    bool IsResume,
    string? Failure);

public sealed record ProcessInvocation(
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    string StandardInput,
    IReadOnlyDictionary<string, string> Environment,
    TimeSpan Timeout);

public sealed record ProcessExit(int ExitCode, DateTimeOffset StartedAt, DateTimeOffset EndedAt);

public interface IProcessRunner
{
    Task<ProcessExit> RunAsync(
        ProcessInvocation invocation,
        Func<string, CancellationToken, ValueTask> onStandardOutputLine,
        Func<string, CancellationToken, ValueTask> onStandardErrorLine,
        CancellationToken cancellationToken);
}

public interface IAgentAdapter
{
    string Provider { get; }
    Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken);
    Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken);
}
