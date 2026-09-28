using AILedger.Core.Contracts;

namespace AILedger.Providers.Adapters;

internal static class FindingsRecording
{
    internal const string ClaudeTool = "mcp__ailedger__record_findings";

    // CLI overrides have precedence over writable user/project files. There is no host binding
    // in CODEX_HOME. Approval is granted to this one operation, not every MCP or shell tool.
    internal static void AddCodexArguments(List<string> arguments, ProviderFindingsEndpoint? endpoint)
    {
        if (endpoint is null) return;
        var args = string.Join(", ", endpoint.Arguments.Select(RoslynNavigation.Quote));
        arguments.Add("--config");
        arguments.Add("mcp_servers.ailedger={command=" + RoslynNavigation.Quote(endpoint.Command) +
            ",args=[" + args + "],enabled_tools=[\"record_findings\"],required=true," +
            "default_tools_approval_mode=\"prompt\",tools={record_findings={approval_mode=\"approve\"}}}");
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
            tool configuration or launch a replacement endpoint. Other operations still use the CLI
            below; operator claim/evidence CLI support remains available outside this supplied path.
            """;
    }
}
