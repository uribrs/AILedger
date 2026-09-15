using AILedger.Core.Contracts;
using AILedger.Cli.Routing;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Decisions;

// Owns Decision command vocabulary and translation at the process boundary. CliApplication only
// orchestrates execution; extending Decisions no longer grows its central dispatch switch.
internal static class DecisionCliCommands
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> CommandOptions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["decision propose"] = CliCommandOptions.Set(
                "root", "task", "actor", "id", "statement", "rationale", "depends-on", "supersedes",
                "from-lesson", "cause", "correlation"),
            ["decision resolve"] = CliCommandOptions.Set(
                "root", "task", "actor", "id", "status", "cause", "correlation")
        };

    public static IEnumerable<CliCommandRegistration> Registrations(CliCommandExecutor executor) =>
        CommandOptions.Select(command => new CliCommandRegistration(
            [command.Key],
            command.Value,
            isReadOnly: false,
            (invocation, cancellationToken) => executor.ExecuteAsync(
                invocation,
                Create(command.Key, invocation.Input),
                cancellationToken)));

    private static LedgerCommand Create(string command, CommandLine input) =>
        command switch
        {
            "decision propose" => new ProposeDecisionCommand(
                Actor(input), Cause(input), input.CorrelationId, new DecisionId(input.Required("id")),
                input.Required("statement"), input.Required("rationale"),
                input.Many("depends-on").Select(value => new ClaimId(value)).ToArray(),
                OptionalId(input.Optional("supersedes"), value => new DecisionId(value)),
                OptionalId(input.Optional("from-lesson"), value => new LessonId(value))),
            "decision resolve" => new ResolveDecisionCommand(
                Actor(input), Cause(input), input.CorrelationId, new DecisionId(input.Required("id")),
                EnumValue<DecisionStatus>(input, "status")),
            _ => throw new InvalidOperationException($"Decision command '{command}' is not registered.")
        };
}
