using AILedger.Core.Contracts;

namespace AILedger.Cli.Routing;

internal sealed class CliCommandRegistration
{
    private readonly Func<CliCommandInvocation, CancellationToken, Task> _execute;

    public CliCommandRegistration(
        IEnumerable<string> names,
        IEnumerable<string> allowedOptions,
        bool isReadOnly,
        Func<CliCommandInvocation, CancellationToken, Task> execute)
    {
        Names = names.Select(Normalize).Distinct(StringComparer.Ordinal).ToArray();
        if (Names.Count == 0)
        {
            throw new ArgumentException("A CLI command registration requires at least one name.", nameof(names));
        }

        AllowedOptions = new HashSet<string>(allowedOptions, StringComparer.OrdinalIgnoreCase);
        IsReadOnly = isReadOnly;
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public IReadOnlyList<string> Names { get; }
    public IReadOnlySet<string> AllowedOptions { get; }
    public bool IsReadOnly { get; }

    public Task ExecuteAsync(CliCommandInvocation invocation, CancellationToken cancellationToken) =>
        _execute(invocation, cancellationToken);

    internal static string Normalize(string command) => command.Trim().ToLowerInvariant();
}

internal sealed record CliCommandInvocation(
    CommandLine Input,
    IGovernedTaskService Service,
    string LedgerRoot,
    string LessonRoot);
