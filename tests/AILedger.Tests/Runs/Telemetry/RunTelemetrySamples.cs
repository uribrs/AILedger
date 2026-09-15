namespace AILedger.Tests.Runs.Telemetry;

// Provider telemetry shared by reader, completion-record, and launcher behavior tests. Keeping the
// captured sample here prevents a behavior test class from acting as test infrastructure.
internal static class RunTelemetrySamples
{
    internal const string ClaudeResult =
        """
        {"type":"result","subtype":"success","is_error":false,"num_turns":125,"duration_ms":1093960,
         "session_id":"session-1","usage":{"input_tokens":2,"cache_creation_input_tokens":41368,
         "cache_read_input_tokens":16285,"output_tokens":33110}}
        """;
}
