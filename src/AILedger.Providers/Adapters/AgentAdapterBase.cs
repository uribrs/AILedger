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
            // A version probe writes one short line and its count would have no reader.
            null,
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

        // Read by the failure paths below, which never receive a ProcessExit. The drain increments
        // it as it cuts, so a run refused part-way through still records how much had been cut when
        // it was refused (CC2).
        var tally = new TruncatedLineTally();
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
                tally,
                cancellationToken).ConfigureAwait(false);
        }
        // The runner's own expired deadline, said so by the runner. This is the one result that
        // carries EndedAtTheLaunchTimeout true, and it is the whole reason the type exists: the fact
        // is observable here and nowhere later, and the elapsed-time comparison that used to stand in
        // for it was unsound (VC6, VE10). It is caught ahead of the two filters below because it
        // derives from OperationCanceledException and would otherwise be swallowed by them.
        catch (ProviderProcessTimeoutException)
        {
            var now = DateTimeOffset.UtcNow;
            return CreateFailure(
                request, sessionId, version, arguments, now, now, -1, finalOutput, events, errors,
                AgentRunStatus.Cancelled, "Provider run timed out.", tally.Observed,
                endedAtTheLaunchTimeout: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            return CreateFailure(
                request, sessionId, version, arguments, now, now, -1, finalOutput, events, errors,
                AgentRunStatus.Cancelled, "Provider run was cancelled.", tally.Observed);
        }
        // A cancellation this adapter did not ask for, from a runner that did not name its own
        // deadline. The wording is kept because a timeout is by far its likeliest cause and a reader
        // is better served by the likely account than by none — but the flag stays false, because a
        // likely account is not an observation and this measure is the one place that distinction
        // has to hold. A runner that means "timeout" says so with the type above.
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            return CreateFailure(
                request, sessionId, version, arguments, now, now, -1, finalOutput, events, errors,
                AgentRunStatus.Cancelled, "Provider run timed out.", tally.Observed);
        }
        catch (InvalidDataException exception)
        {
            var now = DateTimeOffset.UtcNow;
            return CreateFailure(
                request, sessionId, version, arguments, now, now, -1, finalOutput, events, errors,
                AgentRunStatus.ProtocolError, exception.Message, tally.Observed);
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
            failure,
            // Whatever the drain had to cut, as the drain finally counted it. The three catch blocks
            // above never receive an exit, so they read the same count off the tally instead, where
            // it is null until something is cut — "nobody counted", never "nothing was cut".
            exit.TruncatedLines);
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
                // A capability probe reads a help screen and its count would have no reader.
                null,
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
        string failure,
        // What the drains had cut by the time the run was refused, or null if they had cut nothing.
        // A ProtocolError that says how much was cut before it died is the difference between a
        // diagnosable failure and the four runs this repository cannot explain (CC2, C3).
        int? truncatedLines,
        // Set only on the path that caught the runner's own timeout. Every other failure path leaves
        // it false, which states that this adapter watched the run end some other way.
        bool endedAtTheLaunchTimeout = false) =>
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
            failure,
            truncatedLines,
            endedAtTheLaunchTimeout);

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
