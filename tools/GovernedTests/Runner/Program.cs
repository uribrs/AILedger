using System.Reflection;
using Xunit.Sdk;

namespace GovernedTests;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            Console.Error.WriteLine("Usage: GovernedTests.dll <test-assembly> [name-contains]");
            return 2;
        }

        try
        {
            return await RunAsync(Path.GetFullPath(args[0]), args.ElementAtOrDefault(1));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"RUNNER ERROR: {exception}");
            return 2;
        }
    }

    private static async Task<int> RunAsync(string assemblyPath, string? filter)
    {
        var assembly = Assembly.LoadFrom(assemblyPath);
        var discovery = new DiscoverySink();
        using var framework = new XunitTestFramework(discovery);
        using var discoverer = framework.GetDiscoverer(new ReflectionAssemblyInfo(assembly));
        var options = new FrameworkOptions();
        discoverer.Find(false, discovery, options);
        await discovery.Completed.Task.WaitAsync(TimeSpan.FromMinutes(2));
        var tests = discovery.Tests.Where(test => filter is null ||
            test.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (discovery.HasErrors || tests.Length == 0)
        {
            Console.Error.WriteLine("RUNNER ERROR: discovery failed or no tests matched.");
            return 2;
        }

        var execution = new ExecutionSink();
        using var executor = framework.GetExecutor(assembly.GetName());
        executor.RunTests(tests, execution, options);
        var result = await execution.Completed.Task.WaitAsync(TimeSpan.FromMinutes(10));
        Console.WriteLine($"{assembly.GetName().Name}: total={result.TestsRun}, " +
            $"failed={result.TestsFailed}, skipped={result.TestsSkipped}, errors={execution.Errors}");
        return result.TestsRun == 0 ? 2 : result.TestsFailed > 0 || execution.Errors > 0 ? 1 : 0;
    }
}
