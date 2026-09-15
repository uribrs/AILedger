using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Tasks;

internal static class TaskCliCommands
{
    public static CliCommandRegistration Open(CliCommandExecutor executor) => new(
        ["task open"],
        CliCommandOptions.Set(
            "root", "task", "actor", "title", "goal", "tag", "cause", "correlation"),
        isReadOnly: false,
        (invocation, cancellationToken) => executor.ExecuteAsync(
            invocation,
            new OpenTaskCommand(
                Actor(invocation.Input), Cause(invocation.Input), Correlation(invocation.Input),
                Task(invocation.Input), invocation.Input.Required("title"), invocation.Input.Required("goal"),
                Tags: invocation.Input.Many("tag")),
            cancellationToken));
}
