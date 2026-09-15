using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Runs;

internal sealed class RunCliCommands(CliCommandExecutor executor)
{
    public IEnumerable<CliCommandRegistration> Registrations()
    {
        yield return new CliCommandRegistration(
            ["run start"],
            CliCommandOptions.Set(
                "root", "task", "actor", "subject", "run", "work", "provider", "session",
                "coordinator-session", "cause", "correlation"),
            isReadOnly: false,
            (invocation, cancellationToken) => executor.ExecuteAsync(
                invocation, CreateStart(invocation.Input), cancellationToken));
        yield return new CliCommandRegistration(
            ["run complete"],
            CliCommandOptions.Set(
                "root", "task", "actor", "run", "status", "session", "cause", "correlation"),
            isReadOnly: false,
            (invocation, cancellationToken) => executor.ExecuteAsync(
                invocation,
                new CompleteRunCommand(
                    Actor(invocation.Input), Cause(invocation.Input), Correlation(invocation.Input),
                    new RunId(invocation.Input.Required("run")),
                    EnumValue<AgentRunStatus>(invocation.Input, "status"),
                    invocation.Input.Optional("session")),
                cancellationToken));
    }

    private static StartRunCommand CreateStart(CommandLine input) => new(
        Actor(input), Cause(input), Correlation(input), SafeRunId(input.Required("run")),
        OptionalId(input.Optional("work"), value => new WorkItemId(value)),
        input.Required("provider"), input.Optional("session"), input.Optional("model"),
        null, null, Subject(input), null,
        input.Optional("without-brief"),
        OptionalId(input.Optional("with-stale-brief"), value => new EvidenceId(value)),
        OptionalId(input.Optional("coordinator-session"), value => new CoordinatorSessionId(value)));

    private static RunId SafeRunId(string value) =>
        value is not ("." or "..") && !value.StartsWith('-') && value.All(IsSafeRunIdCharacter)
            ? new RunId(value)
            : throw new CliUsageException(
                $"Run id '{value}' is not allowed. A run id may contain only letters, digits, '-', '_' " +
                "and '.', may not begin with '-', and may not be '.' or '..'.");

    private static bool IsSafeRunIdCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.';
}
