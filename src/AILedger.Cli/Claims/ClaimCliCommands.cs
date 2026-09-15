using AILedger.Core.Contracts;
using AILedger.Cli.Routing;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Claims;

// The CLI edge of the Claims concept. CliApplication remains the process orchestrator; this class
// owns the vocabulary and translation for claim commands so extending Claims does not add another
// unrelated branch to the application's central command switch.
internal static class ClaimCliCommands
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> CommandOptions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["claim add"] = CliCommandOptions.Set(
                "root", "task", "actor", "id", "statement", "consequence", "from-lesson", "cause", "correlation"),
            ["claim resolve"] = CliCommandOptions.Set(
                "root", "task", "actor", "id", "status", "evidence", "superseded-by", "cause", "correlation")
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
            "claim add" => new AddClaimCommand(
                Actor(input), Cause(input), input.CorrelationId, new ClaimId(input.Required("id")),
                input.Required("statement"), input.Optional("consequence"),
                OptionalId(input.Optional("from-lesson"), value => new LessonId(value))),
            "claim resolve" => new ResolveClaimCommand(
                Actor(input), Cause(input), input.CorrelationId, new ClaimId(input.Required("id")),
                EnumValue<ClaimStatus>(input, "status"),
                input.Many("evidence").Select(value => new EvidenceId(value)).ToArray(),
                OptionalId(input.Optional("superseded-by"), value => new ClaimId(value))),
            _ => throw new InvalidOperationException($"Claim command '{command}' is not registered.")
        };
}
