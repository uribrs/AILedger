using Xunit.Abstractions;

namespace GovernedTests;

// xUnit owns discovery, theories, fixtures, output helpers and disposal. These are execution
// options, not a second implementation of its test lifecycle.
internal sealed class FrameworkOptions : ITestFrameworkDiscoveryOptions, ITestFrameworkExecutionOptions
{
    private readonly Dictionary<string, object?> values = new()
    {
        ["xunit.execution.DisableParallelization"] = true,
        ["xunit.discovery.PreEnumerateTheories"] = false
    };

    public T GetValue<T>(string name) => values.TryGetValue(name, out var value) ? (T)value! : default!;

    public void SetValue<T>(string name, T value) => values[name] = value;
}
