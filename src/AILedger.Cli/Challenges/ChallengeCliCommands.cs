using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Challenges;

internal static class ChallengeCliCommands
{
    public static IEnumerable<CliCommandRegistration> Registrations(CliCommandExecutor executor)
    {
        yield return new CliCommandRegistration(
            ["challenge raise"],
            CliCommandOptions.Set(
                "root", "task", "actor", "id", "target-type", "target-id", "reason", "evidence",
                "cause", "correlation"),
            isReadOnly: false,
            (invocation, cancellationToken) => executor.ExecuteAsync(
                invocation,
                new RaiseChallengeCommand(
                    Actor(invocation.Input), Cause(invocation.Input), Correlation(invocation.Input),
                    new ChallengeId(invocation.Input.Required("id")),
                    invocation.Input.Required("target-type"), invocation.Input.Required("target-id"),
                    invocation.Input.Required("reason"),
                    invocation.Input.Many("evidence").Select(value => new EvidenceId(value)).ToArray()),
                cancellationToken));

        yield return new CliCommandRegistration(
            ["challenge dispose"],
            CliCommandOptions.Set("root", "task", "actor", "id", "status", "cause", "correlation"),
            isReadOnly: false,
            (invocation, cancellationToken) => executor.ExecuteAsync(
                invocation,
                new DisposeChallengeCommand(
                    Actor(invocation.Input), Cause(invocation.Input), Correlation(invocation.Input),
                    new ChallengeId(invocation.Input.Required("id")),
                    EnumValue<ChallengeStatus>(invocation.Input, "status")),
                cancellationToken));
    }
}
