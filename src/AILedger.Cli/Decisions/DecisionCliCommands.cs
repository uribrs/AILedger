using AILedger.Core.Contracts;

namespace AILedger.Cli.Decisions;

// Owns Decision command vocabulary and translation at the process boundary. CliApplication only
// orchestrates execution; extending Decisions no longer grows its central dispatch switch.
internal static class DecisionCliCommands
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> OptionsByCommand =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["decision propose"] = Options(
                "root", "task", "actor", "id", "statement", "rationale", "depends-on", "supersedes",
                "from-lesson", "cause", "correlation"),
            ["decision resolve"] = Options(
                "root", "task", "actor", "id", "status", "cause", "correlation")
        };

    public static bool TryGetAllowedOptions(string command, out IReadOnlySet<string> allowedOptions) =>
        OptionsByCommand.TryGetValue(command, out allowedOptions!);

    public static bool TryCreate(string command, CommandLine input, out LedgerCommand? ledgerCommand)
    {
        ledgerCommand = command switch
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
