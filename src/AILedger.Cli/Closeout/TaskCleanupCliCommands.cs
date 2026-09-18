using System.Text;
using System.Text.Json;
using AILedger.Cli.Routing;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Storage;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Closeout;

internal sealed class TaskCleanupCliCommands(CliCommandExecutor executor)
{
    public IEnumerable<CliCommandRegistration> Registrations()
    {
        yield return new CliCommandRegistration(
            ["task cleanup plan"],
            CliCommandOptions.Set("root", "task", "actor", "output"),
            isReadOnly: false,
            PlanAsync);
        yield return new CliCommandRegistration(
            ["task cleanup apply"],
            CliCommandOptions.Set("root", "task", "actor", "plan"),
            isReadOnly: false,
            ApplyAsync);
    }

    private async Task PlanAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var taskId = Task(invocation.Input);
        _ = Actor(invocation.Input);
        var state = await CliCommandExecutor.RequireStateAsync(invocation, cancellationToken).ConfigureAwait(false);
        var taskDirectory = new TaskWorkspacePathResolver(invocation.LedgerRoot).Resolve(taskId);
        var plan = TaskRetentionPlanner.Plan(
            taskDirectory, state, TaskCloseoutEligibility.Evaluate(state), DateTimeOffset.UtcNow);
        var output = invocation.Input.Optional("output") is { } explicitPath
            ? Path.GetFullPath(explicitPath)
            : Path.Combine(taskDirectory, "cleanup", $"{plan.PlanId}.plan.json");

        var relativeOutput = Path.GetRelativePath(taskDirectory, output);
        var outputIsInsideTask = !Path.IsPathRooted(relativeOutput) &&
                                 relativeOutput is not ".." &&
                                 !relativeOutput.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
        var outputIsInCleanup = relativeOutput.StartsWith($"cleanup{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
        if (outputIsInsideTask && !outputIsInCleanup)
        {
            throw new CliUsageException(
                "Retention plan output inside the task directory must be under its 'cleanup' directory; " +
                $"'{output}' would make the plan permanently unapplicable.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(
            output,
            JsonSerializer.Serialize(plan, LedgerJson.CreateOptions(indented: true)),
            new UTF8Encoding(false),
            cancellationToken).ConfigureAwait(false);

        await executor.WriteJsonAsync(new
        {
            plan.PlanId,
            plan.Task,
            plan.PlannedAtUtc,
            plan.Inputs,
            plan.Totals,
            PlanPath = output,
            Deletions = plan.Entries.Where(entry => entry.Decision == TaskRetentionDecision.Delete).ToArray()
        }).ConfigureAwait(false);
    }

    private async Task ApplyAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var taskId = Task(invocation.Input);
        var actor = Actor(invocation.Input);
        var planPath = Path.GetFullPath(invocation.Input.Required("plan"));
        if (!File.Exists(planPath))
        {
            throw new CliUsageException($"Retention plan '{planPath}' was not found.");
        }

        TaskRetentionPlan plan;
        try
        {
            plan = JsonSerializer.Deserialize<TaskRetentionPlan>(
                       await File.ReadAllTextAsync(planPath, cancellationToken).ConfigureAwait(false),
                       LedgerJson.CreateOptions())
                   ?? throw new InvalidDataException($"Retention plan '{planPath}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Retention plan '{planPath}' is invalid JSON.", exception);
        }

        var state = await CliCommandExecutor.RequireStateAsync(invocation, cancellationToken).ConfigureAwait(false);
        var taskDirectory = new TaskWorkspacePathResolver(invocation.LedgerRoot).Resolve(taskId);
        var result = await TaskRetentionApplier.ApplyAsync(
            taskDirectory, state, TaskCloseoutEligibility.Evaluate(state), plan, actor,
            DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

        await executor.WriteJsonAsync(result).ConfigureAwait(false);
    }
}
