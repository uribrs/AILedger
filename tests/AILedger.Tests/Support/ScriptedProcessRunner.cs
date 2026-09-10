using System.Text;
using AILedger.Core.Contracts;
using AILedger.Providers.Process;

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
        TruncatedLineTally? tally,
        CancellationToken cancellationToken)
    {
        Invocations.Add(invocation);
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No scripted process response remains.");
        }

        var response = _responses.Dequeue()(invocation);
        if (response.DrainRealStreams)
        {
            // Handed to the production drain instead of delivered line by line, so the lines are
            // really cut, really counted and really charged to the receiver. A claim about what the
            // adapter does at a boundary the drain enforces cannot rest on pre-cut strings.
            return new ProcessExit(
                response.ExitCode,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                await DrainAsync(response.StandardOutput, onStandardOutputLine, tally, cancellationToken) +
                await DrainAsync(response.StandardError, onStandardErrorLine, tally, cancellationToken));
        }

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
            DateTimeOffset.UnixEpoch.AddSeconds(1),
            response.TruncatedLines);
    }

    private static async Task<int> DrainAsync(
        IReadOnlyList<string> lines,
        Func<string, CancellationToken, ValueTask> receiver,
        TruncatedLineTally? tally,
        CancellationToken cancellationToken)
    {
        var payload = lines.Count == 0 ? string.Empty : string.Join("\n", lines) + "\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        using var reader = new StreamReader(stream);
        return await SystemProcessRunner.DrainAsync(reader, receiver, tally, cancellationToken);
    }
}

internal sealed record ScriptedProcessResult(
    int ExitCode,
    IReadOnlyList<string> StandardOutput,
    IReadOnlyList<string> StandardError,
    // What a real drain would have counted. Null by default so every existing script keeps saying
    // "nobody counted" rather than silently claiming a clean stream.
    int? TruncatedLines = null,
    // Set to hand these lines to the production drain rather than deliver them as they are, for a
    // test whose subject is what the drain does to them.
    bool DrainRealStreams = false);
