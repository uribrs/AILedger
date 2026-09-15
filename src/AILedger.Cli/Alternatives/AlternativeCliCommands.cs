using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Alternatives;

internal static class AlternativeCliCommands
{
    public static CliCommandRegistration Registration(CliCommandExecutor executor) => new(
        ["alternative record"],
        CliCommandOptions.Set(
            "root", "task", "actor", "id", "statement", "rejected-because", "replaced-by",
            "from-lesson", "cause", "correlation"),
        isReadOnly: false,
        (invocation, cancellationToken) => executor.ExecuteAsync(
            invocation,
            new RecordAlternativeCommand(
                Actor(invocation.Input), Cause(invocation.Input), Correlation(invocation.Input),
                new AlternativeId(invocation.Input.Required("id")),
                invocation.Input.Required("statement"), invocation.Input.Required("rejected-because"),
                OptionalId(invocation.Input.Optional("replaced-by"), value => new DecisionId(value)),
                OptionalId(invocation.Input.Optional("from-lesson"), value => new LessonId(value))),
            cancellationToken));
}
