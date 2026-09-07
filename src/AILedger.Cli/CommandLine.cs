namespace AILedger.Cli;

internal sealed class CommandLine
{
    // Options that are present or absent rather than set to something. Every other option consumes
    // the argument after it, so a bare '--follow' would otherwise be refused for missing a value.
    private static readonly IReadOnlySet<string> ValuelessOptions =
        new HashSet<string>(["follow", "body-stdin", "json"], StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<string>> _options = new(StringComparer.OrdinalIgnoreCase);

    private CommandLine(IReadOnlyList<string> command) => Command = command;

    public IReadOnlyList<string> Command { get; }

    // One invocation is one logical operation, so every event it emits shares a
    // correlation ID. Computed once; a provider launch would otherwise correlate
    // its run.started and run.completed events differently.
    public string CorrelationId =>
        _correlationId ??= Optional("correlation") is { } supplied && !string.IsNullOrWhiteSpace(supplied)
            ? supplied
            : Guid.NewGuid().ToString("N");

    private string? _correlationId;

    public static CommandLine Parse(IReadOnlyList<string> arguments)
    {
        var command = new List<string>();
        var parsed = new CommandLine(command);

        for (var index = 0; index < arguments.Count; index++)
        {
            var value = arguments[index];
            if (!value.StartsWith("--", StringComparison.Ordinal))
            {
                command.Add(value);
                continue;
            }

            var separator = value.IndexOf('=');
            var name = separator < 0 ? value[2..] : value[2..separator];
            var optionValue = separator >= 0
                ? value[(separator + 1)..]
                : ValuelessOptions.Contains(name)
                    ? bool.TrueString
                    : ReadFollowingValue(arguments, ref index, name);
            parsed.Add(name, optionValue);
        }

        return parsed;
    }

    public string Required(string name)
    {
        var value = Optional(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new CliUsageException($"Missing required option '--{name}'.")
            : value;
    }

    public string? Optional(string name) =>
        _options.TryGetValue(name, out var values) ? values[^1] : null;

    public IReadOnlyList<string> Many(string name) =>
        _options.TryGetValue(name, out var values) ? values : [];

    public bool Has(string name) => _options.ContainsKey(name);

    // A valueless option is true by its presence. '--follow=false' is the one way to say otherwise,
    // so a script can build the argument list without branching on whether to include the option.
    public bool Flag(string name) =>
        Optional(name) is { } value && !string.Equals(value, bool.FalseString, StringComparison.OrdinalIgnoreCase);

    public void EnsureOnlyAllowedOptions(string command, IReadOnlySet<string> allowedOptions)
    {
        var unknownOptions = _options.Keys
            .Where(name => !allowedOptions.Contains(name))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(name => $"--{name}")
            .ToArray();
        if (unknownOptions.Length > 0)
        {
            throw new CliUsageException(
                $"Unknown option(s) for command '{command}': {string.Join(", ", unknownOptions)}.");
        }
    }

    private static string ReadFollowingValue(IReadOnlyList<string> arguments, ref int index, string name)
    {
        if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliUsageException($"Option '--{name}' requires a value.");
        }

        return arguments[++index];
    }

    private void Add(string name, string value)
    {
        if (!_options.TryGetValue(name, out var values))
        {
            values = [];
            _options.Add(name, values);
        }

        values.Add(value);
    }
}

internal sealed class CliUsageException(string message) : Exception(message);
