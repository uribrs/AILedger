using AILedger.Core.Contracts;

namespace AILedger.Cli.Routing;

internal static class CliInput
{
    public static ActorId Actor(CommandLine input) => new(input.Required("actor"));

    public static ActorId? Subject(CommandLine input) =>
        OptionalId(input.Optional("subject"), value => new ActorId(value));

    public static ActorId SubjectOrActor(CommandLine input) => Subject(input) ?? Actor(input);

    public static TaskId Task(CommandLine input) => new(input.Required("task"));

    public static RunId? OptionalExistingRun(CommandLine input) =>
        OptionalId(input.Optional("run"), value => new RunId(value));

    public static EventId? Cause(CommandLine input) =>
        OptionalId(input.Optional("cause"), value => new EventId(value));

    public static string Correlation(CommandLine input) => input.CorrelationId;

    public static T? OptionalId<T>(string? value, Func<string, T> factory) where T : struct =>
        string.IsNullOrWhiteSpace(value) ? null : factory(value);

    public static T EnumValue<T>(CommandLine input, string name) where T : struct, Enum =>
        ParseEnum<T>(input.Required(name));

    public static T? OptionalEnumValue<T>(CommandLine input, string name) where T : struct, Enum =>
        input.Optional(name) is { } value ? ParseEnum<T>(value) : null;

    public static T ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value.Replace("-", string.Empty, StringComparison.Ordinal), true, out var parsed) &&
        Enum.IsDefined(parsed)
            ? parsed
            : throw new CliUsageException($"'{value}' is not a valid {typeof(T).Name}.");
}
