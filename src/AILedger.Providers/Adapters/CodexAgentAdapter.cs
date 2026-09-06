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

    // Codex discovers skills and AGENTS.md under CODEX_HOME. The operator's own home carries a
    // full interactive install — including its own copies of the pipeline skills — so a governed
    // run gets a purpose-built home holding only authentication and the model choice. Ledger
    // serves the role's skills through the manifest; the ambient copies would be a second source
    // of the same rules, diverging the moment either is edited.
    protected override ProviderLaunchScope OpenLaunchScope(AgentLaunchRequest request)
    {
        var operatorHome = Environment.GetEnvironmentVariable("CODEX_HOME")
                           ?? Path.Combine(
                               Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var credentials = Path.Combine(operatorHome, "auth.json");
        if (!File.Exists(credentials))
        {
            throw new AgentAdapterException(
                $"Codex authentication was not found at '{credentials}'. Authenticate Codex, or set CODEX_HOME.");
        }

        var governedHome = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"ailedger-codex-{request.RunId.Value}-{Guid.NewGuid():N}")).FullName;
        // Linked, not copied: a governed run should not put a second copy of the operator's
        // credentials on disk for its duration.
        File.CreateSymbolicLink(Path.Combine(governedHome, "auth.json"), credentials);
        var configuration = request.Model is null
            ? string.Empty
            : $"model = \"{request.Model}\"{Environment.NewLine}";
        File.WriteAllText(Path.Combine(governedHome, "config.toml"), configuration);

        return new ProviderLaunchScope(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["CODEX_HOME"] = governedHome },
            governedHome);
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
