using System.Text.Json;
using AILedger.Cli.Actors;
using AILedger.Cli.Alternatives;
using AILedger.Cli.Artifacts;
using AILedger.Cli.Audit;
using AILedger.Cli.Challenges;
using AILedger.Cli.Closeout;
using AILedger.Cli.Claims;
using AILedger.Cli.Constraints;
using AILedger.Cli.ContextBriefing;
using AILedger.Cli.CoordinatorSessions;
using AILedger.Cli.Decisions;
using AILedger.Cli.Escalations;
using AILedger.Cli.Evidence;
using AILedger.Cli.Help;
using AILedger.Cli.Lessons;
using AILedger.Cli.Providers;
using AILedger.Cli.Preflight;
using AILedger.Cli.Retrospectives;
using AILedger.Cli.Routing;
using AILedger.Cli.Runs;
using AILedger.Cli.Stages;
using AILedger.Cli.Tasks;
using AILedger.Cli.Verification;
using AILedger.Cli.Versioning;
using AILedger.Cli.WorkItems;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Providers.Adapters;
using AILedger.Providers.Process;
using AILedger.Storage;

namespace AILedger.Cli;

public sealed class CliApplication
{
    private static readonly TextReader ProcessStandardInput = Console.In;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly Func<string, string, IGovernedTaskService> _serviceFactory;
    private readonly JsonSerializerOptions _json;
    private readonly CliCommandExecutor _executor;
    private readonly ContextBriefingCliCommands _contextCommands;
    private readonly ProviderLauncher _providerLauncher;
    private readonly CliCommandCatalog _commands;

    // The only directory a coordinator transcript may be read from, and the one control that makes a
    // named path evidence rather than an assertion (VC1). It defaults to where the harness actually
    // writes and is settable so that a test can stand up a harness directory of its own without
    // writing into the operator's real one — the same reason the ledger root and the lesson root are
    // the composing process's choice rather than an ambient path.
    public string HarnessTranscriptRoot { get; init; } =
        CoordinatorUsageReader.DefaultHarnessTranscriptRoot();

    // What container verification reads from the machine: environment, engine transport, process
    // spawner and clock. Settable for the reason HarnessTranscriptRoot is: a test replaces it rather
    // than reaching the real Docker socket or running a real profile command.
    internal VerificationHost VerificationHost { get; init; } = VerificationHost.Default;

    internal CliCommandCatalog CommandCatalog => _commands;

    // A factory that takes only the ledger root builds a service with no lesson store, so recall
    // reaches this root's own archived tasks and nothing else — the behaviour before lessons crossed
    // repositories. Selecting the shared store is the composing process's choice, which is what keeps
    // a caller holding a temporary root from writing into the operator's real one by omission.
    public CliApplication(
        TextWriter output,
        TextWriter error,
        Func<string, IGovernedTaskService> serviceFactory,
        Func<string, IAgentAdapter> adapterFactory,
        IContextAssembler contextAssembler)
        : this(output, error, (root, _) => serviceFactory(root), adapterFactory, contextAssembler,
            new CognitiveArtifactLoader())
    {
    }

    // The lesson root the factory receives is the one '--lesson-root' selected, or the per-user
    // default when the option was absent.
    public CliApplication(
        TextWriter output,
        TextWriter error,
        Func<string, string, IGovernedTaskService> serviceFactory,
        Func<string, IAgentAdapter> adapterFactory,
        IContextAssembler contextAssembler)
        : this(output, error, serviceFactory, adapterFactory, contextAssembler, new CognitiveArtifactLoader())
    {
    }

    private CliApplication(
        TextWriter output,
        TextWriter error,
        Func<string, string, IGovernedTaskService> serviceFactory,
        Func<string, IAgentAdapter> adapterFactory,
        IContextAssembler contextAssembler,
        CognitiveArtifactLoader artifactLoader)
    {
        _output = output;
        _error = error;
        _serviceFactory = serviceFactory;
        _json = LedgerJson.CreateOptions(indented: true);
        _executor = new CliCommandExecutor(_output, _json);
        _contextCommands = new ContextBriefingCliCommands(
            _executor, contextAssembler, artifactLoader, _json);
        var providerRunRecorder = new ProviderRunRecorder(_error, _json);
        _providerLauncher = new ProviderLauncher(
            adapterFactory, _contextCommands, _executor, _json, new RefusalJournal(), providerRunRecorder,
            () => VerificationHost);
        _commands = CreateCommandCatalog();
    }

    public static CliApplication CreateDefault()
    {
        var runner = new SystemProcessRunner();
        return new CliApplication(
            Console.Out,
            Console.Error,
            (root, lessonRoot) =>
            {
                var reducer = new TaskReducer();
                return new FileGovernedTaskService(
                    root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer,
                    lessonStore: new FileLessonStore(lessonRoot));
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
                await _output.WriteLineAsync(CliHelpText.Text).ConfigureAwait(false);
                return 0;
            }

            var input = CommandLine.Parse(arguments);
            if (input.Command is ["help"])
            {
                await _output.WriteLineAsync(CliHelpText.Text).ConfigureAwait(false);
                return 0;
            }

            var command = NormalizeCommand(input);
            if (!_commands.TryGet(command, out var registration))
            {
                throw new CliUsageException($"Unknown command '{string.Join(' ', input.Command)}'. Use --help.");
            }

            input.EnsureOnlyAllowedOptions(
                command,
                CliCommandOptions.Set([.. registration.AllowedOptions, .. GlobalOptions]));
            var root = Path.GetFullPath(input.Optional("root") ?? DefaultWorkspaceRoot());
            // The lesson store is selected separately from the ledger because it is the one record
            // worth more to the next task than to this one, and that task is often in another
            // repository. A store inside a single ledger root is not a store that crosses roots.
            var lessonRoot = Path.GetFullPath(
                input.Optional("lesson-root")
                ?? (DiscoverLedgerHome() is { } lessonHome
                    ? Path.Combine(lessonHome, "lessons")
                    : FileLessonStore.DefaultRoot()));
            var service = _serviceFactory(root, lessonRoot);
            // Before the work, not after: a launch briefs an agent, and an agent briefed by a stale
            // launcher is the failure this warning exists to catch. Reads are exempt because a stale
            // tool reads a ledger correctly and a warning on every status is a warning nobody sees.
            if (!registration.IsReadOnly &&
                KernelVersion.StalenessWarning(DiscoverLedgerHome()) is { } staleness)
            {
                await _error.WriteLineAsync(staleness).ConfigureAwait(false);
            }

            await registration.ExecuteAsync(
                new CliCommandInvocation(input, service, root, lessonRoot), cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _error.WriteLineAsync("Cancelled.").ConfigureAwait(false);
            return 130;
        }
        catch (AuditFailedException)
        {
            return 1;
        }
        catch (ProviderRunFailedException exception)
        {
            await _error.WriteLineAsync($"error: {exception.Message}").ConfigureAwait(false);
            return 3;
        }
        catch (VerificationFailedException exception)
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

    private static string NormalizeCommand(CommandLine input) =>
        string.Join(' ', input.Command).ToLowerInvariant();

    private CliCommandCatalog CreateCommandCatalog()
    {
        var taskInspection = new TaskInspectionCliCommands(_executor, _json);
        var artifacts = new ArtifactCliCommands(_executor, ProcessStandardInput);
        var retrospectives = new RetrospectiveCliCommands(
            _executor, ProcessStandardInput, () => HarnessTranscriptRoot);
        var lessons = new LessonCliCommands(_executor);
        var audit = new AuditCliCommands(_executor);
        var workItems = new WorkItemCliCommands(_executor, _contextCommands);
        var runs = new RunCliCommands(_executor);
        var preflight = new BatchPreflightCliCommands(
            _executor, _contextCommands, _json);
        var closeout = new CloseoutCliCommands(_executor);
        var cleanup = new TaskCleanupCliCommands(_executor);
        var verification = new VerificationCliCommands(_executor, _json, () => VerificationHost);
        return new CliCommandCatalog(new[] { TaskCliCommands.Open(_executor) }
            .Concat(taskInspection.Registrations())
            .Append(VersionCliCommands.Registration(_executor))
            .Append(ActorCliCommands.Registration(_executor))
            .Concat(artifacts.Registrations())
            .Concat(retrospectives.Registrations())
            .Concat(closeout.Registrations())
            .Concat(cleanup.Registrations())
            .Concat(lessons.Registrations())
            .Append(audit.Registration())
            .Append(_contextCommands.Registration())
            .Concat(ClaimCliCommands.Registrations(_executor))
            .Concat(DecisionCliCommands.Registrations(_executor))
            .Append(EvidenceCliCommands.Registration(_executor))
            .Concat(ChallengeCliCommands.Registrations(_executor))
            .Concat(EscalationCliCommands.Registrations(_executor))
            .Append(AlternativeCliCommands.Registration(_executor))
            .Concat(ConstraintCliCommands.Registrations(_executor))
            .Concat(CoordinatorSessionCliCommands.Registrations(_executor))
            .Append(StageCliCommands.Registration(_executor))
            .Concat(workItems.Registrations())
            .Concat(runs.Registrations())
            .Append(preflight.Registration())
            .Append(verification.Registration())
            .Concat(ProviderCliCommands.Registrations(_providerLauncher)));
    }

    private static string? DiscoverLedgerHome()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, ".ailedger");
            if (Directory.Exists(candidate))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static string DefaultWorkspaceRoot()
    {
        if (DiscoverLedgerHome() is { } home)
        {
            return Path.Combine(home, ".ailedger", "tasks");
        }

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.Combine(localData, "AILedger", "tasks");
    }

    // Accepted by every command, on top of what that command allows. '--root' predates this and is
    // still listed per command; '--lesson-root' is here because the store it selects is read when a
    // task opens and written when one archives, which is not one command's business to declare.
    //
    // '--correlation' is here because a launched agent carries its run id there on every command it
    // issues, and the first commands it issues are 'context build' and 'status'. Those declare
    // no correlation option of their own and would refuse the flag the launcher put in front of it,
    // which would break the agent's first act rather than lose a measurement (C22, D5).
    private static readonly IReadOnlySet<string> GlobalOptions =
        CliCommandOptions.Set("lesson-root", "correlation");


}
