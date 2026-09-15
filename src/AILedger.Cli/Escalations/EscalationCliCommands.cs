using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Escalations;

internal static class EscalationCliCommands
{
    public static IEnumerable<CliCommandRegistration> Registrations(CliCommandExecutor executor)
    {
        yield return new CliCommandRegistration(
            ["escalation raise"],
            CliCommandOptions.Set(
                "root", "task", "actor", "id", "kind", "question", "work", "option", "recommend", "evidence",
                "cause", "correlation"),
            isReadOnly: false,
            (invocation, cancellationToken) => executor.ExecuteAsync(
                invocation,
                new RaiseEscalationCommand(
                    Actor(invocation.Input), Cause(invocation.Input), Correlation(invocation.Input),
                    new EscalationId(invocation.Input.Required("id")),
                    EnumValue<EscalationKind>(invocation.Input, "kind"),
                    invocation.Input.Required("question"),
                    OptionalId(invocation.Input.Optional("work"), value => new WorkItemId(value)),
                    invocation.Input.Many("option"), invocation.Input.Optional("recommend"),
                    invocation.Input.Many("evidence").Select(value => new EvidenceId(value)).ToArray()),
                cancellationToken));

        yield return new CliCommandRegistration(
            ["escalation resolve"],
            CliCommandOptions.Set(
                "root", "task", "actor", "id", "status", "resolution", "cause", "correlation"),
            isReadOnly: false,
            (invocation, cancellationToken) => executor.ExecuteAsync(
                invocation,
                new ResolveEscalationCommand(
                    Actor(invocation.Input), Cause(invocation.Input), Correlation(invocation.Input),
                    new EscalationId(invocation.Input.Required("id")),
                    EnumValue<EscalationStatus>(invocation.Input, "status"),
                    invocation.Input.Optional("resolution")),
                cancellationToken));
    }
}
