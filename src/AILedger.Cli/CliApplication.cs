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
                    input.Many("evidence").Select(value => new EvidenceId(value)).ToArray()), cancellationToken).ConfigureAwait(false);
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
                    input.Many("depends-on").Select(value => new ClaimId(value)).ToArray(), scopes), cancellationToken).ConfigureAwait(false);
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
                    Actor(input), Cause(input), Correlation(input), new WorkItemId(input.Required("id"))), cancellationToken).ConfigureAwait(false);
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

    private async Task WriteStateAsync(IGovernedTaskService service, CommandLine input, CancellationToken cancellationToken)
    {
        var state = await RequireStateAsync(service, Task(input), cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(state).ConfigureAwait(false);
    }

    private async Task WriteHistoryAsync(IGovernedTaskService service, CommandLine input, CancellationToken cancellationToken)
    {
        var found = false;
        await foreach (var @event in service.GetHistoryAsync(Task(input), cancellationToken).ConfigureAwait(false))
        {
            found = true;
            await _output.WriteLineAsync(JsonSerializer.Serialize(@event, LedgerJson.CreateOptions())).ConfigureAwait(false);
        }

        if (!found)
        {
            throw new CliUsageException($"Task '{Task(input)}' was not found.");
        }
    }

    private async Task BuildContextAsync(IGovernedTaskService service, CommandLine input, CancellationToken cancellationToken)
    {
        var manifest = await CreateContextAsync(service, input, cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        var state = await RequireStateAsync(service, Task(input), cancellationToken).ConfigureAwait(false);
        var artifacts = await _artifactLoader.LoadAsync(input.Optional("cognitive-root"), cancellationToken).ConfigureAwait(false);
        var workItem = OptionalId(input.Optional("work"), value => new WorkItemId(value));
        return _contextAssembler.Build(state, Actor(input), workItem, artifacts, DateTimeOffset.UtcNow);
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

        var start = CreateStartRun(input, provider, sessionId);
        var launchState = await RequireStateAsync(service, Task(input), cancellationToken).ConfigureAwait(false);
        var grants = ResolveProviderGrants(launchState, Actor(input), start.WorkItemId, input, ledgerRoot);
        var started = await service.ExecuteAsync(Task(input), start, cancellationToken).ConfigureAwait(false);
        var startedEventId = started.Events[^1].EventId;
        AgentRunResult result;
        try
        {
            var manifest = await CreateContextAsync(service, input, cancellationToken).ConfigureAwait(false);
            var request = new AgentLaunchRequest(
                start.RunId, Task(input), Actor(input), start.WorkItemId, mode, provider,
                ExecutableResolver.Resolve(provider, input.Optional("executable")),
                grants.WorkingDirectory,
                JsonSerializer.Serialize(manifest, _json), sessionId, PermissionProfile.WorkspaceGoverned,
                input.Optional("model"), input.Optional("output-schema"),
                grants.AdditionalDirectories,
                new Dictionary<string, string>(),
                TimeSpan.FromSeconds(PositiveInt(input.Optional("timeout-seconds"), 1800)));
            result = await _adapterFactory(provider).RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception launchException)
        {
            try
            {
                var status = launchException is OperationCanceledException
                    ? AgentRunStatus.Cancelled
                    : AgentRunStatus.Failed;
                await CompleteRunWithFreshTokenAsync(
                    service, input, start.RunId, sessionId, status, startedEventId).ConfigureAwait(false);
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
                service, input, start.RunId, result.ProviderSessionId, result.Status, startedEventId).ConfigureAwait(false);
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
        EventId causationId)
    {
        using var completion = new CancellationTokenSource(TerminalPersistenceDeadline);
        var command = new CompleteRunCommand(
            Actor(input), causationId, Correlation(input), runId, status, sessionId);

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

        return new ProviderGrants(workingDirectory, additionalDirectories);
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
        var containingDirectory = canonicalProviderDirectories.FirstOrDefault(
            directory => IsContainedPath(directory, canonicalLedgerRoot));
        if (containingDirectory is not null)
        {
            throw new GovernanceException(
                $"Provider directory '{containingDirectory}' contains the authoritative Ledger root '{canonicalLedgerRoot}'.");
        }

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
            ["status"] = Options("root", "task", "actor"),
            ["task status"] = Options("root", "task", "actor"),
            ["history"] = Options("root", "task", "actor"),
            ["task history"] = Options("root", "task", "actor"),
            ["actor attach"] = Options(
                "root", "task", "actor", "target", "role", "capability", "cause", "correlation"),
            ["context build"] = Options("root", "task", "actor", "work", "cognitive-root", "output"),
            ["claim add"] = Options(
                "root", "task", "actor", "id", "statement", "consequence", "cause", "correlation"),
            ["claim resolve"] = Options(
                "root", "task", "actor", "id", "status", "evidence", "cause", "correlation"),
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
                "root", "task", "actor", "id", "title", "owner", "depends-on", "scope", "cause", "correlation"),
            ["escalation raise"] = Options(
                "root", "task", "actor", "id", "kind", "question", "work", "option", "recommend", "evidence",
                "cause", "correlation"),
            ["escalation resolve"] = Options(
                "root", "task", "actor", "id", "status", "resolution", "cause", "correlation"),
            ["alternative record"] = Options(
                "root", "task", "actor", "id", "statement", "rejected-because", "replaced-by",
                "cause", "correlation"),
            ["constraint add"] = Options(
                "root", "task", "actor", "id", "statement", "source", "scope", "cause", "correlation"),
            ["constraint supersede"] = Options(
                "root", "task", "actor", "id", "cause", "correlation"),
            ["work complete"] = Options("root", "task", "actor", "id", "cause", "correlation"),
            ["work block"] = Options(
                "root", "task", "actor", "id", "reason", "escalation", "cause", "correlation"),
            ["work unblock"] = Options("root", "task", "actor", "id", "cause", "correlation"),
            ["run start"] = Options(
                "root", "task", "actor", "run", "work", "provider", "session", "cause", "correlation"),
            ["run complete"] = Options(
                "root", "task", "actor", "run", "status", "session", "cause", "correlation"),
            ["stage transition"] = Options(
                "root", "task", "actor", "stage", "cause", "correlation"),
            ["provider launch"] = ProviderOptions(),
            ["provider resume"] = ProviderOptions()
        };

    private static IReadOnlySet<string> ProviderOptions() => Options(
        "root", "task", "actor", "run", "work", "provider", "session", "executable", "working-directory",
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

    private static StartRunCommand CreateStartRun(CommandLine input, string? provider = null, string? sessionId = null) =>
        new(
            Actor(input), Cause(input), Correlation(input), new RunId(input.Required("run")),
            OptionalId(input.Optional("work"), value => new WorkItemId(value)),
            provider ?? input.Required("provider"), sessionId ?? input.Optional("session"));

    private static ActorId Actor(CommandLine input) => new(input.Required("actor"));
    private static TaskId Task(CommandLine input) => new(input.Required("task"));
    private static EventId? Cause(CommandLine input) => OptionalId(input.Optional("cause"), value => new EventId(value));
    private static string Correlation(CommandLine input) => input.CorrelationId;
    private static T? OptionalId<T>(string? value, Func<string, T> factory) where T : struct =>
        string.IsNullOrWhiteSpace(value) ? null : factory(value);
    private static T EnumValue<T>(CommandLine input, string name) where T : struct, Enum => ParseEnum<T>(input.Required(name));
    private static T ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value.Replace("-", string.Empty, StringComparison.Ordinal), true, out var parsed) &&
        Enum.IsDefined(parsed)
            ? parsed
            : throw new CliUsageException($"'{value}' is not a valid {typeof(T).Name}.");
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
        history            --task ID
        actor attach       --task ID --actor OPERATOR --target ID --role ROLE [--capability CAP]
        context build      --task ID --actor ID [--work ID] [--cognitive-root PATH] [--output FILE]
        claim add          --task ID --actor ID --id ID --statement TEXT [--consequence TEXT]
        claim resolve      --task ID --actor ID --id ID --status STATUS [--evidence ID]
        evidence add       --task ID --actor ID --id ID --source-type TYPE --citation TEXT --summary TEXT
                           [--supports CLAIM] [--refutes CLAIM]
        decision propose   --task ID --actor ID --id ID --statement TEXT --rationale TEXT
                           [--depends-on CLAIM] [--supersedes DECISION]
        decision resolve   --task ID --actor ID --id ID --status accepted|superseded
        challenge raise    --task ID --actor ID --id ID --target-type TYPE --target-id ID --reason TEXT
                           [--evidence ID]
        challenge dispose  --task ID --actor ID --id ID --status supported|rejected|withdrawn
        work add           --task ID --actor ID --id ID --title TEXT [--owner ID]
                           [--depends-on CLAIM] [--scope PATH]
        work complete      --task ID --actor ID --id ID
        work block         --task ID --actor ID --id ID --reason TEXT [--escalation ID]
        work unblock       --task ID --actor ID --id ID
        escalation raise   --task ID --actor ID --id ID --kind business-decision|true-unknown
                           --question TEXT [--work ID] [--option TEXT] [--recommend TEXT] [--evidence ID]
        escalation resolve --task ID --actor ID --id ID --status resolved|withdrawn [--resolution TEXT]
        alternative record --task ID --actor ID --id ID --statement TEXT --rejected-because TEXT
                           [--replaced-by DECISION]
        constraint add     --task ID --actor ID --id ID --statement TEXT --source TEXT [--scope TEXT]
        constraint supersede --task ID --actor ID --id ID
        run start          --task ID --actor ID --run ID [--work ID] --provider NAME [--session ID]
        run complete       --task ID --actor ID --run ID --status STATUS [--session ID]
        stage transition   --task ID --actor ID --stage STAGE
        provider launch    --task ID --actor ID --run ID --provider codex|claude [provider options]
        provider resume    --task ID --actor ID --run ID --provider codex|claude --session EXACT_ID [provider options]

        Provider options: --work ID --executable PATH --working-directory PATH --model NAME
                          --timeout-seconds N --add-dir PATH --cognitive-root PATH --output-schema VALUE
        """;

    private sealed class ProviderRunFailedException(string message) : Exception(message);
    private sealed record ProviderGrants(string WorkingDirectory, IReadOnlyList<string> AdditionalDirectories);
}
