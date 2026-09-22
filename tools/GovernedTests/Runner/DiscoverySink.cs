using Xunit.Abstractions;

namespace GovernedTests;

internal sealed class DiscoverySink : IMessageSink
{
    public List<ITestCase> Tests { get; } = [];
    public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool HasErrors { get; private set; }

    public bool OnMessage(IMessageSinkMessage message)
    {
        if (message is ITestCaseDiscoveryMessage discovered)
            Tests.Add(discovered.TestCase);
        if (message is IFailureInformation failure)
        {
            HasErrors = true;
            Console.Error.WriteLine(string.Join(Environment.NewLine, failure.Messages));
        }
        if (message is IDiscoveryCompleteMessage)
            Completed.TrySetResult();
        return true;
    }
}
