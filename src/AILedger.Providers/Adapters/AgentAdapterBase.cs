using System.Text;
using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Providers.Process;

namespace AILedger.Providers.Adapters;

public abstract class AgentAdapterBase(IProcessRunner processRunner) : IAgentAdapter
{
    private readonly IProcessRunner _processRunner = processRunner;

    public abstract string Provider { get; }

    protected abstract IReadOnlyList<CapabilityProbe> CapabilityProbes { get; }

    public async Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken)
    {
        var standardOutput = new List<string>();
        var standardError = new List<string>();
        var invocation = new ProcessInvocation(
            executablePath,
            Environment.CurrentDirectory,
            ["--version"],
            string.Empty,
            new Dictionary<string, string>(),
            TimeSpan.FromSeconds(15));

        var exit = await _processRunner.RunAsync(
            invocation,
            (line, _) => AddLineAsync(standardOutput, line),
            (line, _) => AddLineAsync(standardError, line),
            cancellationToken).ConfigureAwait(false);

        var version = standardOutput.Concat(standardError)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
        if (exit.ExitCode != 0 || version is null)
        {
            throw new AgentAdapterException($"{Provider} version probe failed with exit code {exit.ExitCode}.");
        }

        return version.Trim();
    }

    private static void WriteProgress(AgentLaunchRequest request, string eventType)
    {
        try
        {
            Console.Error.WriteLine(
                $"[{DateTimeOffset.UtcNow:HH:mm:ss}] {request.RunId} {request.Provider} {eventType}");
            Console.Error.Flush();
        }
        catch (IOException)
        {
            // A closed or redirected stderr must never fail a governed run.
        }
    }

    public async Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        EnsurePreconditions(request);
        var version = await ProbeVersionAsync(request.ExecutablePath, cancellationToken).ConfigureAwait(false);
        await ProbeCapabilitiesAsync(request, cancellationToken).ConfigureAwait(false);

        var events = new List<ProviderEvent>();
        var errors = new List<string>();
        var parseFailure = default(string);
        var sessionFailure = default(string);
        var sessionId = request.ProviderSessionId;
        var finalOutput = default(string);
        long retainedCharacters = 0;
        var redactor = new SensitiveDataRedactor(request.Environment.Values);
        var arguments = BuildArguments(request, ref sessionId);
        var expectedSessionId = request.Mode == AgentLaunchMode.Resume || RequirePreassignedSessionMatch
            ? sessionId
            : null;
        using var launchScope = OpenLaunchScope(request);
        var environment = new Dictionary<string, string>(request.Environment, StringComparer.Ordinal);
        foreach (var variable in launchScope.Environment)
        {
            environment[variable.Key] = variable.Value;
        }

        var invocation = new ProcessInvocation(
            request.ExecutablePath,
            request.WorkingDirectory,
            arguments,
            ComposeStandardInput(request),
            environment,
            request.Timeout);

        ProcessExit exit;
        try
        {
            exit = await _processRunner.RunAsync(
                invocation,
                (line, _) =>
                {
                    ReserveOutput(line, ref retainedCharacters);
                    try
                    {
                        var providerEvent = ParseEvent(events.Count, line);
                        if (providerEvent.SessionId is not null)
                        {
                            expectedSessionId ??= providerEvent.SessionId;
                            if (!string.Equals(expectedSessionId, providerEvent.SessionId, StringComparison.Ordinal))
                            {
                                sessionFailure ??= "Provider emitted conflicting session identities within one run.";
                            }

                            sessionId ??= providerEvent.SessionId;
                        }

                        var output = ReadFinalOutput(providerEvent);
                        // Stamped where the line is read rather than inside ParseEvent, which is pure
                        // parsing and is exercised by tests that assert on shape.
                        events.Add(providerEvent with
                        {
                            RawJson = redactor.RedactJson(providerEvent.RawJson),
                            RecordedAt = DateTimeOffset.UtcNow
                        });
                        finalOutput = output is null ? finalOutput : redactor.RedactText(output);
                        // Progress, as it happens, on stderr so stdout stays the single JSON result.
                        // Without this a launch is silent until it ends, and an agent that is working
                        // but not recording in the ledger is indistinguishable from one that is hung —
                        // run RP1 exited zero having done nothing and only its final log said so.
                        // The event type is provider vocabulary and carries no task content; the raw
                        // JSON is not echoed, because it is the thing the redactor exists to guard.
                        WriteProgress(request, providerEvent.Type);
                    }
                    catch (JsonException exception)
                    {
                        parseFailure = exception.Message;
                    }

                    return ValueTask.CompletedTask;
                },
                (line, _) =>
                {
                    ReserveOutput(line, ref retainedCharacters);
                    return AddLineAsync(errors, redactor.RedactText(line));
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            return CreateFailure(
                request, sessionId, version, arguments, now, now, -1, finalOutput, events, errors,
                AgentRunStatus.Cancelled, "Provider run was cancelled.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            return CreateFailure(
                request, sessionId, version, arguments, now, now, -1, finalOutput, events, errors,
                AgentRunStatus.Cancelled, "Provider run timed out.");
        }
        catch (InvalidDataException exception)
        {
            var now = DateTimeOffset.UtcNow;
            return CreateFailure(
                request, sessionId, version, arguments, now, now, -1, finalOutput, events, errors,
                AgentRunStatus.ProtocolError, exception.Message);
        }

        var failure = DetermineFailure(expectedSessionId, sessionId, exit.ExitCode, events, parseFailure, sessionFailure);
        var status = failure is null ? AgentRunStatus.Completed : AgentRunStatus.ProtocolError;
        if (exit.ExitCode != 0 || events.Any(item => item.IsError))
        {
            status = AgentRunStatus.Failed;
        }

        return new AgentRunResult(
            request.RunId,
            Provider,
            sessionId,
            status,
            exit.StartedAt,
            exit.EndedAt,
            exit.ExitCode,
            finalOutput,
            events.ToArray(),
            string.Join(Environment.NewLine, errors),
            version,
            arguments,
            request.Mode == AgentLaunchMode.Resume,
            failure);
    }

    protected abstract IReadOnlyList<string> BuildArguments(AgentLaunchRequest request, ref string? sessionId);
    protected abstract ProviderEvent ParseEvent(long sequence, string json);
    protected abstract string? ReadFinalOutput(ProviderEvent providerEvent);
    protected virtual bool RequirePreassignedSessionMatch => false;

    /// <summary>Claude carries the briefing in its prompt argument; Codex prepends it to stdin.</summary>
    protected virtual string ComposeStandardInput(AgentLaunchRequest request) => request.StandardInput;

    /// <summary>Provider-specific launch preconditions, checked before any process is spawned.</summary>
    protected virtual void EnsurePreconditions(AgentLaunchRequest request)
    {
    }

    /// <summary>
    /// Per-launch isolation from the operator's own installed configuration. Ledger serves the
    /// role's skills through the context manifest, so a governed agent must not also inherit the
    /// ambient copies installed for interactive use — they are a second, silently diverging source
    /// of the same rules.
    /// </summary>
    protected virtual ProviderLaunchScope OpenLaunchScope(AgentLaunchRequest request) =>
        ProviderLaunchScope.None;

    protected sealed record CapabilityProbe(IReadOnlyList<string> Arguments, IReadOnlyList<string> RequiredTokens);

    protected virtual string? ValidateSession(string? expectedSessionId, string? observedSessionId)
    {
        if (string.IsNullOrWhiteSpace(observedSessionId))
        {
            return "Provider did not emit a session identity.";
        }

        if (expectedSessionId is not null &&
            !string.Equals(expectedSessionId, observedSessionId, StringComparison.Ordinal))
        {
            return "Provider emitted a session identity that does not match the requested resume session.";
        }

        return null;
    }

    private async Task ProbeCapabilitiesAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
    {
        foreach (var probe in CapabilityProbes)
        {
            var standardOutput = new StringBuilder();
            var standardError = new StringBuilder();
            var invocation = new ProcessInvocation(
                request.ExecutablePath,
                request.WorkingDirectory,
                probe.Arguments,
                string.Empty,
                request.Environment,
                TimeSpan.FromSeconds(15));

            var exit = await _processRunner.RunAsync(
                invocation,
                (line, _) => AppendLineAsync(standardOutput, line),
                (line, _) => AppendLineAsync(standardError, line),
                cancellationToken).ConfigureAwait(false);

            var help = standardOutput.Append(standardError).ToString();
            var missing = probe.RequiredTokens.Where(token => !help.Contains(token, StringComparison.Ordinal)).ToArray();
            if (exit.ExitCode != 0 || missing.Length > 0)
            {
                throw new AgentAdapterException(
                    $"{Provider} CLI capability probe '{string.Join(" ", probe.Arguments)}' failed or is missing: {string.Join(", ", missing)}.");
            }
        }
    }

    private string? DetermineFailure(
        string? expectedSessionId,
        string? sessionId,
        int exitCode,
        IReadOnlyList<ProviderEvent> events,
        string? parseFailure,
        string? sessionFailure)
    {
        if (parseFailure is not null)
        {
            return $"Malformed provider JSONL: {parseFailure}";
        }

        if (sessionFailure is not null)
        {
            return sessionFailure;
        }

        if (exitCode != 0)
        {
            return $"Provider exited with code {exitCode}.";
        }

        var failedEvent = events.FirstOrDefault(item => item.IsError);
        if (failedEvent is not null)
        {
            return $"Provider emitted failed event '{failedEvent.Type}'.";
        }

        var terminalEvents = events.Where(item => item.IsTerminal).ToArray();
        if (terminalEvents.Length == 0)
        {
            return "Provider stream ended without a terminal event.";
        }

        if (terminalEvents.Length != 1)
        {
            return $"Provider stream must contain exactly one terminal event; observed {terminalEvents.Length}.";
        }

        if (!ReferenceEquals(terminalEvents[0], events[^1]))
        {
            return "Provider terminal event must be the final stream event.";
        }

        return ValidateSession(expectedSessionId, sessionId);
    }

    private static AgentRunResult CreateFailure(
        AgentLaunchRequest request,
        string? sessionId,
        string version,
        IReadOnlyList<string> arguments,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        int exitCode,
        string? finalOutput,
        IReadOnlyList<ProviderEvent> events,
        IReadOnlyList<string> errors,
        AgentRunStatus status,
        string failure) =>
        new(
            request.RunId,
            request.Provider,
            sessionId,
            status,
            startedAt,
            endedAt,
            exitCode,
            finalOutput,
            events.ToArray(),
            string.Join(Environment.NewLine, errors),
            version,
            arguments,
            request.Mode == AgentLaunchMode.Resume,
            failure);

    private void ValidateRequest(AgentLaunchRequest request)
    {
        if (!string.Equals(request.Provider, Provider, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Request provider '{request.Provider}' does not match adapter '{Provider}'.", nameof(request));
        }

        if (!Path.IsPathFullyQualified(request.ExecutablePath) || !Path.IsPathFullyQualified(request.WorkingDirectory))
        {
            throw new ArgumentException("Executable and working-directory paths must be absolute.", nameof(request));
        }

        if (request.Mode == AgentLaunchMode.Resume && string.IsNullOrWhiteSpace(request.ProviderSessionId))
        {
            throw new ArgumentException("Resume requires an exact provider session identity.", nameof(request));
        }

        if (request.PermissionProfile != PermissionProfile.WorkspaceGoverned)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Unsupported permission profile.");
        }
    }

    private static ValueTask AddLineAsync(ICollection<string> target, string line)
    {
        target.Add(line);
        return ValueTask.CompletedTask;
    }

    private static ValueTask AppendLineAsync(StringBuilder target, string line)
    {
        target.AppendLine(line);
        return ValueTask.CompletedTask;
    }

    private static void ReserveOutput(string line, ref long retainedCharacters)
    {
        if (line.Length > ProviderOutputLimits.MaximumCharactersPerLine ||
            Interlocked.Add(ref retainedCharacters, line.Length + 1L) > ProviderOutputLimits.MaximumRetainedCharacters)
        {
            throw new InvalidDataException("Provider output exceeded the retained-output limit.");
        }
    }
}
