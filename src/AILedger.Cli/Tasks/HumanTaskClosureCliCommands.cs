using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.TaskClosure;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Tasks;

internal sealed class HumanTaskClosureCliCommands(CliCommandExecutor executor,
    Func<IHumanClosureConfirmation> confirmation)
{
    internal CliCommandRegistration Registration() => new(
        ["task close"], CliCommandOptions.Set("root", "task", "actor", "human-override", "reason"),
        isReadOnly: false, CloseAsync);

    private async Task CloseAsync(CliCommandInvocation invocation, CancellationToken token)
    {
        if (!invocation.Input.Flag("human-override"))
            throw new CliUsageException("task close requires --human-override, --reason, and explicit local human confirmation.");
        var reason = invocation.Input.Required("reason").Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000 || reason.Any(char.IsControl))
            throw new CliUsageException("A reason of 1–2000 characters without control characters is required.");
        var state = await CliCommandExecutor.RequireStateAsync(invocation, token).ConfigureAwait(false);
        var actor = Actor(invocation.Input);
        if (!state.Roles.TryGetValue(actor, out var assignment) || assignment.Role != RoleKind.Operator)
            throw new GovernanceException("Human task closure requires an operator actor and local human confirmation.");
        if (state.HumanClosure is not null || state.Stage == TaskStage.Archive)
            throw new GovernanceException("Task is already closed; inspect status for its recorded outcome.");
        var admission = await confirmation().ConfirmAsync(state, actor, reason, invocation.LedgerRoot, token).ConfigureAwait(false);
        var command = new CloseTaskByHumanCommand(actor, null, Guid.NewGuid().ToString("N"), reason)
        { Admission = admission };
        var result = await invocation.Service.ExecuteAsync(state.TaskId, command, token).ConfigureAwait(false);
        await executor.WriteJsonAsync(new
        {
            result.State.TaskId, result.State.Version, result.State.Stage,
            result.State.HumanClosure,
            ProcessTermination = result.State.HumanClosure!.InterruptedRuns.Count == 0 ? "not_needed" : "unconfirmed",
            Detail = "Task closed successfully by human override. Findings and check results are preserved. " +
                "New launchers cancel interrupted providers; older launchers require external termination."
        }).ConfigureAwait(false);
    }
}
