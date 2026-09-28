using AILedger.Core.Contracts;

namespace AILedger.Providers.Adapters;

internal static class FindingsRecording
{
    internal const string ClaudeTool = "mcp__ailedger__record_findings";
    internal const string ClaudeAlternativesTool = "mcp__ailedger__record_alternatives";

    // CLI overrides have precedence over writable user/project files. There is no host binding
    // in CODEX_HOME. Approval is granted to these two recording operations, not every MCP or shell tool.
    internal static void AddCodexArguments(List<string> arguments, ProviderFindingsEndpoint? endpoint)
    {
        if (endpoint is null) return;
        var args = string.Join(", ", endpoint.Arguments.Select(RoslynNavigation.Quote));
        arguments.Add("--config");
        arguments.Add("mcp_servers.ailedger={command=" + RoslynNavigation.Quote(endpoint.Command) +
            ",args=[" + args + "],enabled_tools=[\"record_findings\",\"record_alternatives\"],required=true," +
            "default_tools_approval_mode=\"prompt\",tools={record_findings={approval_mode=\"approve\"},record_alternatives={approval_mode=\"approve\"}}}");
    }

    internal static object ClaudeServer(ProviderFindingsEndpoint endpoint) =>
        new { type = "stdio", command = endpoint.Command, args = endpoint.Arguments };

    internal static string Guidance(AgentLaunchRequest request, string command)
    {
        if (request.FindingsEndpoint is null)
            return $"  {command} claim add --task {request.TaskId} --actor {request.ActorId} --id ID --statement TEXT\n" +
                $"  {command} evidence add --task {request.TaskId} --actor {request.ActorId} --id ID --source-type TYPE --citation TEXT --summary TEXT [--supports CLAIM] [--refutes CLAIM]";
        return """
            Record claims and evidence with the supplied ailedger record_findings MCP tool directly.
            This recording guidance supersedes claim/evidence shell examples in the manifest's skills.
            Do not shell-translate prose or allocate durable claim/evidence IDs. Example tool arguments:
            {"schema_version":1,"request_id":"observation-1","findings":[{"key":"f1","statement":"Observed behavior"}],"evidence":[{"key":"e1","source_type":"source-read","citation":"file:line","summary":"What the source establishes","supports":[{"finding":"f1"}],"refutes":[]}]}
            Use {"claim_id":"existing-id"} to reference an existing claim. Both arrays are required;
            either may be empty, but not both. Findings stay open; evidence does not resolve them.
            Keep the original body and stable request_id. For outcome_unknown or a lost response,
            retry exactly that body/key on this binding. A replay returns the original receipt;
            never change content or generate a new key to evade a conflict. Retain receipt mappings.
            Task, subject actor, run, correlation, causation and grants are supplied by the host, not
            tool arguments or files. Kernel capability refusals remain authoritative. Do not modify
            tool configuration or launch a replacement endpoint.
            Record your own rejected approaches with the supplied record_alternatives MCP tool:
            {"schema_version":1,"request_id":"alternatives-1","alternatives":[{"key":"a1","statement":"Approach considered","rejection_rationale":"Why it was rejected"}]}
            Optional replaced_by_decision_id and from_lesson link an existing decision or recalled lesson.
            This tool accepts 1–32 alternatives, generates IDs, and commits all or none. The same retry
            rules apply. Preserve your actual wording; only outer whitespace is trimmed. This guidance
            supersedes alternative-record shell examples in supplied skills. A missing RecordAlternative
            capability requires an explicit operator assignment change preserving intended existing grants; do not
            impersonate a lead, self-grant, or hand reasoning to another actor merely for transcription.
            New default author roles include RecordAlternative; old recorded assignments do not change.
            Recording alternatives grants no approval powers. Other operations still use the CLI below;
            operator recording CLI support remains available outside this supplied path.
            """;
    }
}
