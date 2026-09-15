using AILedger.Core.Contracts;

namespace AILedger.Cli.Claims;

// The CLI edge of the Claims concept. CliApplication remains the process orchestrator; this class
// owns the vocabulary and translation for claim commands so extending Claims does not add another
// unrelated branch to the application's central command switch.
internal static class ClaimCliCommands
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> OptionsByCommand =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["claim add"] = Options(
                "root", "task", "actor", "id", "statement", "consequence", "from-lesson", "cause", "correlation"),
            ["claim resolve"] = Options(
                "root", "task", "actor", "id", "status", "evidence", "superseded-by", "cause", "correlation")
        };

    public static bool TryGetAllowedOptions(string command, out IReadOnlySet<string> allowedOptions) =>
        OptionsByCommand.TryGetValue(command, out allowedOptions!);

    public static bool TryCreate(string command, CommandLine input, out LedgerCommand? ledgerCommand)
    {
        ledgerCommand = command switch
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
            _ => null
        };

        return ledgerCommand is not null;
    }

    private static ActorId Actor(CommandLine input) => new(input.Required("actor"));
    private static EventId? Cause(CommandLine input) =>
        OptionalId(input.Optional("cause"), value => new EventId(value));

    private static T? OptionalId<T>(string? value, Func<string, T> factory) where T : struct =>
        string.IsNullOrWhiteSpace(value) ? null : factory(value);

    private static T EnumValue<T>(CommandLine input, string name) where T : struct, Enum =>
        ParseEnum<T>(input.Required(name));

    private static T ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value.Replace("-", string.Empty, StringComparison.Ordinal), true, out var parsed) &&
        Enum.IsDefined(parsed)
            ? parsed
            : throw new CliUsageException($"'{value}' is not a valid {typeof(T).Name}.");

    private static IReadOnlySet<string> Options(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
}
