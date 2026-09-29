using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Core.Contracts;

namespace AILedger.Providers.Adapters;

public sealed class ClaudeAgentAdapter(IProcessRunner processRunner) : AgentAdapterBase(processRunner)
{
    private const int MaximumInputBytes = 10 * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string> ReviewerMemoryControls =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CLAUDE_CODE_DISABLE_CLAUDE_MDS"] = "1",
            ["CLAUDE_CODE_DISABLE_AUTO_MEMORY"] = "1",
            ["CLAUDE_CODE_DISABLE_ORG_MEMORY"] = "1",
            ["CLAUDE_CODE_POST_TURN_MEMORY"] = "0"
        };

    public override string Provider => "claude";

    protected override bool RequirePreassignedSessionMatch => true;

    protected override IReadOnlyList<CapabilityProbe> CapabilityProbes =>
    [
        new(["--help"],
            ["--print", "--output-format", "--session-id", "--resume", "--permission-prompts", "--settings", "--strict-mcp-config", "--disable-slash-commands"])
    ];

    protected override ProviderLaunchScope OpenLaunchScope(AgentLaunchRequest request)
    {
        var environment = request.Assurance?.VerifierRunId is not null
            ? ReviewerMemoryControls : new Dictionary<string, string>();
        var directory = Directory.CreateTempSubdirectory("ailedger-claude-navigation-").FullName;
        try
        {
            var providerSettings = JsonNode.Parse(request.Assurance?.VerifierRunId is not null
                ? ReviewerSettings() : SandboxSettings)!.AsObject();
            var settings = RoslynNavigation.CreateSettingsFile(request, directory);
            var servers = new Dictionary<string, object>();
            var tools = new List<string>();
            if (settings is not null)
            {
                providerSettings["hooks"] = JsonSerializer.SerializeToNode(
                    RoslynNavigation.Hooks(RoslynNavigation.GuardCommand(request.NavigationHostAssembly!, settings)));
                providerSettings["disableAllHooks"] = false;
                servers["roslyn"] = new { type = "stdio", command = "dotnet",
                    args = new[] { request.NavigationHostAssembly!, "navigation", "serve", settings } };
                tools.AddRange(RoslynNavigation.Tools.Select(tool => $"mcp__roslyn__{tool}"));
            }
            if (request.FindingsEndpoint is { } findings)
            {
                servers["ailedger"] = FindingsRecording.ClaudeServer(findings);
                tools.Add(FindingsRecording.ClaudeTool);
                tools.Add(FindingsRecording.ClaudeAlternativesTool);
                tools.Add(FindingsRecording.ClaudeArtifactTool);
                tools.Add(FindingsRecording.ClaudeClaimDispositionsTool);
                tools.Add("mcp__ailedger__inspect_task");
                tools.Add("mcp__ailedger__retrieve_context");
                tools.Add("mcp__ailedger__check_readiness");
                tools.AddRange((findings.AssuranceTools ?? []).Select(name => "mcp__ailedger__" + name));
            }

            var arguments = new List<string> { "--settings", providerSettings.ToJsonString() };
            if (servers.Count > 0)
            {
                // Inline launch configuration cannot be rewritten through an agent-writable file.
                arguments.AddRange(["--mcp-config", JsonSerializer.Serialize(new { mcpServers = servers }),
                    "--allowedTools", string.Join(",", tools)]);
            }
            return new ProviderLaunchScope(environment, directory, arguments);
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    protected override IReadOnlyList<string> BuildArguments(AgentLaunchRequest request, ref string? sessionId)
    {
        if (Encoding.UTF8.GetByteCount(request.StandardInput) > MaximumInputBytes)
        {
            throw new AgentAdapterException("Claude context exceeds the documented 10 MB stdin limit.");
        }

        sessionId ??= Guid.NewGuid().ToString();
        var arguments = new List<string>
        {
            "-p", GovernedExecutionBriefing.For(request, request.LedgerRoot) + Environment.NewLine + RoslynNavigation.Guidance,
            "--output-format", "stream-json",
            "--verbose",
            "--forward-subagent-text",
            "--permission-mode", "acceptEdits",
            "--permission-prompts", "none",
            "--strict-mcp-config",
            "--disable-slash-commands"
        };

        if (request.Mode == AgentLaunchMode.Resume)
        {
            arguments.Add("--resume");
            arguments.Add(request.ProviderSessionId!);
        }
        else
        {
            arguments.Add("--session-id");
            arguments.Add(sessionId);
        }

        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            arguments.Add("--model");
            arguments.Add(request.Model);
        }

        if (!string.IsNullOrWhiteSpace(request.OutputSchema))
        {
            arguments.Add("--json-schema");
            arguments.Add(request.OutputSchema);
        }

        if (request.AdditionalDirectories.Count > 0)
        {
            arguments.Add("--add-dir");
            foreach (var directory in request.AdditionalDirectories)
            {
                arguments.Add(directory);
            }
        }

        return arguments;
    }

    protected override ProviderEvent ParseEvent(long sequence, string json) => ProviderProtocol.ParseClaude(sequence, json);

    protected override string? ReadFinalOutput(ProviderEvent providerEvent)
    {
        if (providerEvent.Type != "result")
        {
            return null;
        }

        try
        {
            return ProviderProtocol.ReadString(providerEvent.RawJson, "result");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private const string SandboxSettings =
        "{\"sandbox\":{\"enabled\":true,\"failIfUnavailable\":true,\"autoAllowBashIfSandboxed\":true,\"allowUnsandboxedCommands\":false,\"excludedCommands\":[]}}";

    private static string ReviewerSettings()
    {
        // CLI settings keep ordinary settings from restoring memory after process startup.
        // Managed policy retains precedence; conflicting policy cannot promise isolated input.
        using var settings = JsonDocument.Parse(SandboxSettings);
        return JsonSerializer.Serialize(new
        {
            sandbox = settings.RootElement.GetProperty("sandbox"),
            env = ReviewerMemoryControls
        });
    }
}
