using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.CoordinatorSessions;

internal static class CoordinatorSessionCliCommands
{
    public static IEnumerable<CliCommandRegistration> Registrations(CliCommandExecutor executor)
    {
        yield return new CliCommandRegistration(
            ["session start"],
            CliCommandOptions.Set(
                "root", "task", "actor", "id", "harness", "harness-session", "cause", "correlation"),
            isReadOnly: false,
            (invocation, cancellationToken) => executor.ExecuteAsync(
                invocation,
                new StartCoordinatorSessionCommand(
                    Actor(invocation.Input), Cause(invocation.Input), Correlation(invocation.Input),
                    new CoordinatorSessionId(invocation.Input.Required("id")),
                    invocation.Input.Required("harness"), invocation.Input.Optional("harness-session")),
                cancellationToken));

        yield return new CliCommandRegistration(
            ["session complete"],
            CliCommandOptions.Set("root", "task", "actor", "id", "cause", "correlation"),
            isReadOnly: false,
            (invocation, cancellationToken) => executor.ExecuteAsync(
                invocation,
                new CompleteCoordinatorSessionCommand(
                    Actor(invocation.Input), Cause(invocation.Input), Correlation(invocation.Input),
                    new CoordinatorSessionId(invocation.Input.Required("id"))),
                cancellationToken));
    }
}
