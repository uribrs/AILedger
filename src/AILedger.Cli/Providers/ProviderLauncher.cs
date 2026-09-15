using System.Text.Json;
using AILedger.Cli.ContextBriefing;
using AILedger.Cli.Routing;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Providers.Adapters;
using AILedger.Providers.Process;
using AILedger.Storage;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Providers;

internal sealed class ProviderLauncher(
    Func<string, IAgentAdapter> _adapterFactory,
    ContextBriefingCliCommands _contextCommands,
    CliCommandExecutor executor,
    JsonSerializerOptions _json,
    RefusalJournal _refusalJournal,
    ProviderRunRecorder _runRecorder)
{
    public async Task LaunchAsync(
        IGovernedTaskService service,
        CommandLine input,
        AgentLaunchMode mode,
        string ledgerRoot,
        CancellationToken cancellationToken)
    {
        var provider = input.Required("provider").ToLowerInvariant();
        var sessionId = input.Optional("session");
        if (mode == AgentLaunchMode.Resume && string.IsNullOrWhiteSpace(sessionId))
        {
            throw new CliUsageException("Provider resume requires '--session' with the exact provider session ID.");
        }

        var launchState = await RequireStateAsync(service, Task(input), cancellationToken).ConfigureAwait(false);
        var requestedWorkItem = OptionalId(input.Optional("work"), value => new WorkItemId(value));
        // Read once, here, and used for both the pre-flight refusal and the command below. Reading
        // it twice would leave a window where the layer changed between the check and the command.
        var servedNow = await _contextCommands.CurrentSkillsAsync(launchState, input, cancellationToken)
            .ConfigureAwait(false);
        // Authority and scope are settled before an adapter is resolved or any process spawned: a
        // request that will be refused should cost neither.
        ProviderGrants grants;
        try
        {
            grants = ProviderGrantResolver.Resolve(
                launchState, Actor(input), requestedWorkItem, input, ledgerRoot);
            // And the brief and the dispatch rules, on the same grounds and in the order the kernel
            // states: authority first, then the brief, then anything with a side effect. These ran
            // only inside the command below, which the version probe already precedes, so an actor
            // with no brief had executed a provider binary before being refused (VC3). The kernel
            // still refuses the launch — this only moves the refusal in front of the process.
            //
            // The work item is passed because the refusals keyed on it — a coordinating role cannot
            // hold a run that names one — are part of the same set. Omitting it left that rule
            // seeing no item, so it did not fire here and the launch paid for a version probe before
            // the command refused it.
            ProviderLaunchPreflight.EnsurePermitted(
                launchState, Actor(input), Subject(input), servedNow,
                input.Optional("without-brief"),
                OptionalId(input.Optional("with-stale-brief"), value => new EvidenceId(value)),
                requestedWorkItem);
        }
        catch (GovernanceException refusal)
        {
            // The seven authority-and-scope refusals decided here submit no command and start no
            // run, so the journal's service site never sees them and a journal without this one
            // reads as complete while missing the refusals a retrospective values most (C11). Three
            // of the operator's own saved lessons were learned by hitting this method.
            await JournalLaunchRefusalAsync(
                input, mode, ledgerRoot, launchState.Version, refusal).ConfigureAwait(false);
            throw;
        }

        var adapter = _adapterFactory(provider);
        var executable = ExecutableResolver.Resolve(provider, input.Optional("executable"));
        // Probed before the run is recorded, so the ledger knows which cognition ran even if the
        // launch later fails. What ran should never be known only in memory.
        var providerVersion = await adapter.ProbeVersionAsync(executable, cancellationToken).ConfigureAwait(false);
        // Held only here, for this run's lifetime. Only its hash is recorded, and it never reaches
        // the manifest, the briefing or the child's environment.
        var launchToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var start = CreateStartRun(input, provider, sessionId, providerVersion,
            CommandHandler.HashLaunchToken(launchToken), servedNow);
        var started = await service.ExecuteAsync(Task(input), start, cancellationToken).ConfigureAwait(false);
        var startedEventId = started.Events[^1].EventId;
        // The instant the kernel recorded the run, which is what the first-write time is measured
        // from. Read off the same event as the id above, not from a clock here and not by looking
        // the run up: the envelope's timestamp and the run's StartedAt are the one 'now' the command
        // was handled with, so both ends of the interval come from the same source and this line
        // cannot fail where the line above it succeeded.
        var runStartedAt = started.Events[^1].RecordedAt;
        // Set only once the manifest exists. A launch that fails before briefing leaves both null,
        // which is what makes "this run was never briefed" distinguishable from "briefed with
        // nothing". They are recorded at completion because the manifest is built after run.start.
        string? manifestHash = null;
        int? manifestArtifactCount = null;
        // The limit this launch was given, recorded on the run so that a run which ended at its limit
        // can be told from one that failed on its own merits (K16, measure 11). Declared out here and
        // assigned inside the try, so the value the request is built with and the value the record
        // carries are the same one — and so a launch whose --timeout-seconds could not be parsed
        // records no limit, which is the truth: none was given.
        int? launchTimeoutSeconds = null;
        AgentRunResult result;
        try
        {
            // The manifest is filtered by the subject's role, not the dispatcher's. An operator who
            // dispatches a code reviewer must not hand it an operator's view of the task.
            var manifest = await _contextCommands.CreateAsync(
                service, input, SubjectOrActor(input), cancellationToken).ConfigureAwait(false);
            // Over the exact bytes handed to the child, not a re-serialisation of the manifest: the
            // hash names the brief one run received, so a manifest kept outside the ledger can be
            // matched to the run that read it. It is not a comparison between two runs — the
            // manifest carries the task version and the assembly time, so two launches never hash
            // alike, and normalising either one would buy a comparison the version already denies.
            var manifestJson = JsonSerializer.Serialize(manifest, _json);
            // Parsed before the request rather than inside its constructor, because the record and
            // the request must carry one number and not two readings of one option.
            launchTimeoutSeconds = PositiveInt(input.Optional("timeout-seconds"), 1800);
            var request = new AgentLaunchRequest(
                start.RunId, Task(input), SubjectOrActor(input), start.WorkItemId, mode, provider,
                executable,
                grants.WorkingDirectory, ledgerRoot, ResolveLedgerCommandLine(start.RunId),
                manifestJson, sessionId, PermissionProfile.WorkspaceGoverned,
                input.Optional("model"), input.Optional("output-schema"),
                grants.AdditionalDirectories,
                new Dictionary<string, string>(),
                TimeSpan.FromSeconds(launchTimeoutSeconds.Value));
            // Marked delivered only once the request is fully built, because building it is fallible
            // — the timeout argument is parsed on the line above and throws on a bad value.
            // Assigning earlier recorded a brief for a run the adapter never received, which is the
            // opposite of what the absence is meant to mean. Found by RV1 as VC1/VCH1.
            manifestHash = _runRecorder.HashManifest(manifestJson);
            manifestArtifactCount = manifest.Artifacts.Count;
            result = await adapter.RunAsync(request, cancellationToken).ConfigureAwait(false);
            // The result says which run it belongs to, and everything read off it below is attributed
            // to the run this process started: the cost and the served model are recorded on
            // start.RunId, while the sidecar is named after the result's. The production adapters echo
            // the request's id (AgentAdapterBase:169) and this launcher must not assume it — a result
            // naming another run would write that run's stream under its name, overwriting a genuine
            // one, and close this run from a stream that is not its own (RC5).
            //
            // Refused here rather than repaired downstream, because there is no correct way to split
            // one result between two runs. Thrown inside this try so the catch below closes the run as
            // failed, which is what every other adapter fault does: nothing from the foreign result
            // reaches the record and no file is written.
            if (result.RunId != start.RunId)
            {
                throw new AgentAdapterException(
                    $"Provider '{provider}' returned a result for run '{result.RunId}' while run " +
                    $"'{start.RunId}' was launched. The provider result was not kept and the run was " +
                    "closed as failed.");
            }
        }
        catch (Exception launchException)
        {
            // Recorded before the cleanup, because the cleanup can fail and the refusal happened
            // either way. Only a governance refusal is journalled: --timeout-seconds is parsed
            // inside the request construction above, so a CliUsageException arrives here too, and a
            // malformed command line is a typing mistake rather than a gate that fired.
            if (launchException is GovernanceException refusal)
            {
                await JournalLaunchRefusalAsync(
                    input, mode, ledgerRoot, started.State.Version, refusal).ConfigureAwait(false);
            }

            try
            {
                var status = launchException is OperationCanceledException
                    ? AgentRunStatus.Cancelled
                    : AgentRunStatus.Failed;
                // No AgentRunResult exists on this path — the adapter threw rather than returning
                // one — so there is no stream to read a cost off and no result to keep beside the
                // log. The one measurement that survives is the first-write time: a child can reach
                // the ledger and then die, and a launch that failed after the agent worked is
                // exactly the run worth telling apart from one that failed before it started.
                await _runRecorder.CompleteAsync(
                    service, input, start.RunId, sessionId, status, startedEventId, launchToken,
                    RunCost.Unmeasured,
                    await _runRecorder.MeasureFirstLedgerWriteAsync(
                        service, input, start.RunId, runStartedAt).ConfigureAwait(false),
                    servedModel: null,
                    // No result means no drain count either, so this stays absent rather than zero:
                    // this stream was never watched to a finish.
                    truncatedLines: null,
                    // Absent when the launch died before the request was built, which includes a
                    // --timeout-seconds this process could not parse. Null then says no limit was
                    // given, and that is the truth rather than a default standing in for one.
                    launchTimeoutSeconds,
                    // The adapter returned nothing on this path — it threw — so the provider stated
                    // no reason and there is none to relay. What ended the run is this process's own
                    // exception, and that is what is recorded, because a run that failed before its
                    // provider spoke is exactly the one a reader cannot otherwise account for.
                    launchException.Message,
                    // Nobody observed which condition ended this run. The observation exists only on
                    // an AgentRunResult and there is none on this path, so this stays absent rather
                    // than becoming false: false is the launcher stating it watched the run end some
                    // other way, and here it watched nothing. Measure 11 leaves the row unjudged,
                    // which is the correct answer and not a degraded one (VC4, VC6).
                    endedAtTheLaunchTimeout: null,
                    manifestHash, manifestArtifactCount).ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw new IOException(
                    "Provider launch failed and its active run could not be closed.",
                    new AggregateException(launchException, cleanupException));
            }

            throw;
        }

        // Kept before the run is closed, so the stream survives a completion that strands: the whole
        // reason this file exists is that the launcher held the stream and dropped it (C3, E4, D6).
        await _runRecorder.WriteResultAsync(ledgerRoot, Task(input), result).ConfigureAwait(false);
        // What the run cost, read off the stream the provider already emitted. Read here and passed
        // through untouched: RunCostReader's accepted set is a subset of what the completion rule
        // accepts, and the arithmetic that could break that lives inside the reader (C15).
        var cost = RunCostReader.Read(result.Events);
        var servedModel = _runRecorder.ReadServedModel(result.Events);
        var firstLedgerWrite = await _runRecorder.MeasureFirstLedgerWriteAsync(
            service, input, start.RunId, runStartedAt).ConfigureAwait(false);

        var requiredOutputKind = launchState.Roles[SubjectOrActor(input)].Role switch
        {
            RoleKind.Verifier => GovernedArtifactKind.VerifierOutput,
            RoleKind.CodeReviewer => GovernedArtifactKind.CodeReviewOutput,
            _ => (GovernedArtifactKind?)null
        };
        GovernedArtifactKind? missingOutputKind = null;
        Exception? completionFailure = null;
        try
        {
            await _runRecorder.CompleteAsync(
                service, input, start.RunId, result.ProviderSessionId, result.Status, startedEventId, launchToken,
                cost, firstLedgerWrite, servedModel, result.TruncatedLines,
                launchTimeoutSeconds,
                // The adapter's own Failure string, relayed unaltered and read off the same result
                // object the sidecar beside this run is written from. So what the run record says
                // about why the run ended and what its sidecar says are one statement rather than
                // two that can disagree — which is the disagreement C8 measured on run LR4.
                result.Failure,
                // What ended this run, relayed from the same result the reason above is read off.
                // The process runner sets it by observing which cancellation source fired at the
                // moment it ended the process, so this is the one input measure 11 attributes a
                // timeout on — and the reason it never compares durations again (VC6, WC2).
                result.EndedAtTheLaunchTimeout,
                manifestHash, manifestArtifactCount).ConfigureAwait(false);
        }
        catch (GovernanceException exception) when (
            result.Status == AgentRunStatus.Completed &&
            requiredOutputKind is { } kind &&
            exception.Message.Contains($"requires its matching '{kind}' artifact.", StringComparison.Ordinal))
        {
            missingOutputKind = kind;
            try
            {
                // The same cost, on the run that is being closed as failed instead: what the run
                // spent is what it spent, and a run refused for filing no output is one whose cost
                // a retrospective most wants to see.
                await _runRecorder.CompleteAsync(
                    service, input, start.RunId, result.ProviderSessionId, AgentRunStatus.Failed,
                    startedEventId, launchToken, cost, firstLedgerWrite, servedModel,
                    result.TruncatedLines,
                    launchTimeoutSeconds,
                    // Still the provider's own reason, which on this path is normally none: the
                    // status here is the launcher's decision that a run filing no required output is
                    // a failure, and the provider completed. Composing a reason from that decision
                    // would put a provider failure on a run whose provider reported success, and
                    // C8's own example is a run recorded as failed while its sidecar said completed
                    // with no failure at all. Measure 11 therefore does not read this run as a failed
                    // dispatch, and the missing artifact is the artifact rule's finding, not a
                    // dispatch the coordinator got wrong.
                    result.Failure,
                    // The adapter's observation, unchanged by the launcher's own reclassification.
                    // What ended the provider is a fact the runner watched; that this run is being
                    // closed as failed for filing no artifact is a separate decision taken here, and
                    // overwriting the observation with it would report a termination nobody saw.
                    result.EndedAtTheLaunchTimeout,
                    manifestHash, manifestArtifactCount).ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                completionFailure = new AggregateException(exception, cleanupException);
            }
        }
        catch (Exception exception)
        {
            completionFailure = exception;
        }

        await executor.WriteJsonAsync(result).ConfigureAwait(false);
        if (completionFailure is not null)
        {
            throw new IOException(
                $"Provider run '{result.RunId}' returned a terminal result, but its Ledger run could not be closed. " +
                "The provider result was written to standard output for recovery.",
                completionFailure);
        }

        if (missingOutputKind is { } requiredKind)
        {
            throw new ProviderRunFailedException(
                $"Provider run '{result.RunId}' did not record its required '{requiredKind}' artifact; " +
                "its Ledger run was closed as 'Failed'.");
        }

        if (result.Status == AgentRunStatus.Cancelled && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (result.Status != AgentRunStatus.Completed)
        {
            throw new ProviderRunFailedException(
                $"Provider run '{result.RunId}' ended with status '{result.Status}'.");
        }
    }

    // The launch site holds no mutation lease, so it appends through the journal's locking method:
    // two unsynchronised processes lose or truncate rows at every size measured. That method bounds
    // its own wait and swallows its own failures, which is why there is no try/catch and no timeout
    // here — on a refusal path the operator is already waiting to be told what was refused, and a
    // telemetry row must neither delay that message nor replace it with an I/O error (R18).
    private Task JournalLaunchRefusalAsync(
        CommandLine input,
        AgentLaunchMode mode,
        string ledgerRoot,
        long taskVersion,
        GovernanceException refusal) =>
        _refusalJournal.TryAppendUnderTaskLockAsync(
            new TaskWorkspacePathResolver(ledgerRoot).Resolve(Task(input)),
            new RefusalRecord(
                DateTimeOffset.UtcNow,
                // Who was refused, which is the actor that issued the launch — not the subject it
                // would have run for. The service site records the same thing.
                Actor(input),
                mode == AgentLaunchMode.Resume ? "provider resume" : "provider launch",
                RefusalSite.ProviderLaunch,
                taskVersion,
                // The gate's own text, verbatim. A category derived from it would be a second,
                // looser model of what the gate requires (C5).
                refusal.Message));

    // The cost is a required parameter rather than an optional one, because the defect this item
    // exists to fix is six fields that shipped with nothing populating them (IC4). A new closing
    // path that has no cost to record has to say so as RunCost.Unmeasured, which is a decision a
    // reader can see, rather than by leaving an argument off.
    private static StartRunCommand CreateStartRun(
        CommandLine input,
        string provider,
        string? sessionId,
        string? providerVersion,
        string launchTokenHash,
        IReadOnlyList<ContextSkill>? skillsServedNow) =>
        new(
            Actor(input), Cause(input), Correlation(input), SafeRunId(input.Required("run")),
            OptionalId(input.Optional("work"), value => new WorkItemId(value)),
            provider, sessionId, input.Optional("model"), providerVersion, launchTokenHash,
            Subject(input), skillsServedNow,
            input.Optional("without-brief"),
            OptionalId(input.Optional("with-stale-brief"), value => new EvidenceId(value)),
            OptionalId(input.Optional("coordinator-session"), value => new CoordinatorSessionId(value)));

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

    private static int PositiveInt(string? value, int fallback) =>
        value is null ? fallback : int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new CliUsageException("Timeout must be a positive integer.");

    private static async Task<GovernedTaskState> RequireStateAsync(
        IGovernedTaskService service,
        TaskId taskId,
        CancellationToken cancellationToken) =>
        await service.GetStateAsync(taskId, cancellationToken).ConfigureAwait(false)
        ?? throw new CliUsageException($"Task '{taskId}' was not found.");

}

internal sealed class ProviderRunFailedException(string message) : Exception(message);
