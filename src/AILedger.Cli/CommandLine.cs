namespace AILedger.Cli;

internal sealed class CommandLine
{
    private readonly Dictionary<string, List<string>> _options = new(StringComparer.OrdinalIgnoreCase);

    private CommandLine(IReadOnlyList<string> command) => Command = command;

    public IReadOnlyList<string> Command { get; }

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
            var optionValue = separator < 0
                ? ReadFollowingValue(arguments, ref index, name)
                : value[(separator + 1)..];
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
