using Xunit.Abstractions;

namespace GovernedTests;

internal sealed class ExecutionSink : IMessageSink
{
    public TaskCompletionSource<ITestAssemblyFinished> Completed { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Errors { get; private set; }

    public bool OnMessage(IMessageSinkMessage message)
    {
        if (message is IFailureInformation failure)
        {
            if (message is ITestFailed failed)
                Console.Error.WriteLine($"FAIL: {failed.Test.DisplayName}");
            else
            {
                Errors++;
                Console.Error.WriteLine($"FRAMEWORK ERROR: {message.GetType().Name}");
            }
            Console.Error.WriteLine(string.Join(Environment.NewLine, failure.Messages));
            Console.Error.WriteLine(string.Join(Environment.NewLine, failure.StackTraces));
        }
        if (message is ITestAssemblyFinished finished)
            Completed.TrySetResult(finished);
        return true;
    }
}
