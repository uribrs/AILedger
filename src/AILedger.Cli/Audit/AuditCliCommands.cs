using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Cli.Audit;

internal sealed class AuditCliCommands(CliCommandExecutor executor)
{
    public CliCommandRegistration Registration() => new(
        ["audit"],
        CliCommandOptions.Set("root", "recent", "quiet"),
        isReadOnly: true,
        ExecuteAsync);

    private async Task ExecuteAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var input = invocation.Input;
        var root = invocation.LedgerRoot;
        var quiet = input.Flag("quiet");
        var recent = double.TryParse(input.Optional("recent"), out var days) ? days : (double?)null;
        if (!Directory.Exists(root))
        {
            return;
        }

        var findings = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(root).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            if (recent is { } window)
            {
                var newest = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Select(File.GetLastWriteTimeUtc)
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max();
                if ((DateTime.UtcNow - newest).TotalDays > window)
                {
                    continue;
                }
            }

            GovernedTaskState? state;
            try
            {
                state = await invocation.Service.GetStateAsync(new TaskId(name), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (GovernanceException exception)
            {
                findings.Add($"{name}: does not replay — {exception.Message}");
                continue;
            }

            if (state is null || state.Stage == TaskStage.Archive)
            {
                continue;
            }

            var activeRuns = state.Runs.Values.Count(run => run.Status == AgentRunStatus.Active);
            var openEscalations = state.Escalations.Values.Count(escalation => escalation.Status == EscalationStatus.Open);
            var liveWork = state.WorkItems.Values.Count(item =>
                item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Abandoned or WorkItemStatus.Stale));
            if (activeRuns == 0 && openEscalations == 0 && liveWork == 0 && state.WorkItems.Count > 0)
            {
                findings.Add(
                    $"{name}: stage '{state.Stage}' with no live work, no active run and no open escalation — " +
                    $"finished but never closed, so its {state.LessonMarks.Count} mark(s) minted nothing. " +
                    "Walk it to archive or say why it stays open");
            }
        }

        if (findings.Count == 0)
        {
            if (!quiet)
            {
                await executor.WriteLineAsync("Close-out is clean.").ConfigureAwait(false);
            }
            return;
        }

        foreach (var finding in findings)
        {
            await executor.WriteLineAsync(finding).ConfigureAwait(false);
        }

        throw new AuditFailedException();
    }
}

internal sealed class AuditFailedException : Exception;
