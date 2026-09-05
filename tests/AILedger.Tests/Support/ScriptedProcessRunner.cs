using AILedger.Core.Contracts;

namespace AILedger.Tests.Support;

internal sealed class ScriptedProcessRunner : IProcessRunner
{
    private readonly Queue<Func<ProcessInvocation, ScriptedProcessResult>> _responses = new();

    public List<ProcessInvocation> Invocations { get; } = [];

    public ScriptedProcessRunner Enqueue(
        int exitCode,
        IReadOnlyList<string>? standardOutput = null,
        IReadOnlyList<string>? standardError = null)
    {
        _responses.Enqueue(_ => new ScriptedProcessResult(exitCode, standardOutput ?? [], standardError ?? []));
        return this;
    }

    public ScriptedProcessRunner Enqueue(Func<ProcessInvocation, ScriptedProcessResult> response)
    {
        _responses.Enqueue(response);
        return this;
    }

    public async Task<ProcessExit> RunAsync(
        ProcessInvocation invocation,
        Func<string, CancellationToken, ValueTask> onStandardOutputLine,
        Func<string, CancellationToken, ValueTask> onStandardErrorLine,
        CancellationToken cancellationToken)
    {
        Invocations.Add(invocation);
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No scripted process response remains.");
        }

        var response = _responses.Dequeue()(invocation);
        foreach (var line in response.StandardOutput)
        {
            await onStandardOutputLine(line, cancellationToken);
        }

        foreach (var line in response.StandardError)
        {
            await onStandardErrorLine(line, cancellationToken);
        }

        return new ProcessExit(
            response.ExitCode,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(1));
    }
}

internal sealed record ScriptedProcessResult(
    int ExitCode,
    IReadOnlyList<string> StandardOutput,
    IReadOnlyList<string> StandardError);
