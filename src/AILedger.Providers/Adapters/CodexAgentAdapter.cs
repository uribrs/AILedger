using System.Text.Json;
using AILedger.Core.Contracts;

namespace AILedger.Providers.Adapters;

public sealed class CodexAgentAdapter(IProcessRunner processRunner) : AgentAdapterBase(processRunner)
{
    public override string Provider => "codex";

    protected override IReadOnlyList<CapabilityProbe> CapabilityProbes =>
    [
        new(["exec", "--help"], ["--strict-config", "--sandbox", "--cd", "--add-dir", "--output-schema", "--json"]),
        new(["exec", "resume", "--help"], ["SESSION_ID", "--json"])
    ];

    // Codex refuses to run outside a git work tree unless --skip-git-repo-check is passed, and
    // that flag exists to be a deliberate choice rather than an adapter default. Governed work
    // should be in a repository anyway, so this is checked as a precondition and reported before
    // launch — the same reason the capability probes run first.
    protected override void EnsurePreconditions(AgentLaunchRequest request)
    {
        for (var directory = new DirectoryInfo(request.WorkingDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return;
            }
        }

        throw new AgentAdapterException(
            $"Codex refuses to run outside a git work tree, and '{request.WorkingDirectory}' is not inside one. " +
            "Govern work that lives in a repository, or initialise one for this scope.");
    }

    protected override IReadOnlyList<string> BuildArguments(AgentLaunchRequest request, ref string? sessionId)
    {
        var arguments = new List<string> { "exec", "--strict-config", "--sandbox", "workspace-write", "--cd", request.WorkingDirectory };
        AddOptionalGlobalArguments(arguments, request);

        if (request.Mode == AgentLaunchMode.Resume)
        {
            arguments.Add("resume");
            arguments.Add("--json");
            arguments.Add(request.ProviderSessionId!);
            arguments.Add("-");
            return arguments;
        }

        arguments.Add("--json");
        arguments.Add("--color");
        arguments.Add("never");
        arguments.Add("-");
        return arguments;
    }

    // Codex takes its prompt from stdin, so the briefing leads and the manifest follows it.
    protected override string ComposeStandardInput(AgentLaunchRequest request) =>
        GovernedExecutionBriefing.For(request, request.LedgerRoot) + Environment.NewLine + request.StandardInput;

    protected override ProviderEvent ParseEvent(long sequence, string json) => ProviderProtocol.ParseCodex(sequence, json);

    protected override string? ReadFinalOutput(ProviderEvent providerEvent)
    {
        try
        {
            return providerEvent.Type switch
            {
                "item.completed" => ProviderProtocol.ReadNestedString(providerEvent.RawJson, "item", "text"),
                "turn.completed" => ProviderProtocol.ReadString(providerEvent.RawJson, "output_text"),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void AddOptionalGlobalArguments(ICollection<string> arguments, AgentLaunchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            arguments.Add("--model");
            arguments.Add(request.Model);
        }

        if (!string.IsNullOrWhiteSpace(request.OutputSchema))
        {
            arguments.Add("--output-schema");
            arguments.Add(request.OutputSchema);
        }

        foreach (var directory in request.AdditionalDirectories)
        {
            arguments.Add("--add-dir");
            arguments.Add(directory);
        }
    }
}
