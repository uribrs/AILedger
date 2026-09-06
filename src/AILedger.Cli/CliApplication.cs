using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Providers.Adapters;
using AILedger.Providers.Process;
using AILedger.Storage;

namespace AILedger.Cli;

public sealed class CliApplication
{
    private const int TerminalPersistenceAttempts = 3;
    private static readonly TimeSpan TerminalPersistenceDeadline = TimeSpan.FromSeconds(65);
    private static readonly TimeSpan TerminalPersistenceRetryDelay = TimeSpan.FromMilliseconds(100);
    // How long a governed agent's work can sit in the log before the feed shows it. Short enough to
    // read as live, long enough that polling a small file costs nothing against a writing agent.
    private static readonly TimeSpan FollowPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly Func<string, IGovernedTaskService> _serviceFactory;
    private readonly Func<string, IAgentAdapter> _adapterFactory;
    private readonly IContextAssembler _contextAssembler;
    private readonly CognitiveArtifactLoader _artifactLoader;
    private readonly JsonSerializerOptions _json;

    public CliApplication(
        TextWriter output,
        TextWriter error,
        Func<string, IGovernedTaskService> serviceFactory,
        Func<string, IAgentAdapter> adapterFactory,
        IContextAssembler contextAssembler)
        : this(output, error, serviceFactory, adapterFactory, contextAssembler, new CognitiveArtifactLoader())
    {
    }

    private CliApplication(
        TextWriter output,
        TextWriter error,
        Func<string, IGovernedTaskService> serviceFactory,
        Func<string, IAgentAdapter> adapterFactory,
        IContextAssembler contextAssembler,
        CognitiveArtifactLoader artifactLoader)
    {
        _output = output;
        _error = error;
        _serviceFactory = serviceFactory;
        _adapterFactory = adapterFactory;
        _contextAssembler = contextAssembler;
        _artifactLoader = artifactLoader;
        _json = LedgerJson.CreateOptions(indented: true);
    }

    public static CliApplication CreateDefault()
    {
        var runner = new SystemProcessRunner();
        return new CliApplication(
            Console.Out,
            Console.Error,
            root =>
            {
                var reducer = new TaskReducer();
                return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
            },
            provider => provider.ToLowerInvariant() switch
            {
                "codex" => new CodexAgentAdapter(runner),
                "claude" => new ClaudeAgentAdapter(runner),
                _ => throw new CliUsageException("Provider must be 'codex' or 'claude'.")
            },
            new ContextAssembler(),
            new CognitiveArtifactLoader());
    }

    public async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            if (arguments.Count == 0 || arguments.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
                arguments.Contains("-h", StringComparer.OrdinalIgnoreCase))
            {
                await _output.WriteLineAsync(HelpText).ConfigureAwait(false);
                return 0;
            }

            var input = CommandLine.Parse(arguments);
            if (input.Command is ["help"])
            {
                await _output.WriteLineAsync(HelpText).ConfigureAwait(false);
                return 0;
            }

            var command = NormalizeCommand(input);
            ValidateOptions(command, input);
            var root = Path.GetFullPath(input.Optional("root") ?? DefaultWorkspaceRoot());
            var service = _serviceFactory(root);
            await DispatchAsync(command, input, service, root, cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _error.WriteLineAsync("Cancelled.").ConfigureAwait(false);
            return 130;
        }
        catch (ProviderRunFailedException exception)
        {
            await _error.WriteLineAsync($"error: {exception.Message}").ConfigureAwait(false);
            return 3;
        }
        catch (Exception exception) when (exception is CliUsageException or GovernanceException or
                                          AgentAdapterException or ArgumentException or InvalidDataException or IOException)
        {
            await _error.WriteLineAsync($"error: {exception.Message}").ConfigureAwait(false);
            return exception is CliUsageException ? 2 : 1;
        }
    }

    private async Task DispatchAsync(
        string command,
        CommandLine input,
        IGovernedTaskService service,
        string ledgerRoot,
        CancellationToken cancellationToken)
    {
        switch (command)
        {
            case "task open":
                await ExecuteAsync(service, input, new OpenTaskCommand(
                    Actor(input), Cause(input), Correlation(input), Task(input),
                    input.Required("title"), input.Required("goal")), cancellationToken).ConfigureAwait(false);
                break;
            case "who":
                await WriteWhoAsync(service, input, cancellationToken).ConfigureAwait(false);
                break;
            case "status":
            case "task status":
                await WriteStateAsync(service, input, cancellationToken).ConfigureAwait(false);
                break;
            case "history":
            case "task history":
                await WriteHistoryAsync(service, input, cancellationToken).ConfigureAwait(false);
                break;
            case "actor attach":
                await AttachActorAsync(service, input, cancellationToken).ConfigureAwait(false);
                break;
            case "context build":
                await BuildContextAsync(service, input, cancellationToken).ConfigureAwait(false);
                break;
            case "claim add":
                await ExecuteAsync(service, input, new AddClaimCommand(
                    Actor(input), Cause(input), Correlation(input), new ClaimId(input.Required("id")),
                    input.Required("statement"), input.Optional("consequence")), cancellationToken).ConfigureAwait(false);
                break;
            case "claim resolve":
                await ExecuteAsync(service, input, new ResolveClaimCommand(
                    Actor(input), Cause(input), Correlation(input), new ClaimId(input.Required("id")),
                    EnumValue<ClaimStatus>(input, "status"),
                    input.Many("evidence").Select(value => new EvidenceId(value)).ToArray(),
                    OptionalId(input.Optional("superseded-by"), value => new ClaimId(value))), cancellationToken).ConfigureAwait(false);
                break;
            case "evidence add":
                await ExecuteAsync(service, input, new AddEvidenceCommand(
                    Actor(input), Cause(input), Correlation(input), new EvidenceId(input.Required("id")),
                    input.Required("source-type"), input.Required("citation"), input.Required("summary"),
                    input.Many("supports").Select(value => new ClaimId(value)).ToArray(),
                    input.Many("refutes").Select(value => new ClaimId(value)).ToArray()), cancellationToken).ConfigureAwait(false);
                break;
            case "decision propose":
                await ExecuteAsync(service, input, new ProposeDecisionCommand(
                    Actor(input), Cause(input), Correlation(input), new DecisionId(input.Required("id")),
                    input.Required("statement"), input.Required("rationale"),
                    input.Many("depends-on").Select(value => new ClaimId(value)).ToArray(),
                    OptionalId(input.Optional("supersedes"), value => new DecisionId(value))), cancellationToken).ConfigureAwait(false);
                break;
            case "decision resolve":
                await ExecuteAsync(service, input, new ResolveDecisionCommand(
                    Actor(input), Cause(input), Correlation(input), new DecisionId(input.Required("id")),
                    EnumValue<DecisionStatus>(input, "status")), cancellationToken).ConfigureAwait(false);
                break;
            case "challenge raise":
                await ExecuteAsync(service, input, new RaiseChallengeCommand(
                    Actor(input), Cause(input), Correlation(input), new ChallengeId(input.Required("id")),
                    input.Required("target-type"), input.Required("target-id"), input.Required("reason"),
                    input.Many("evidence").Select(value => new EvidenceId(value)).ToArray()), cancellationToken).ConfigureAwait(false);
                break;
            case "challenge dispose":
                await ExecuteAsync(service, input, new DisposeChallengeCommand(
                    Actor(input), Cause(input), Correlation(input), new ChallengeId(input.Required("id")),
                    EnumValue<ChallengeStatus>(input, "status")), cancellationToken).ConfigureAwait(false);
                break;
            case "work add":
                var scopes = input.Many("scope")
                    .Select(ResolveExistingDirectory)
                    .Distinct(PathComparer)
                    .ToArray();
                EnsureLedgerIsOutsideProviderDirectories(ledgerRoot, scopes);
                await ExecuteAsync(service, input, new AddWorkItemCommand(
                    Actor(input), Cause(input), Correlation(input), new WorkItemId(input.Required("id")),
                    input.Required("title"), OptionalId(input.Optional("owner"), value => new ActorId(value)),
                    input.Many("depends-on").Select(value => new ClaimId(value)).ToArray(), scopes,
                    OptionalId(input.Optional("not-split-because"), value => new AlternativeId(value))), cancellationToken).ConfigureAwait(false);
                break;
            case "escalation raise":
                await ExecuteAsync(service, input, new RaiseEscalationCommand(
                    Actor(input), Cause(input), Correlation(input), new EscalationId(input.Required("id")),
                    EnumValue<EscalationKind>(input, "kind"), input.Required("question"),
                    OptionalId(input.Optional("work"), value => new WorkItemId(value)),
                    input.Many("option"), input.Optional("recommend"),
                    input.Many("evidence").Select(value => new EvidenceId(value)).ToArray()), cancellationToken).ConfigureAwait(false);
                break;
            case "escalation resolve":
                await ExecuteAsync(service, input, new ResolveEscalationCommand(
                    Actor(input), Cause(input), Correlation(input), new EscalationId(input.Required("id")),
                    EnumValue<EscalationStatus>(input, "status"), input.Optional("resolution")), cancellationToken).ConfigureAwait(false);
                break;
            case "alternative record":
                await ExecuteAsync(service, input, new RecordAlternativeCommand(
                    Actor(input), Cause(input), Correlation(input), new AlternativeId(input.Required("id")),
                    input.Required("statement"), input.Required("rejected-because"),
                    OptionalId(input.Optional("replaced-by"), value => new DecisionId(value))), cancellationToken).ConfigureAwait(false);
                break;
            case "lesson mark":
                await ExecuteAsync(service, input, new MarkLessonBearingCommand(
                    Actor(input), Cause(input), Correlation(input),
                    EnumValue<LessonSourceKind>(input, "kind"), input.Required("source"),
                    OptionalId(input.Optional("supersedes"), value => new LessonId(value))), cancellationToken).ConfigureAwait(false);
                break;
            case "constraint add":
                await ExecuteAsync(service, input, new AddConstraintCommand(
                    Actor(input), Cause(input), Correlation(input), new ConstraintId(input.Required("id")),
                    input.Required("statement"), input.Required("source"), input.Many("scope")), cancellationToken).ConfigureAwait(false);
                break;
            case "constraint supersede":
                await ExecuteAsync(service, input, new SupersedeConstraintCommand(
                    Actor(input), Cause(input), Correlation(input), new ConstraintId(input.Required("id"))), cancellationToken).ConfigureAwait(false);
                break;
            case "work complete":
                await ExecuteAsync(service, input, new CompleteWorkItemCommand(
                    Actor(input), Cause(input), Correlation(input), new WorkItemId(input.Required("id")),
                    input.Optional("without-verification")), cancellationToken).ConfigureAwait(false);
                break;
            case "work block":
                await ExecuteAsync(service, input, new BlockWorkItemCommand(
                    Actor(input), Cause(input), Correlation(input), new WorkItemId(input.Required("id")),
                    input.Required("reason"),
                    OptionalId(input.Optional("escalation"), value => new EscalationId(value))), cancellationToken).ConfigureAwait(false);
                break;
            case "work unblock":
                await ExecuteAsync(service, input, new UnblockWorkItemCommand(
                    Actor(input), Cause(input), Correlation(input), new WorkItemId(input.Required("id"))), cancellationToken).ConfigureAwait(false);
                break;
            case "work abandon":
                await ExecuteAsync(service, input, new AbandonWorkItemCommand(
                    Actor(input), Cause(input), Correlation(input), new WorkItemId(input.Required("id")),
                    input.Required("reason")), cancellationToken).ConfigureAwait(false);
                break;
            case "run start":
                await ExecuteAsync(service, input, CreateStartRun(input), cancellationToken).ConfigureAwait(false);
                break;
            case "run complete":
                await ExecuteAsync(service, input, new CompleteRunCommand(
                    Actor(input), Cause(input), Correlation(input), new RunId(input.Required("run")),
                    EnumValue<AgentRunStatus>(input, "status"), input.Optional("session")), cancellationToken).ConfigureAwait(false);
                break;
            case "stage transition":
                await ExecuteAsync(service, input, new RequestStageTransitionCommand(
                    Actor(input), Cause(input), Correlation(input), EnumValue<TaskStage>(input, "stage")), cancellationToken).ConfigureAwait(false);
                break;
            case "provider launch":
                await LaunchProviderAsync(service, input, AgentLaunchMode.New, ledgerRoot, cancellationToken).ConfigureAwait(false);
                break;
            case "provider resume":
                await LaunchProviderAsync(service, input, AgentLaunchMode.Resume, ledgerRoot, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new CliUsageException($"Unknown command '{string.Join(' ', input.Command)}'. Use --help.");
        }
    }

    private static string NormalizeCommand(CommandLine input) =>
        string.Join(' ', input.Command).ToLowerInvariant();

    private static void ValidateOptions(string command, CommandLine input)
    {
        if (!AllowedOptions.TryGetValue(command, out var allowedOptions))
        {
            throw new CliUsageException($"Unknown command '{string.Join(' ', input.Command)}'. Use --help.");
        }

        input.EnsureOnlyAllowedOptions(command, allowedOptions);
    }

    private async Task AttachActorAsync(IGovernedTaskService service, CommandLine input, CancellationToken cancellationToken)
    {
        var role = EnumValue<RoleKind>(input, "role");
        var capabilities = input.Many("capability").Count == 0
            ? RoleDefaults.For(role)
            : input.Many("capability").Select(ParseEnum<Capability>).Distinct().OrderBy(value => value).ToArray();
        RoleDefaults.EnsureSafe(role, capabilities);
        await ExecuteAsync(service, input, new AssignRoleCommand(
            Actor(input), Cause(input), Correlation(input), new ActorId(input.Required("target")), role, capabilities),
            cancellationToken).ConfigureAwait(false);
    }

    // One answer to "who is working on this, as what, with which model".
    private async Task WriteWhoAsync(IGovernedTaskService service, CommandLine input, CancellationToken cancellationToken)
    {
        var state = await RequireStateAsync(service, Task(input), cancellationToken).ConfigureAwait(false);
        var rows = state.Roles.Values
            .OrderBy(assignment => assignment.Role)
            .ThenBy(assignment => assignment.ActorId.Value, StringComparer.Ordinal)
            .Select(assignment =>
            {
                var live = state.Runs.Values
                    .Where(run => run.ActorId == assignment.ActorId && run.Status == AgentRunStatus.Active)
                    .OrderBy(run => run.Id.Value, StringComparer.Ordinal)
                    .FirstOrDefault();
                return new
                {
                    Actor = assignment.ActorId.Value,
                    Role = assignment.Role.ToString(),
                    Status = live is null ? "idle" : "working",
                    Run = live?.Id.Value,
                    WorkItem = live?.WorkItemId?.Value,
                    Provider = live?.Provider,
                    live?.Model,
                    live?.ProviderVersion,
                    Session = live?.ProviderSessionId,
                    Since = live?.StartedAt
                };
            })
            .ToArray();

        var occupied = state.WorkItems.Values
            // Which statuses release an area is this rule written twice: the second copy is the
            // filter in CommandHandler.EnsureScopeIsNotAlreadyOccupied. They must change together.
            // Apart, `who` shows an operator an area as taken that `work add` hands to someone else
            // in the next command, or the reverse.
            .Where(item => item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Stale
                or WorkItemStatus.Abandoned))
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .Select(item => new { WorkItem = item.Id.Value, item.Status, Owner = item.Owner?.Value, Areas = item.ResourceScope })
            .ToArray();

        await WriteJsonAsync(new { state.TaskId, Actors = rows, OccupiedAreas = occupied }).ConfigureAwait(false);
    }

    private async Task WriteStateAsync(IGovernedTaskService service, CommandLine input, CancellationToken cancellationToken)
    {
        var state = await RequireStateAsync(service, Task(input), cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(state).ConfigureAwait(false);
    }

    // Without --follow this prints the log and returns, as it always has. With it, the process stays
    // and prints each event as it is appended, so an operator watching several governed agents sees
    // their work land in one feed instead of re-running the command to find out.
    private async Task WriteHistoryAsync(IGovernedTaskService service, CommandLine input, CancellationToken cancellationToken)
    {
        var taskId = Task(input);
        var cursor = SinceVersion(input.Optional("since"));
        var version = await WriteEventsAfterAsync(service, taskId, cursor, cancellationToken).ConfigureAwait(false);
        // An empty log is a task that was never opened. Said now rather than followed in silence: a
        // feed that prints nothing because the task ID is a typo looks exactly like a quiet task.
        if (version == 0)
        {
            throw new CliUsageException($"Task '{taskId}' was not found.");
        }

        if (!input.Flag("follow"))
        {
            return;
        }

        cursor = Math.Max(cursor, version);
        while (true)
        {
            await System.Threading.Tasks.Task.Delay(FollowPollInterval, cancellationToken).ConfigureAwait(false);
            version = await WriteEventsAfterAsync(service, taskId, cursor, cancellationToken).ConfigureAwait(false);
            cursor = Math.Max(cursor, version);
        }
    }

    // The log is re-read through the service on every poll rather than tailed by byte offset, because
    // GetHistoryAsync reads it under the task's mutation lock and validates each envelope. A feed that
    // read the file itself could print a half-written line, or an event the kernel refuses on replay.
    private async Task<long> WriteEventsAfterAsync(
        IGovernedTaskService service,
        TaskId taskId,
        long cursor,
        CancellationToken cancellationToken)
    {
        var options = LedgerJson.CreateOptions();
        var version = 0L;
        await foreach (var @event in service.GetHistoryAsync(taskId, cancellationToken).ConfigureAwait(false))
        {
            // An event's position in the log is the task version it produced, so counting them is
            // what makes --since a version and not an offset the caller has to translate.
            version++;
            if (version > cursor)
            {
                await _output.WriteLineAsync(JsonSerializer.Serialize(@event, options)).ConfigureAwait(false);
            }
        }

        return version;
    }

    private async Task BuildContextAsync(IGovernedTaskService service, CommandLine input, CancellationToken cancellationToken)
    {
        var manifest = await CreateContextAsync(service, input, Actor(input), cancellationToken).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(manifest, _json) + Environment.NewLine;
        var outputPath = input.Optional("output");
        if (outputPath is null)
        {
            await _output.WriteAsync(json).ConfigureAwait(false);
            return;
        }

        await File.WriteAllTextAsync(Path.GetFullPath(outputPath), json, cancellationToken).ConfigureAwait(false);
        await _output.WriteLineAsync(Path.GetFullPath(outputPath)).ConfigureAwait(false);
    }

    private async Task<ContextManifest> CreateContextAsync(
        IGovernedTaskService service,
        CommandLine input,
        ActorId actorId,
        CancellationToken cancellationToken)
    {
        var state = await RequireStateAsync(service, Task(input), cancellationToken).ConfigureAwait(false);
        var artifacts = await _artifactLoader.LoadAsync(input.Optional("cognitive-root"), cancellationToken).ConfigureAwait(false);
        var workItem = OptionalId(input.Optional("work"), value => new WorkItemId(value));
        return _contextAssembler.Build(state, actorId, workItem, artifacts, DateTimeOffset.UtcNow);
    }

    private async Task LaunchProviderAsync(
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
        // Authority and scope are settled before an adapter is resolved or any process spawned: a
        // request that will be refused should cost neither.
        var grants = ResolveProviderGrants(launchState, Actor(input), requestedWorkItem, input, ledgerRoot);
        var adapter = _adapterFactory(provider);
        var executable = ExecutableResolver.Resolve(provider, input.Optional("executable"));
        // Probed before the run is recorded, so the ledger knows which cognition ran even if the
        // launch later fails. What ran should never be known only in memory.
        var providerVersion = await adapter.ProbeVersionAsync(executable, cancellationToken).ConfigureAwait(false);
        // Held only here, for this run's lifetime. Only its hash is recorded, and it never reaches
        // the manifest, the briefing or the child's environment.
        var launchToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var start = CreateStartRun(input, provider, sessionId, providerVersion,
            CommandHandler.HashLaunchToken(launchToken));
        var started = await service.ExecuteAsync(Task(input), start, cancellationToken).ConfigureAwait(false);
        var startedEventId = started.Events[^1].EventId;
        AgentRunResult result;
        try
        {
            // The manifest is filtered by the subject's role, not the dispatcher's. An operator who
            // dispatches a code reviewer must not hand it an operator's view of the task.
            var manifest = await CreateContextAsync(
                service, input, SubjectOrActor(input), cancellationToken).ConfigureAwait(false);
            var request = new AgentLaunchRequest(
                start.RunId, Task(input), SubjectOrActor(input), start.WorkItemId, mode, provider,
                executable,
                grants.WorkingDirectory, ledgerRoot, ResolveLedgerCommandLine(),
                JsonSerializer.Serialize(manifest, _json), sessionId, PermissionProfile.WorkspaceGoverned,
                input.Optional("model"), input.Optional("output-schema"),
                grants.AdditionalDirectories,
                new Dictionary<string, string>(),
                TimeSpan.FromSeconds(PositiveInt(input.Optional("timeout-seconds"), 1800)));
            result = await adapter.RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception launchException)
        {
            try
            {
                var status = launchException is OperationCanceledException
                    ? AgentRunStatus.Cancelled
                    : AgentRunStatus.Failed;
                await CompleteRunWithFreshTokenAsync(
                    service, input, start.RunId, sessionId, status, startedEventId, launchToken).ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw new IOException(
                    "Provider launch failed and its active run could not be closed.",
                    new AggregateException(launchException, cleanupException));
            }

            throw;
        }

        Exception? completionFailure = null;
        try
        {
            await CompleteRunWithFreshTokenAsync(
                service, input, start.RunId, result.ProviderSessionId, result.Status, startedEventId, launchToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            completionFailure = exception;
        }

        await WriteJsonAsync(result).ConfigureAwait(false);
        if (completionFailure is not null)
        {
            throw new IOException(
                $"Provider run '{result.RunId}' returned a terminal result, but its Ledger run could not be closed. " +
                "The provider result was written to standard output for recovery.",
                completionFailure);
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

    private static async Task CompleteRunWithFreshTokenAsync(
        IGovernedTaskService service,
        CommandLine input,
        RunId runId,
        string? sessionId,
        AgentRunStatus status,
        EventId causationId,
        string launchToken)
    {
        using var completion = new CancellationTokenSource(TerminalPersistenceDeadline);
        var command = new CompleteRunCommand(
            Actor(input), causationId, Correlation(input), runId, status, sessionId, launchToken);

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

    private static ProviderGrants ResolveProviderGrants(
        GovernedTaskState state,
        ActorId actorId,
        WorkItemId? workItemId,
        CommandLine input,
        string ledgerRoot)
    {
        var requestedWorkingDirectory = input.Optional("working-directory");
        var requestedAdditionalDirectories = input.Many("add-dir");
        if (workItemId is null)
        {
            if (!state.Roles.TryGetValue(actorId, out var assignment) || assignment.Role != RoleKind.Operator)
            {
                throw new GovernanceException("Only an operator may launch a provider without governed work scope.");
            }

            var unscopedGrants = new ProviderGrants(
                ResolveExistingDirectory(requestedWorkingDirectory ?? Environment.CurrentDirectory),
                requestedAdditionalDirectories.Select(ResolveExistingDirectory).ToArray());
            EnsureLedgerIsOutsideProviderDirectories(
                ledgerRoot, unscopedGrants.AdditionalDirectories.Prepend(unscopedGrants.WorkingDirectory));
            return unscopedGrants;
        }

        if (!state.WorkItems.TryGetValue(workItemId.Value, out var workItem))
        {
            throw new GovernanceException($"Unknown work item '{workItemId.Value}'.");
        }

        if (!state.Roles.TryGetValue(actorId, out var actorAssignment))
        {
            throw new GovernanceException($"Actor '{actorId}' has no assigned role.");
        }

        if (workItem.Owner is { } owner && owner != actorId && actorAssignment.Role != RoleKind.Operator)
        {
            throw new GovernanceException($"Actor '{actorId}' does not own work item '{workItemId.Value}'.");
        }

        if (workItem.ResourceScope.Any(scope => !Path.IsPathFullyQualified(scope)))
        {
            throw new GovernanceException(
                $"Work item '{workItemId.Value}' contains a legacy relative directory scope and must be recreated.");
        }

        var scopes = workItem.ResourceScope
            .Select(ResolveExistingDirectory)
            .Distinct(PathComparer)
            .ToArray();
        EnsureLedgerIsOutsideProviderDirectories(ledgerRoot, scopes);
        if (scopes.Length == 0)
        {
            throw new GovernanceException($"Work item '{workItemId.Value}' has no directory scope for a provider run.");
        }

        var workingDirectory = ResolveExistingDirectory(requestedWorkingDirectory ?? scopes[0]);
        var additionalDirectories = requestedAdditionalDirectories.Select(ResolveExistingDirectory).ToArray();
        foreach (var grant in additionalDirectories.Prepend(workingDirectory))
        {
            if (!scopes.Any(scope => IsContainedPath(scope, grant)))
            {
                throw new GovernanceException(
                    $"Provider directory '{grant}' is outside work item '{workItemId.Value}' scope.");
            }
        }

        // The ledger is a governed channel, not work product, so it is granted separately from the
        // work item's scope. Without this, an agent could only record truth when the ledger happened
        // to sit inside its own scope — which two concurrent agents on disjoint scopes can never
        // both satisfy, making concurrency and self-hosting mutually exclusive.
        return new ProviderGrants(
            workingDirectory,
            [.. additionalDirectories, ResolveExistingDirectory(ledgerRoot)]);
    }

    private static string ResolveExistingDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new GovernanceException($"Provider directory '{fullPath}' does not exist.");
        }

        var root = Path.GetPathRoot(fullPath)!;
        var current = root;
        foreach (var segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var directory = new DirectoryInfo(current);
            if (directory.LinkTarget is not null)
            {
                current = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new GovernanceException($"Could not resolve provider directory link '{directory.FullName}'.");
            }
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }

    private static bool IsContainedPath(string scope, string candidate)
    {
        if (string.Equals(scope, candidate, PathComparison))
        {
            return true;
        }

        var prefix = Path.TrimEndingDirectorySeparator(scope) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, PathComparison);
    }

    private static void EnsureLedgerIsOutsideProviderDirectories(
        string ledgerRoot,
        IEnumerable<string> providerDirectories)
    {
        var canonicalLedgerRoot = ResolveExistingDirectory(ledgerRoot);
        var canonicalProviderDirectories = providerDirectories
            .Select(ResolveExistingDirectory)
            .Distinct(PathComparer)
            .ToArray();
        // A provider directory that *contains* the Ledger root is the self-hosting case: a governed
        // agent has to be able to record claims, evidence and escalations while it works, and under
        // a workspace sandbox it can only write inside its own workspace. The protection against a
        // tampered log is not this check — it is replay: FileGovernedTaskService re-validates event
        // sequence and causation, and TaskTransitionValidator re-checks payload provenance, so a
        // forged history fails closed. Truncation is the residual risk, detected separately by
        // comparing the replayed version against the materialised state.
        var containedDirectory = canonicalProviderDirectories.FirstOrDefault(
            directory => IsContainedPath(canonicalLedgerRoot, directory));
        if (containedDirectory is not null)
        {
            throw new GovernanceException(
                $"Provider directory '{containedDirectory}' is inside the authoritative Ledger root '{canonicalLedgerRoot}'.");
        }
    }

    private static string DefaultWorkspaceRoot()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.Combine(localData, "AILedger", "tasks");
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedOptions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["task open"] = Options("root", "task", "actor", "title", "goal", "cause", "correlation"),
            ["who"] = Options("root", "task", "actor"),
            ["status"] = Options("root", "task", "actor"),
            ["task status"] = Options("root", "task", "actor"),
            ["history"] = Options("root", "task", "actor", "follow", "since"),
            ["task history"] = Options("root", "task", "actor", "follow", "since"),
            ["actor attach"] = Options(
                "root", "task", "actor", "target", "role", "capability", "cause", "correlation"),
            ["context build"] = Options("root", "task", "actor", "work", "cognitive-root", "output"),
            ["claim add"] = Options(
                "root", "task", "actor", "id", "statement", "consequence", "cause", "correlation"),
            ["claim resolve"] = Options(
                "root", "task", "actor", "id", "status", "evidence", "superseded-by", "cause", "correlation"),
            ["evidence add"] = Options(
                "root", "task", "actor", "id", "source-type", "citation", "summary", "supports", "refutes",
                "cause", "correlation"),
            ["decision propose"] = Options(
                "root", "task", "actor", "id", "statement", "rationale", "depends-on", "supersedes",
                "cause", "correlation"),
            ["decision resolve"] = Options(
                "root", "task", "actor", "id", "status", "cause", "correlation"),
            ["challenge raise"] = Options(
                "root", "task", "actor", "id", "target-type", "target-id", "reason", "evidence",
                "cause", "correlation"),
            ["challenge dispose"] = Options(
                "root", "task", "actor", "id", "status", "cause", "correlation"),
            ["work add"] = Options(
                "root", "task", "actor", "id", "title", "owner", "depends-on", "scope",
                "not-split-because", "cause", "correlation"),
            ["escalation raise"] = Options(
                "root", "task", "actor", "id", "kind", "question", "work", "option", "recommend", "evidence",
                "cause", "correlation"),
            ["escalation resolve"] = Options(
                "root", "task", "actor", "id", "status", "resolution", "cause", "correlation"),
            ["alternative record"] = Options(
                "root", "task", "actor", "id", "statement", "rejected-because", "replaced-by",
                "cause", "correlation"),
            ["lesson mark"] = Options(
                "root", "task", "actor", "kind", "source", "supersedes", "cause", "correlation"),
            ["constraint add"] = Options(
                "root", "task", "actor", "id", "statement", "source", "scope", "cause", "correlation"),
            ["constraint supersede"] = Options(
                "root", "task", "actor", "id", "cause", "correlation"),
            ["work complete"] = Options(
                "root", "task", "actor", "id", "without-verification", "cause", "correlation"),
            ["work block"] = Options(
                "root", "task", "actor", "id", "reason", "escalation", "cause", "correlation"),
            ["work unblock"] = Options("root", "task", "actor", "id", "cause", "correlation"),
            ["work abandon"] = Options("root", "task", "actor", "id", "reason", "cause", "correlation"),
            ["run start"] = Options(
                "root", "task", "actor", "subject", "run", "work", "provider", "session", "cause", "correlation"),
            ["run complete"] = Options(
                "root", "task", "actor", "run", "status", "session", "cause", "correlation"),
            ["stage transition"] = Options(
                "root", "task", "actor", "stage", "cause", "correlation"),
            ["provider launch"] = ProviderOptions(),
            ["provider resume"] = ProviderOptions()
        };

    private static IReadOnlySet<string> ProviderOptions() => Options(
        "root", "task", "actor", "subject", "run", "work", "provider", "session", "executable", "working-directory",
        "model", "timeout-seconds", "add-dir", "cognitive-root", "output-schema", "cause", "correlation");

    private static IReadOnlySet<string> Options(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    private async Task ExecuteAsync(
        IGovernedTaskService service,
        CommandLine input,
        LedgerCommand command,
        CancellationToken cancellationToken)
    {
        var outcome = await service.ExecuteAsync(Task(input), command, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(new { outcome.State.TaskId, outcome.State.Version, outcome.State.Stage, Events = outcome.Events.Select(e => e.EventId) })
            .ConfigureAwait(false);
    }

    private async Task WriteJsonAsync<T>(T value) =>
        await _output.WriteLineAsync(JsonSerializer.Serialize(value, _json)).ConfigureAwait(false);

    private static async Task<GovernedTaskState> RequireStateAsync(
        IGovernedTaskService service,
        TaskId taskId,
        CancellationToken cancellationToken) =>
        await service.GetStateAsync(taskId, cancellationToken).ConfigureAwait(false)
        ?? throw new CliUsageException($"Task '{taskId}' was not found.");

    private static StartRunCommand CreateStartRun(
        CommandLine input,
        string? provider = null,
        string? sessionId = null,
        string? providerVersion = null,
        string? launchTokenHash = null) =>
        new(
            Actor(input), Cause(input), Correlation(input), new RunId(input.Required("run")),
            OptionalId(input.Optional("work"), value => new WorkItemId(value)),
            provider ?? input.Required("provider"), sessionId ?? input.Optional("session"),
            input.Optional("model"), providerVersion, launchTokenHash, Subject(input));

    private static ActorId Actor(CommandLine input) => new(input.Required("actor"));
    // Who the run is for. Absent, an actor starts its own run and nothing changes.
    private static ActorId? Subject(CommandLine input) =>
        OptionalId(input.Optional("subject"), value => new ActorId(value));
    // The actor whose role filters the manifest and whose provenance the run carries.
    private static ActorId SubjectOrActor(CommandLine input) => Subject(input) ?? Actor(input);
    private static TaskId Task(CommandLine input) => new(input.Required("task"));
    private static EventId? Cause(CommandLine input) => OptionalId(input.Optional("cause"), value => new EventId(value));
    // A launched agent runs inside its own work scope, never the Ledger repository, so a relative
    // "--project src/AILedger.Cli" would not resolve. Hand it this process's own absolute entry point.
    private static string ResolveLedgerCommandLine()
    {
        var assembly = Path.Combine(AppContext.BaseDirectory, "AILedger.Cli.dll");
        var host = Environment.ProcessPath;
        if (host is null)
        {
            return $"dotnet \"{assembly}\"";
        }

        return Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"\"{host}\" \"{assembly}\""
            : $"\"{host}\"";
    }

    private static string Correlation(CommandLine input) => input.CorrelationId;
    private static T? OptionalId<T>(string? value, Func<string, T> factory) where T : struct =>
        string.IsNullOrWhiteSpace(value) ? null : factory(value);
    private static T EnumValue<T>(CommandLine input, string name) where T : struct, Enum => ParseEnum<T>(input.Required(name));
    private static T ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value.Replace("-", string.Empty, StringComparison.Ordinal), true, out var parsed) &&
        Enum.IsDefined(parsed)
            ? parsed
            : throw new CliUsageException($"'{value}' is not a valid {typeof(T).Name}.");
    // Zero is the whole log, which is what history has always printed, so it is also the default.
    private static long SinceVersion(string? value) =>
        value is null ? 0
            : long.TryParse(value, out var parsed) && parsed >= 0
                ? parsed
                : throw new CliUsageException("Since must be a task version of zero or more.");
    private static int PositiveInt(string? value, int fallback) =>
        value is null ? fallback : int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new CliUsageException("Timeout must be a positive integer.");

    private const string HelpText = """
        AILedger 2.0 governed task CLI

        Global option: --root PATH (default: platform local application data/AILedger/tasks)
        Every mutation requires an explicit --actor ID. Repeat list options once per value.

        task open          --task ID --actor ID --title TEXT --goal TEXT
        status             --task ID
        who                --task ID   (actors, roles, live runs, occupied areas)
        history            --task ID [--follow] [--since VERSION]
        actor attach       --task ID --actor OPERATOR --target ID --role ROLE [--capability CAP]
        context build      --task ID --actor ID [--work ID] [--cognitive-root PATH] [--output FILE]
        claim add          --task ID --actor ID --id ID --statement TEXT [--consequence TEXT]
        claim resolve      --task ID --actor ID --id ID --status STATUS [--evidence ID]
                           [--superseded-by CLAIM]   (required when --status superseded)
        evidence add       --task ID --actor ID --id ID --source-type TYPE --citation TEXT --summary TEXT
                           [--supports CLAIM] [--refutes CLAIM]
        decision propose   --task ID --actor ID --id ID --statement TEXT --rationale TEXT
                           [--depends-on CLAIM] [--supersedes DECISION]
        decision resolve   --task ID --actor ID --id ID --status accepted|superseded
        challenge raise    --task ID --actor ID --id ID --target-type TYPE --target-id ID --reason TEXT
                           [--evidence ID]
        challenge dispose  --task ID --actor ID --id ID --status supported|rejected|withdrawn
        work add           --task ID --actor ID --id ID --title TEXT [--owner ID]
                           [--depends-on CLAIM] [--scope PATH] [--not-split-because ALT-ID]
        work complete      --task ID --actor ID --id ID [--without-verification REASON]
        work block         --task ID --actor ID --id ID --reason TEXT [--escalation ID]
        work unblock       --task ID --actor ID --id ID
        work abandon       --task ID --actor ID --id ID --reason TEXT
        escalation raise   --task ID --actor ID --id ID --kind business-decision|true-unknown
                           --question TEXT [--work ID] [--option TEXT] [--recommend TEXT] [--evidence ID]
        escalation resolve --task ID --actor ID --id ID --status resolved|withdrawn [--resolution TEXT]
        alternative record --task ID --actor ID --id ID --statement TEXT --rejected-because TEXT
                           [--replaced-by DECISION]
        lesson mark        --task ID --actor ID --kind validated-claim|rejected-alternative|
                           resolved-escalation --source ID [--supersedes LESSON]
        constraint add     --task ID --actor ID --id ID --statement TEXT --source TEXT [--scope TEXT]
        constraint supersede --task ID --actor ID --id ID
        run start          --task ID --actor ID --run ID [--work ID] --provider NAME [--session ID]
                           [--subject ID]
        run complete       --task ID --actor ID --run ID --status STATUS [--session ID]
        stage transition   --task ID --actor ID --stage STAGE
        provider launch    --task ID --actor ID --run ID --provider codex|claude [provider options]
        provider resume    --task ID --actor ID --run ID --provider codex|claude --session EXACT_ID [provider options]

        Provider options: --work ID --subject ID --executable PATH --working-directory PATH --model NAME
                          --timeout-seconds N --add-dir PATH --cognitive-root PATH --output-schema VALUE

        --subject dispatches a run for another actor: only an operator may pass it, and the run is
        authorised by --actor while the subject does the work, receives the manifest filtered by its
        own role, and owns the run's provenance. It is how a role that holds no run authority — a
        researcher, worker, verifier or code reviewer — is launched at all.

        A work item is completed only after two runs have completed against it: one whose subject
        held a working role, and one whose subject was a verifier. --without-verification REASON is
        the operator's override for both, and the reason goes in the log.

        --not-split-because names an existing alternative explaining why a work item claims more
        than one --scope area instead of being split into separate items. It is required only for a
        multi-area item: holding two areas is one agent taking what two could have held, and the
        kernel refuses to let that choice go unrecorded rather than judging whether it was right.

        work abandon releases an item that will never be completed, and hands its directory areas
        back for another work item to claim.

        lesson mark declares that one record is worth carrying into later tasks. Archiving mints a
        lesson only from marked sources, so a task with no marks teaches the next one nothing.
        --source names the record: a validated claim, a rejected alternative, or a resolved
        escalation. --supersedes names the lesson this one replaces, which keeps the older lesson out
        of a later task's recall without deleting it. Only an operator or a lead may mark: a mark
        decides what every later task inherits, which is scope authority rather than execution.

        history --follow keeps printing, one JSON line per event, as each event is appended, until it
        is interrupted. It is how an operator watches several governed agents work in one feed rather
        than re-running the command to find out what landed. --since VERSION suppresses everything up
        to that task version, so a feed can resume where a previous one stopped without repeating it;
        the version an event produced is its position in the log, which status reports as "version".
        """;

    private sealed class ProviderRunFailedException(string message) : Exception(message);
    private sealed record ProviderGrants(string WorkingDirectory, IReadOnlyList<string> AdditionalDirectories);
}
