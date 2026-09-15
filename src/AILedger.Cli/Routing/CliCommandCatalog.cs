namespace AILedger.Cli.Routing;

internal sealed class CliCommandCatalog
{
    private readonly IReadOnlyDictionary<string, CliCommandRegistration> _commands;

    public CliCommandCatalog(IEnumerable<CliCommandRegistration> registrations)
    {
        var commands = new Dictionary<string, CliCommandRegistration>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            foreach (var name in registration.Names)
            {
                if (!commands.TryAdd(name, registration))
                {
                    throw new InvalidOperationException($"CLI command '{name}' is registered more than once.");
                }
            }
        }

        _commands = commands;
    }

    public IReadOnlyCollection<string> Names => _commands.Keys.ToArray();

    public bool TryGet(string command, out CliCommandRegistration registration) =>
        _commands.TryGetValue(CliCommandRegistration.Normalize(command), out registration!);
}

internal static class CliCommandOptions
{
    public static IReadOnlySet<string> Set(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
}
