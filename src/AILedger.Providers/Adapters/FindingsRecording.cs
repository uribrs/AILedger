using AILedger.Core.Contracts;

namespace AILedger.Providers.Adapters;

internal static class FindingsRecording
{
    internal const string ClaudeTool = "mcp__ailedger__record_findings";
    internal const string ClaudeAlternativesTool = "mcp__ailedger__record_alternatives";

    internal const string ClaudeClaimDispositionsTool = "mcp__ailedger__record_claim_dispositions";
    internal const string ClaudeArtifactTool = "mcp__ailedger__submit_artifact";

    // CLI overrides have precedence over writable user/project files. There is no host binding
    // in CODEX_HOME. Approval is granted to these three recording operations, not every MCP or shell tool.
    internal static void AddCodexArguments(List<string> arguments, ProviderFindingsEndpoint? endpoint)
    {
        if (endpoint is null) return;
        var args = string.Join(", ", endpoint.Arguments.Select(RoslynNavigation.Quote));
        arguments.Add("--config");
        arguments.Add("mcp_servers.ailedger={command=" + RoslynNavigation.Quote(endpoint.Command) +
            ",args=[" + args + "],enabled_tools=[\"record_findings\",\"record_alternatives\",\"submit_artifact\",\"record_claim_dispositions\"],required=true," +
            "default_tools_approval_mode=\"prompt\",tools={record_findings={approval_mode=\"approve\"},record_alternatives={approval_mode=\"approve\"},submit_artifact={approval_mode=\"approve\"},record_claim_dispositions={approval_mode=\"approve\"}}}");
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
            Submit verifier-output and code-review-output documents directly with submit_artifact:
            {"schema_version":1,"request_id":"output-1","kind":"code-review-output","title":"Review result","content":"Your complete Markdown output"}
            The host assigns the artifact ID, actor, producer run, work scope and assurance/candidate links.
            Content is required inline (128 KiB UTF-8 maximum); it is preserved verbatim and hashed in the receipt.
            Optional expected_content_sha256 asserts its identity. Optional supersedes_artifact_id names a
            current predecessor; legacy revisions require it, and assurance replacements follow existing member rules.
            Submit while your matching verifier/reviewer run is active; existing document prerequisites still apply.
            This supersedes file-write/artifact-record instructions for these two kinds only. Preserve the original
            key/body for retries, including after a lost response. Submission never approves work or closes your run.
            Record already-reasoned explicit claim judgments with record_claim_dispositions:
            {"schema_version":1,"request_id":"judgments-1","dispositions":[{"key":"j1","claim":{"claim_id":"C1"},"expected_status":"open","status":"validated","rationale":"Why the cited evidence warrants this judgment","evidence":[{"evidence_id":"E1"}]}]}
            Use existing claim/evidence IDs from receipts or task context. status is validated or rejected;
            expected_status is open or validated. Each claim appears once; 1–32 items commit atomically,
            including rejection's decision/work invalidations. All evidence must support validation or
            refute rejection. Record evidence first with record_findings; evidence never validates a claim.
            This requires existing ResolveClaim capability (normally operator/planning lead); the tool
            grant and payload cannot grant it. Do not impersonate another actor or seek transcription-only
            approval. Your rationale is a durable judgment, not proof that the judgment is correct.
            Keep body/key/binding for uncertain retries. state_conflict requires re-reading and reconsidering;
            kernel_refused preserves the original rule and dispositions[index] points to the failing item.
            No prefix is accepted on refusal. Receipt entries show each accepted status change, rationale,
            evidence, event and dependency consequence; a replay describes the original commit, not current state.
            Formal decision proposal/acceptance, claim supersession, task/context reads and lifecycle
            operations remain CLI work with their own authority. No automatic closure or retrospective approval.
            Other kinds, external content references, artifact export and workflow operations still use the CLI below;
            operator recording CLI support remains available outside this supplied path.
            """;
    }
}
