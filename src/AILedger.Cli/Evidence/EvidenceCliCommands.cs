using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Evidence;

internal static class EvidenceCliCommands
{
    public static CliCommandRegistration Registration(CliCommandExecutor executor) => new(
        ["evidence add"],
        CliCommandOptions.Set(
            "root", "task", "actor", "id", "source-type", "citation", "summary", "supports", "refutes",
            "cause", "correlation"),
        isReadOnly: false,
        (invocation, cancellationToken) => executor.ExecuteAsync(
            invocation,
            new AddEvidenceCommand(
                Actor(invocation.Input), Cause(invocation.Input), Correlation(invocation.Input),
                new EvidenceId(invocation.Input.Required("id")),
                invocation.Input.Required("source-type"),
                invocation.Input.Required("citation"),
                invocation.Input.Required("summary"),
                invocation.Input.Many("supports").Select(value => new ClaimId(value)).ToArray(),
                invocation.Input.Many("refutes").Select(value => new ClaimId(value)).ToArray()),
            cancellationToken));
}
