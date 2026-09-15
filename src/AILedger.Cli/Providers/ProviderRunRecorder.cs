using System.Text.Json;
using AILedger.Cli.Routing;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
using AILedger.Storage;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Providers;

internal sealed class ProviderRunRecorder(
    TextWriter _error,
    JsonSerializerOptions _json)
{
    private const int TerminalPersistenceAttempts = 3;
    private static readonly TimeSpan TerminalPersistenceDeadline = TimeSpan.FromSeconds(65);
    private static readonly TimeSpan TerminalPersistenceRetryDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan FirstLedgerWriteReadDeadline = TimeSpan.FromSeconds(15);

    public async Task CompleteAsync(
        IGovernedTaskService service,
        CommandLine input,
        RunId runId,
        string? sessionId,
        AgentRunStatus status,
        EventId causationId,
        string launchToken,
        RunCost cost,
        long? millisecondsToFirstLedgerWrite,
        string? servedModel,
        int? truncatedLines,
        // Required for the same reason cost is: the two fields measures 11 and 12 read shipped with
        // nothing populating their predecessors once already, and a closing path that has neither has
        // to say so by passing null rather than by leaving an argument off.
        //
        // The reason is the provider's own, relayed by the caller and never derived here. This method
        // is given a status by three different callers and one of them decides that status itself, so
        // composing a reason from the status would manufacture a provider failure the provider never
        // reported — which is exactly what C8 refuses.
        int? launchTimeoutSeconds,
        string? terminalFailureReason,
        // Which condition ended the run, as the adapter's result reports it. Required for the same
        // reason the two above are: the field shipped on AgentRunResult and on CompleteRunCommand
        // with nothing carrying it between them, so every real launch recorded null and measure 11
        // left timeouts the runner had actually observed unjudged (WC2, WE20). A closing path with no
        // result has to say so by passing null rather than by leaving an argument off.
        bool? endedAtTheLaunchTimeout,
        string? manifestHash = null,
        int? manifestArtifactCount = null)
    {
        using var completion = new CancellationTokenSource(TerminalPersistenceDeadline);
        var command = new CompleteRunCommand(
            Actor(input), causationId, Correlation(input), runId, status, sessionId, launchToken,
            manifestHash, manifestArtifactCount,
            cost.Turns, cost.OutputTokens, millisecondsToFirstLedgerWrite, servedModel,
            cost.TokensInUncached, cost.TokensInCacheWrite, cost.TokensInCacheRead,
            truncatedLines, launchTimeoutSeconds, terminalFailureReason, endedAtTheLaunchTimeout);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await service.ExecuteAsync(Task(input), command, completion.Token).ConfigureAwait(false);
                return;
            }
            catch (IOException) when (attempt < TerminalPersistenceAttempts && !completion.IsCancellationRequested)
            {
                await System.Threading.Tasks.Task.Delay(
                    TerminalPersistenceRetryDelay, completion.Token).ConfigureAwait(false);
            }
        }
    }

    // How long the child took to reach the ledger — the one cost dimension no provider reports, and
    // the one that tells a run which burned tokens and recorded nothing from a run which did the
    // work. Measured from the instant the kernel recorded the run to the earliest event the child
    // wrote.
    //
    // The child's writes are the events carrying the run id as their correlation id, which the
    // launcher put on the command line the briefing hands over. The child has nothing to understand
    // and nothing to opt into: it copies that command line, and one that rewrites it falls back to a
    // fresh id per invocation, so this measurement goes absent rather than wrong.
    public async Task<long?> MeasureFirstLedgerWriteAsync(
        IGovernedTaskService service,
        CommandLine input,
        RunId runId,
        DateTimeOffset runStartedAt)
    {
        // The launcher's own events would carry the same correlation as the child's, and its
        // run.started is always the earlier of the two. The field would then report how fast this
        // process wrote its own record, which measures the coordinator and not the agent (ALT5).
        if (string.Equals(Correlation(input), runId.Value, StringComparison.Ordinal))
        {
            return null;
        }

        // Its own deadline on its own token, never the launch's. A cancelled launch arrives here
        // with a cancelled token, and a run must never stay active because its telemetry could not
        // be read — the completion beside it takes the same precaution for the same reason.
        using var read = new CancellationTokenSource(FirstLedgerWriteReadDeadline);
        try
        {
            DateTimeOffset? firstWrite = null;
            await foreach (var @event in service.GetHistoryAsync(Task(input), read.Token).ConfigureAwait(false))
            {
                // Only what was written after the run was recorded. A correlation id is whatever its
                // caller passed and has no uniqueness relation to a run id, so an earlier command can
                // already carry this one — the id is composed by the operator before the run exists.
                // Such an event is not this run's write, and taking it as the earliest made the
                // interval negative, which the floor below then recorded as no first write at all:
                // a run that did reach the ledger, filed as one that never did (RC6).
                if (@event.RecordedAt >= runStartedAt &&
                    string.Equals(@event.CorrelationId, runId.Value, StringComparison.Ordinal) &&
                    (firstWrite is null || @event.RecordedAt < firstWrite))
                {
                    firstWrite = @event.RecordedAt;
                }
            }

            if (firstWrite is not { } reached)
            {
                return null;
            }

            // Floored at nothing rather than passed on negative. This method is called inside the
            // path that closes the run, and the completion rule refuses a negative interval, so a
            // value it refuses would abort completion and strand a finished run as active. That is
            // the class C15 records — found four times, and new arithmetic here is where a fifth
            // would come from. An interval that comes back negative describes no run this kernel can
            // measure, so it records nothing. Kept as the second line of defence now that the
            // selection above excludes the events that produced a negative interval (RC6): the floor
            // answers a clock this process does not own, the exclusion answers the correlation.
            var elapsed = (reached - runStartedAt).TotalMilliseconds;
            return elapsed < 0 ? null : (long?)Math.Round(elapsed);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or JsonException or OperationCanceledException)
        {
            // A log this process cannot read, or cannot finish reading inside the deadline, measured
            // nothing. Named types rather than a widened catch: a real fault must still surface,
            // which is the finding RC1 and RE1 left on the reader (C15).
            return null;
        }
    }

    // Which cognition actually served, when the stream says so. Claude states it on its terminal
    // event as the keys of 'modelUsage', and states no scalar model there — the provider's own
    // schema documents each entry's canonical id as possibly differing from "the raw model string
    // this entry is keyed by" (IC14, IE21, IE22). Codex states no model anywhere in the exec stream
    // the adapter reads: its TurnCompletedEvent carries 'usage' and nothing else (IC13, IE20). So
    // one property name covers both providers with no provider branch, and codex is answered by the
    // absence rather than by a guess taken from the request (K14).
    //
    // Exactly one key, or nothing. A run two models served has no single served model, and the field
    // would otherwise name whichever key enumerated first. The whole map is in the sidecar either
    // way, so declining here loses nothing.
    public string? ReadServedModel(IReadOnlyList<ProviderEvent> events)
    {
        if (events.LastOrDefault(providerEvent => providerEvent.IsTerminal) is not { } terminal)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(terminal.RawJson);
            // A terminal event is a JSON object only by the provider's convention, and
            // TryGetProperty throws on anything else. RC1 was this exact throw reaching a
            // completion path from the cost read beside this one.
            if (document.RootElement is not { ValueKind: JsonValueKind.Object } root ||
                !root.TryGetProperty("modelUsage", out var served) ||
                served.ValueKind is not JsonValueKind.Object)
            {
                return null;
            }

            var models = served.EnumerateObject().Select(property => property.Name).Take(2).ToArray();
            return models.Length == 1 && !string.IsNullOrWhiteSpace(models[0]) ? models[0] : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The provider's whole result, kept beside the event log instead of only written to standard
    // output and dropped — which is what the launcher did with it, holding the entire stream in hand
    // (C3, E4, D6). Not in the event log itself: replay byte-compares what it reads and would then
    // have to accept every shape any provider version ever emitted (ALT2). Nothing replays this file.
    //
    // Best effort, and said out loud. The run has already ended and its stream also went to standard
    // output, so failing to keep a copy must not turn a finished run into a failed launch; but the
    // operator is told which path could not be written rather than left to discover the gap.
    public async Task WriteResultAsync(string ledgerRoot, TaskId taskId, AgentRunResult result)
    {
        if (!IsSafeFileName(result.RunId.Value))
        {
            await _error.WriteLineAsync(
                $"Run '{result.RunId}' has an identifier that is not a safe file name; " +
                "its provider result was not kept beside the log.").ConfigureAwait(false);
            return;
        }

        var path = Path.Combine(
            new TaskWorkspacePathResolver(ledgerRoot).Resolve(taskId), "runs", result.RunId.Value + ".json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // CancellationToken.None deliberately, as the completion beside it is: a launch that was
            // cancelled is exactly the run whose stream is worth reading, and passing the launch's
            // token here would drop it precisely then.
            await File.WriteAllTextAsync(
                path, JsonSerializer.Serialize(result, _json), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await _error.WriteLineAsync(
                $"Provider result for run '{result.RunId}' could not be written to '{path}': {exception.Message}")
                .ConfigureAwait(false);
        }
    }

    // A run id becomes a path segment here. No launch can reach this check any more: Run(input)
    // refused an unsafe '--run' before the run was opened, and the caller above now refuses a result
    // whose run id is not the one it started, so the value is always that same checked id. It stays
    // because the write must not depend on its caller having checked — the run id arrives on a result
    // from an injected IAgentAdapter, and coupling a filesystem write to a guard in another method is
    // how the traversal comes back. TaskWorkspacePathResolver guards the task id for the same reason.
    private static bool IsSafeFileName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value is not ("." or "..") &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !value.Contains(Path.DirectorySeparatorChar) &&
        !value.Contains(Path.AltDirectorySeparatorChar);

    public string HashManifest(string manifestJson) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(manifestJson))).ToLowerInvariant();



}

