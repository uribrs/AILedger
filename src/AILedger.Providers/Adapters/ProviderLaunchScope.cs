namespace AILedger.Providers.Adapters;

/// <summary>
/// Per-launch provider setup that has to be torn down afterwards, plus the environment it
/// contributes to the child process. Exists so a provider can be isolated from the ambient
/// configuration installed for the operator's own interactive use.
/// </summary>
public sealed class ProviderLaunchScope : IDisposable
{
    public static readonly ProviderLaunchScope None = new(new Dictionary<string, string>(), null);

    private readonly string? _temporaryDirectory;

    public ProviderLaunchScope(IReadOnlyDictionary<string, string> environment, string? temporaryDirectory)
    {
        Environment = environment;
        _temporaryDirectory = temporaryDirectory;
    }

    public IReadOnlyDictionary<string, string> Environment { get; }

    public void Dispose()
    {
        if (_temporaryDirectory is null || !Directory.Exists(_temporaryDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover scratch directory under the system temp path is not worth failing a run.
        }
    }
}
