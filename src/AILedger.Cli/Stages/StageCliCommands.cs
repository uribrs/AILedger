using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Stages;

internal static class StageCliCommands
{
    public static CliCommandRegistration Registration(CliCommandExecutor executor) => new(
        ["stage transition"],
        CliCommandOptions.Set(
            "root", "task", "actor", "stage", "without-prerequisites", "reason", "serial-because",
            "cause", "correlation"),
        isReadOnly: false,
        (invocation, cancellationToken) => executor.ExecuteAsync(
            invocation,
            new RequestStageTransitionCommand(
                Actor(invocation.Input), Cause(invocation.Input), Correlation(invocation.Input),
                EnumValue<TaskStage>(invocation.Input, "stage"),
                WithoutPrerequisitesReason: invocation.Input.Optional("without-prerequisites"),
                Reason: invocation.Input.Optional("reason"),
                SerialJustification: OptionalId(
                    invocation.Input.Optional("serial-because"), value => new AlternativeId(value))),
            cancellationToken));
}
