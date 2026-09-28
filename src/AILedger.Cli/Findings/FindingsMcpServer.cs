using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AILedger.Core.Findings;
using AILedger.Core.Alternatives;
using AILedger.Cli.Alternatives;

namespace AILedger.Cli.Findings;

// A local stdio MCP connection. Eight in-flight calls bound memory while the reader continues to
// handle cancellation. Each invocation still uses the recorder's cross-process task lease.
public sealed partial class FindingsMcpServer
{
    private readonly FindingsMcpHost _host;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly FindingsTransportJournal _journal;
    private readonly SemaphoreSlim _outputGate = new(1, 1);
    private readonly Dictionary<string, PendingCall> _pending = new(StringComparer.Ordinal);
    private bool _initialized;
    private bool _ready;

    public FindingsMcpServer(FindingsMcpHost host, Stream input, Stream output, TextWriter error)
    {
        _host = host;
        _input = input;
        _output = output;
        _journal = new(host.Configuration, error);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var reader = new FindingsFrameReader(_input);
        try
        {
            while (await reader.ReadAsync(connection.Token).ConfigureAwait(false) is { } frame)
            {
                ReapCompletedCalls();
                await DispatchAsync(frame, connection).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException)
        {
            await connection.CancelAsync().ConfigureAwait(false);
            await _journal.CaptureAsync(new() { MessageKind = "connection", Failure = e is OperationCanceledException
                ? "connection_cancelled" : "input_failed" }).ConfigureAwait(false);
        }
        finally
        {
            // EOF drains accepted calls. A disconnect/cancel never claims to undo an append.
            await Task.WhenAll(_pending.Values.Select(p => p.Completion)).ConfigureAwait(false);
            ReapCompletedCalls();
        }
    }

    private void ReapCompletedCalls()
    {
        foreach (var key in _pending.Where(p => p.Value.Completion.IsCompleted).Select(p => p.Key).ToArray())
        {
            _pending[key].Cancellation.Dispose();
            _pending.Remove(key);
        }
    }

    private async Task DispatchAsync(FindingsFrame frame, CancellationTokenSource connection)
    {
        var attempt = new FindingsTransportAttempt();
        JsonElement? id = null;
        var notification = false;
        try
        {
            if (frame.Oversized || !frame.Terminated)
                throw new RpcFailure(-32700, frame.Oversized ? "Frame exceeds 272 KiB." : "Unterminated JSON-RPC frame.");
            using var document = StrictJson.Parse(frame.Bytes);
            var root = document.RootElement;
            StrictJson.Members(root, ["jsonrpc", "method"], ["id", "params"]);
            if (StrictJson.Text(root, "jsonrpc") != "2.0") throw new RpcFailure(-32600, "Expected JSON-RPC 2.0.");
            id = FindingsMcpProtocol.Id(root);
            notification = id is null;
            var method = StrictJson.Text(root, "method");
            attempt.MessageKind = method == "tools/call" ? "tool_call" : "protocol";
            var parameters = root.TryGetProperty("params", out var value) ? value : default;
            if (notification)
            {
                await NotificationAsync(method, parameters).ConfigureAwait(false);
                return;
            }
            if (method == "initialize")
            {
                if (_initialized) throw new RpcFailure(-32600, "Connection already initialized.");
                var response = FindingsMcpProtocol.Initialize(parameters);
                await SendAsync(FindingsMcpProtocol.Result(id!.Value, response), connection.Token).ConfigureAwait(false);
                _initialized = true;
                return;
            }
            if (method == "ping")
            {
                await SendAsync(FindingsMcpProtocol.Result(id!.Value, new { }), connection.Token).ConfigureAwait(false);
                return;
            }
            if (!_ready) throw new RpcFailure(-32000, "Initialize the MCP connection first.");
            if (method == "tools/list")
            {
                if (parameters.ValueKind != JsonValueKind.Undefined) StrictJson.Members(parameters, [], ["_meta"]);
                await SendAsync(FindingsMcpProtocol.Result(id!.Value, FindingsMcpProtocol.Tools(_host.Configuration.AllowRecordAlternatives && _host.Recorder is IAlternativesRecorder)), connection.Token)
                    .ConfigureAwait(false);
                return;
            }
            if (method != "tools/call") throw new RpcFailure(-32601, "Method not found.");
            StrictJson.Members(parameters, ["name", "arguments"], ["_meta"]);
            var tool = StrictJson.Text(parameters, "name");
            attempt.IsAlternatives = tool == "record_alternatives";
            if (tool is not ("record_findings" or "record_alternatives"))
                throw new RpcFailure(-32602, "Unknown tool.");
            if (parameters.TryGetProperty("_meta", out var meta)) StrictJson.Validate(meta);
            var key = FindingsMcpProtocol.IdKey(id!.Value);
            if (_pending.ContainsKey(key)) throw new RpcFailure(-32600, "Request id is already in flight.");
            if (_pending.Count >= 8) throw new RpcFailure(-32000, "Too many in-flight requests; retry the same findings request.");
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(connection.Token);
            var arguments = parameters.GetProperty("arguments").Clone();
            var completion = CallAsync(id.Value, arguments, attempt, cancellation.Token, connection);
            _pending.Add(key, new(completion, cancellation));
        }
        catch (Exception e) when (e is RpcFailure or JsonException or InvalidOperationException or DecoderFallbackException)
        {
            attempt.Failure = e is RpcFailure ? "protocol_rejected" : "invalid_json";
            try
            {
                if (!notification)
                {
                    await SendAsync(FindingsMcpProtocol.Error(id, e is RpcFailure rpc ? rpc.Code : -32700,
                        e is RpcFailure failure ? failure.Message : "Invalid JSON-RPC message."), connection.Token)
                        .ConfigureAwait(false);
                    attempt.Delivery = "written";
                }
            }
            catch (Exception send) when (send is IOException or OperationCanceledException)
            {
                attempt.Delivery = "failed";
                await connection.CancelAsync().ConfigureAwait(false);
            }
            await _journal.CaptureAsync(attempt).ConfigureAwait(false);
        }
    }

    private async Task NotificationAsync(string method, JsonElement parameters)
    {
        if (method == "notifications/initialized" && _initialized) { _ready = true; return; }
        if (method == "notifications/cancelled")
        {
            StrictJson.Members(parameters, ["requestId"], ["reason", "_meta"]);
            StrictJson.Validate(parameters);
            var key = FindingsMcpProtocol.IdKey(parameters.GetProperty("requestId"));
            if (_pending.TryGetValue(key, out var call)) await call.Cancellation.CancelAsync().ConfigureAwait(false);
            return;
        }
        // A notification cannot execute a tool or change host attribution; no response is permitted.
        await _journal.CaptureAsync(new() { MessageKind = "protocol", Failure = "notification_ignored" }).ConfigureAwait(false);
    }

    private async Task CallAsync(JsonElement id, JsonElement arguments, FindingsTransportAttempt attempt,
        CancellationToken cancellationToken, CancellationTokenSource connection)
    {
        try
        {
            JsonElement body;
            if (attempt.IsAlternatives)
            {
                attempt.AlternativesResult = await InvokeAlternativesAsync(arguments, attempt, cancellationToken).ConfigureAwait(false);
                body = AlternativesResponseWriter.Write(attempt.AlternativesResult);
            }
            else
            {
                attempt.Result = await InvokeAsync(arguments, attempt, cancellationToken).ConfigureAwait(false);
                body = FindingsResponseWriter.Write(attempt.Result);
            }
            var result = new { content = new[] { new { type = "text", text = body.GetRawText() } },
                structuredContent = body, isError = attempt.Result?.Error is not null || attempt.AlternativesResult?.Error is not null };
            await SendAsync(FindingsMcpProtocol.Result(id, result), connection.Token).ConfigureAwait(false);
            attempt.Delivery = "written"; // A flush is observed, not acknowledgement by the client.
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            attempt.Failure = e is OperationCanceledException ? "response_cancelled" : "response_failed";
            attempt.Delivery = "failed";
            await connection.CancelAsync().ConfigureAwait(false);
        }
        finally { await _journal.CaptureAsync(attempt).ConfigureAwait(false); }
    }

    private async Task<FindingsResult> InvokeAsync(JsonElement arguments, FindingsTransportAttempt attempt,
        CancellationToken cancellationToken)
    {
        FindingsRequest request;
        try
        {
            attempt.RequestId = RequestId(arguments);
            request = FindingsRequestParser.Parse(arguments);
            attempt.RequestId = request.RequestId;
        }
        catch (Exception e) when (e is FindingsRequestException or JsonException or InvalidOperationException)
        {
            var error = e as FindingsRequestException;
            return new(attempt.Id, null, false, new(error?.Code ?? "invalid_request",
                error?.Message ?? "Expected the exact record_findings v1 request shape and valid Unicode.",
                "request", "unknown", "after_correction", error?.ItemPath));
        }
        FindingsBinding binding;
        try { binding = await _host.BindAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            attempt.Failure = "binding_cancelled";
            return new(attempt.Id, null, false, new("storage_unavailable",
                "Cancelled before application admission. Retry the same request and trusted binding.",
                "application", "unknown", "same_request"));
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return new(attempt.Id, null, false, new("authorization_denied",
                "Trusted host binding is unavailable or changed. Restore the original binding before retrying.",
                "binding", "unknown", "none"));
        }
        var started = Stopwatch.GetTimestamp();
        attempt.ApplicationEntered = true;
        try
        {
            var result = await _host.Recorder.RecordAsync(binding, request, cancellationToken).ConfigureAwait(false);
            attempt.ApplicationAttemptId = result.AttemptId;
            return result;
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            attempt.Failure = "application_exception";
            return new(attempt.Id, null, false, new("outcome_unknown",
                "The application did not return an outcome. Retry exactly the same body, key and trusted binding.",
                "application", "unknown", "same_request"));
        }
        finally { attempt.ApplicationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
    }

    private static string? RequestId(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            arguments.EnumerateObject().Count(p => p.Name == "request_id") != 1 ||
            arguments.GetProperty("request_id").ValueKind != JsonValueKind.String) return null;
        var id = arguments.GetProperty("request_id").GetString();
        return FindingsValidation.IsRequestId(id) ? id : null;
    }

    private async Task SendAsync(object response, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response);
        await _outputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _outputGate.Release(); }
    }

    private sealed record PendingCall(Task Completion, CancellationTokenSource Cancellation);
    private sealed class RpcFailure(int code, string message) : Exception(message)
    {
        internal int Code { get; } = code;
    }
}
