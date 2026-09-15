using System.Text;
using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Providers.Adapters;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Cli;

internal static class CliApplicationTestSupport
{
    // Read as the rows are written, one JSON object per line, because the question these two tests
    // ask of the journal is whether the launch site wrote anything at all.
    internal static IReadOnlyList<JsonElement> RefusalJournal(string root)
    {
        var path = Path.Combine(root, "T1", "refusals.jsonl");
        return File.Exists(path)
            ? File.ReadAllLines(path).Select(line => JsonDocument.Parse(line).RootElement).ToArray()
            : [];
    }

    internal static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    internal static async Task<IReadOnlyList<LedgerEvent>> HistoryAsync(string root)
    {
        var events = new List<LedgerEvent>();
        await foreach (var @event in Service(root).GetHistoryAsync(new TaskId("T1"), CancellationToken.None))
        {
            events.Add(@event);
        }

        return events;
    }

    // The body arrives on standard input rather than as an option value, because the parser takes
    // any value opening with two dashes as the next option name and a real workflow document starts
    // with a horizontal rule or YAML front matter. That is validated claim C10, and it is why every
    // artifact recorded through the CLI in this file goes through here.
    internal static async Task<int> RecordArtifactAsync(
        CliApplication application,
        IReadOnlyList<string> common,
        string actor,
        string artifactId,
        string kind,
        string? work,
        string? run,
        string body)
    {
        string[] scope = work is null ? [] : ["--work", work];
        string[] producer = run is null ? [] : ["--run", run];
        using var input = new StandardInput(body);
        return await application.RunAsync(
            ["artifact", "record", .. common, "--actor", actor, "--id", artifactId, "--kind", kind,
             "--title", "Governed document", "--body-stdin", .. scope, .. producer],
            CancellationToken.None);
    }

    // The three artifacts filed here are task-wide: a user request names no work item at all, and a
    // prompt contract and an orchestration plan govern the task rather than one item in it. The run
    // that produces them therefore names no work item either. It cannot: a coordinating role is the
    // only role allowed to file these artifacts, and a coordinating role may not hold a run against
    // a work item. Naming one here was refused outright, which is why this helper takes no item id.
    internal static async Task<int[]> RecordExecutionArtifactsAsync(
        CliApplication application,
        IReadOnlyList<string> common)
    {
        var arguments = common.ToList();
        var root = common[arguments.IndexOf("--root") + 1];
        var task = common[arguments.IndexOf("--task") + 1];
        var originalStage = (await Service(root).GetStateAsync(new TaskId(task), CancellationToken.None))!.Stage;
        var exits = new List<int>
        {
            await RecordArtifactAsync(
                application, common, "operator", "A-request", "user-request", null, null,
                "The governed request")
        };
        switch (originalStage)
        {
            case TaskStage.Discovery:
                await CliStageFixture.AdvanceAsync(
                    application, root, task, TaskStage.Research, TaskStage.Design);
                break;
            case TaskStage.Ready:
                await CliStageFixture.BackAsync(application, root, TaskStage.Scope, task);
                await CliStageFixture.BackAsync(application, root, TaskStage.Design, task);
                break;
            case TaskStage.Execution:
                await CliStageFixture.BackAsync(application, root, TaskStage.Design, task);
                break;
            case not TaskStage.Design:
                throw new InvalidOperationException(
                    $"Artifact fixture cannot revise planning documents from stage '{originalStage}'.");
        }
        exits.Add(await application.RunAsync(
            ["run", "start", .. common, "--actor", "operator", "--subject", "operator",
             "--run", "R-artifacts", "--provider", "codex",
             "--session", "artifact-session"], CancellationToken.None));
        exits.Add(await RecordArtifactAsync(
            application, common, "operator", "A-contract", "prompt-contract", null, "R-artifacts",
            ArtifactCommands.Body));
        exits.Add(await RecordArtifactAsync(
            application, common, "operator", "A-plan", "orchestration-plan", null, "R-artifacts",
            ArtifactCommands.PlanBody));
        exits.Add(await application.RunAsync(
            ["run", "complete", .. common, "--actor", "operator", "--run", "R-artifacts",
             "--status", "completed", "--session", "artifact-session"], CancellationToken.None));
        if (originalStage is TaskStage.Ready or TaskStage.Execution)
        {
            await CliStageFixture.AdvanceAsync(
                application, root, task, TaskStage.Scope, TaskStage.Ready);
        }
        if (originalStage is TaskStage.Execution)
        {
            await CliStageFixture.ToExecutionAsync(application, root, task);
        }
        return [.. exits];
    }

    // The manifest reaches the agent as the CLI wrote it, so it is read back the same way.
    internal static readonly JsonSerializerOptions ManifestJson = LedgerJson.CreateOptions();

    internal sealed class CapturingAdapter : IAgentAdapter
    {
        public List<AgentLaunchRequest> Requests { get; } = [];

        public string Provider => "codex";

        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult("test");

        public Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new AgentRunResult(
                request.RunId, Provider, "session-1", AgentRunStatus.Completed,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), 0, "done", [], string.Empty,
                "test", [], false, null));
        }
    }

    // Stands in for the live agents that closed their own runs despite being told not to: it tries
    // the completion from inside the run, using the subject identity it was launched under.
    internal sealed class SelfCompletingAdapter(string ledgerRoot) : IAgentAdapter
    {
        public static string? LastAttemptError { get; private set; }

        public string Provider => "codex";

        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult("test");

        public async Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
        {
            LastAttemptError = null;
            try
            {
                await Service(ledgerRoot).ExecuteAsync(
                    request.TaskId,
                    new CompleteRunCommand(
                        request.ActorId, null, "agent-inside-run", request.RunId,
                        AgentRunStatus.Completed, "agent-session"),
                    cancellationToken);
            }
            catch (GovernanceException exception)
            {
                LastAttemptError = exception.Message;
            }

            return new AgentRunResult(
                request.RunId, Provider, "session-1", AgentRunStatus.Completed,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), 0, "done", [], string.Empty,
                "test", [], false, null);
        }
    }

    // Returns zero so it can sit in an exits list beside the commands it precedes; the helper
    // throws rather than returning a code when the brief itself is refused.
    internal static async Task<int> BriefAsync(string[] common)
    {
        await ContextBrief.BuildAsync(common[1], common[3], common[5]);
        return 0;
    }

    internal static CliApplication Create(TextWriter output, TextWriter error) => new(
        output,
        error,
        Service,
        _ => throw new InvalidOperationException("Provider adapter is not used by this test."),
        new ContextAssembler());

    internal static async Task WaitUntilAsync(Func<bool> condition, string failureMessage)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail(failureMessage);
    }

    internal sealed class ThreadSafeStringWriter : TextWriter
    {
        private readonly StringBuilder _buffer = new();
        private readonly object _gate = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override Task WriteLineAsync(string? value)
        {
            lock (_gate)
            {
                _buffer.AppendLine(value);
            }

            return Task.CompletedTask;
        }

        public string GetText()
        {
            lock (_gate)
            {
                return _buffer.ToString();
            }
        }
    }

    internal static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }

    internal static string FindCognitiveRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "cognitive");
            if (File.Exists(Path.Combine(candidate, "manifest.json")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Test could not locate the cognitive root.");
    }

    internal sealed class CancellingAdapter(CancellationTokenSource cancellation) : IAgentAdapter
    {
        public string Provider => "codex";

        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult("test");

        public Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromResult(new AgentRunResult(
                request.RunId, Provider, "cancelled-session", AgentRunStatus.Cancelled,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), -1, null, [], string.Empty,
                "test", [], false, "Provider run was cancelled."));
        }
    }

    internal sealed class SuccessfulCancellingAdapter(CancellationTokenSource cancellation) : IAgentAdapter
    {
        public string Provider => "codex";

        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult("test");

        public Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromResult(new AgentRunResult(
                request.RunId, Provider, "session-1", AgentRunStatus.Completed,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), 0, "done", [], string.Empty,
                "test", [], false, null));
        }
    }

    internal sealed class FixedResultAdapter(AgentRunStatus status) : IAgentAdapter
    {
        public string Provider => "codex";

        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) =>
            Task.FromResult("test");

        public Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentRunResult(
                request.RunId, Provider, "session-1", status,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1),
                status == AgentRunStatus.Failed ? 1 : 0, null, [], string.Empty, "test", [], false, "failed"));
    }

    internal sealed class CompletionFailureService(
        IGovernedTaskService inner,
        int failuresBeforeSuccess) : IGovernedTaskService
    {
        public int CompletionAttempts { get; private set; }

        public Task<CommandOutcome> ExecuteAsync(
            TaskId taskId,
            LedgerCommand command,
            CancellationToken cancellationToken)
        {
            if (command is CompleteRunCommand && ++CompletionAttempts <= failuresBeforeSuccess)
            {
                throw new IOException("Injected pre-commit task mutation lock failure.");
            }

            return inner.ExecuteAsync(taskId, command, cancellationToken);
        }

        public Task<GovernedTaskState?> GetStateAsync(TaskId taskId, CancellationToken cancellationToken) =>
            inner.GetStateAsync(taskId, cancellationToken);

        public IAsyncEnumerable<LedgerEvent> GetHistoryAsync(
            TaskId taskId,
            CancellationToken cancellationToken) =>
            inner.GetHistoryAsync(taskId, cancellationToken);
    }
}
