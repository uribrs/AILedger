using System.Text.Json;
using AILedger.Cli.Verification;
using AILedger.Cli.Providers;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;

namespace AILedger.Cli.Dispatch;

internal sealed partial class ProviderDispatchService(
    Func<string, IAgentAdapter> _adapterFactory,
    LaunchContextPreparation _contextPreparation,
    IGovernedTaskService service,
    IGovernedTaskService _hostService,
    DispatchHostOptions _options,
    AILedger.Core.Authority.RoutineOrchestrationAuthority? _authority,
    JsonSerializerOptions _json,
    RefusalJournal _refusalJournal,
    ProviderRunRecorder _runRecorder,
    Func<VerificationHost> _verificationHost) : IProviderDispatchService
{
    public async Task<ProviderDispatchResult> LaunchAsync(ProviderDispatchRequest input, CancellationToken cancellationToken)
    {
        var receipt = new DispatchProgress(input);
        try
        {
            input = input with { AdditionalDirectories = input.AdditionalDirectories.ToArray(), AdditionalWork = input.AdditionalWork.ToArray() };
            await ExecuteAsync(input, receipt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            receipt.Fail(error);
        }
        if (_options.Checkpoint is { } checkpoint)
            await checkpoint.SaveAsync(input, receipt.Result(), null, CancellationToken.None).ConfigureAwait(false);
        return receipt.Result();
    }

    private async Task ExecuteAsync(ProviderDispatchRequest input, DispatchProgress receipt, CancellationToken token)
    {
        var prepared = await PrepareAsync(input, receipt, token).ConfigureAwait(false);
        var active = await AdmitAsync(prepared, receipt, token).ConfigureAwait(false);
        AgentRunResult result;
        try { result = await InvokeProviderAsync(active, receipt, token).ConfigureAwait(false); }
        catch (Exception error)
        {
            receipt.FaultPhase = receipt.Phase;
            await CloseFaultedLaunchAsync(active, receipt, error).ConfigureAwait(false);
            throw;
        }

        receipt.ProviderResult = result;
        receipt.Phase = DispatchPhase.ResultRetention;
        if (_options.Checkpoint is { } observation)
            await observation.SaveAsync(input, receipt.Result(), null, CancellationToken.None).ConfigureAwait(false);
        // Retain before ledger closure, using an independent lifetime even on cancellation.
        receipt.Retention = await _runRecorder.WriteResultAsync(_options.LedgerRoot, input.TaskId, result, service).ConfigureAwait(false);
        if (_options.Checkpoint is { } checkpoint)
            await checkpoint.SaveAsync(input, receipt.Result(), null, CancellationToken.None).ConfigureAwait(false);
        await CloseProviderResultAsync(active, receipt, result, token).ConfigureAwait(false);
    }

    private async Task<ActiveDispatch> AdmitAsync(PreparedDispatch prepared, DispatchProgress receipt, CancellationToken token)
    {
        var input = prepared.Input;
        receipt.Phase = DispatchPhase.ProviderProbe;
        var adapter = _adapterFactory(prepared.Provider);
        var executable = ExecutableResolver.Resolve(prepared.Provider, _options.Executable);
        var isolation = ProviderIsolationPreparation.ProtectExecutable(prepared.Isolation, executable);
        var version = await adapter.ProbeVersionAsync(executable, token).ConfigureAwait(false);
        // Only the hash enters storage. The secret stays in this invocation's private lifetime.
        var secret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var start = CreateStartRun(input, prepared.Provider, input.SessionId, version,
            CommandHandler.HashLaunchToken(secret), prepared.Skills) with { Assurance = prepared.Assurance, ExpectedVersion = input.ExpectedVersion };
        receipt.Phase = DispatchPhase.RunAdmission;
        receipt.StartRecording = LedgerRecordingStatus.Unknown;
        // Existing storage and kernel recheck authority/admission under the mutation lock.
        var started = await service.ExecuteAsync(input.TaskId, start, token).ConfigureAwait(false);
        receipt.StartRecording = LedgerRecordingStatus.Recorded;
        receipt.Started = DispatchProgress.EventReceipt(started, AgentRunStatus.Active);
        receipt.Phase = DispatchPhase.BriefDelivery;
        return new(prepared, adapter, executable, isolation, start, started, secret);
    }

    private sealed record PreparedDispatch(ProviderDispatchRequest Input, string Provider,
        GovernedTaskState State, IReadOnlyList<ContextSkill>? Skills, AssuranceBinding? Assurance,
        ProviderGrants Grants, ProviderIsolation Isolation);
    private sealed record ActiveDispatch(PreparedDispatch Prepared, IAgentAdapter Adapter,
        string Executable, ProviderIsolation Isolation, StartRunCommand Start, CommandOutcome Started, string Secret)
    {
        public ProviderDispatchRequest Input => Prepared.Input;
        public LedgerEvent StartedEvent => Started.Events[^1];
    }

    private Task JournalLaunchRefusalAsync(
        ProviderDispatchRequest input,
        AgentLaunchMode mode,
        string ledgerRoot,
        long taskVersion,
        GovernanceException refusal) =>
        _refusalJournal.TryAppendUnderTaskLockAsync(
            new TaskWorkspacePathResolver(ledgerRoot).Resolve(input.TaskId),
            new RefusalRecord(
                DateTimeOffset.UtcNow,
                input.ActorId,
                mode == AgentLaunchMode.Resume ? "provider resume" : "provider launch",
                RefusalSite.ProviderLaunch,
                taskVersion,
                refusal.Message,
                RunningKernelIdentity.Current, refusal.Kind.ToString()));

    private static StartRunCommand CreateStartRun(
        ProviderDispatchRequest input,
        string provider,
        string? sessionId,
        string? providerVersion,
        string launchTokenHash,
        IReadOnlyList<ContextSkill>? skillsServedNow) =>
        new(
            input.ActorId, input.Cause, input.Correlation, SafeRunId(input.RunId.Value),
            input.WorkItemId,
            provider, sessionId, input.Model, providerVersion, launchTokenHash,
            input.SubjectActorId, skillsServedNow,
            input.WithoutBriefReason,
            input.StaleBriefEvidence,
            input.CoordinatorSession);

    private static RunId SafeRunId(string value) =>
        value is not ("." or "..") && !value.StartsWith('-') &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            ? new RunId(value)
            : throw new CliUsageException(
                $"Run id '{value}' is not allowed. A run id may contain only letters, digits, '-', '_' " +
                "and '.', may not begin with '-', and may not be '.' or '..'.");

    private static string ResolveLedgerCommandLine(RunId runId)
    {
        var assembly = Path.Combine(AppContext.BaseDirectory, "AILedger.Cli.dll");
        var host = Environment.ProcessPath;
        var invocation = host is null
            ? $"dotnet \"{assembly}\""
            : Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? $"\"{host}\" \"{assembly}\""
                : $"\"{host}\"";
        return $"{invocation} --correlation \"{runId.Value}\"";
    }

    private static async Task<GovernedTaskState> RequireStateAsync(
        IGovernedTaskService service,
        TaskId taskId,
        CancellationToken cancellationToken) =>
        await service.GetStateAsync(taskId, cancellationToken).ConfigureAwait(false)
        ?? throw new CliUsageException($"Task '{taskId}' was not found.");

}
