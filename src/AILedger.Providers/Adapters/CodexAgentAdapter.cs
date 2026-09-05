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
