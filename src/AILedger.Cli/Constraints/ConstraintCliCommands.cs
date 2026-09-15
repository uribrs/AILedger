using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Constraints;

internal static class ConstraintCliCommands
{
    public static IEnumerable<CliCommandRegistration> Registrations(CliCommandExecutor executor)
    {
        yield return new CliCommandRegistration(
            ["constraint add"],
            CliCommandOptions.Set(
                "root", "task", "actor", "id", "statement", "source", "scope", "cause", "correlation"),
            isReadOnly: false,
            (invocation, cancellationToken) => executor.ExecuteAsync(
                invocation,
                new AddConstraintCommand(
                    Actor(invocation.Input), Cause(invocation.Input), Correlation(invocation.Input),
                    new ConstraintId(invocation.Input.Required("id")), invocation.Input.Required("statement"),
                    invocation.Input.Required("source"), invocation.Input.Many("scope")),
                cancellationToken));

        yield return new CliCommandRegistration(
            ["constraint supersede"],
            CliCommandOptions.Set("root", "task", "actor", "id", "cause", "correlation"),
            isReadOnly: false,
            (invocation, cancellationToken) => executor.ExecuteAsync(
                invocation,
                new SupersedeConstraintCommand(
                    Actor(invocation.Input), Cause(invocation.Input), Correlation(invocation.Input),
                    new ConstraintId(invocation.Input.Required("id"))),
                cancellationToken));
    }
}
