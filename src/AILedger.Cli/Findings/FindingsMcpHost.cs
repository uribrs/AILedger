using System.Text.Json;
using System.Text.Json.Serialization;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Findings;
using AILedger.Core.Alternatives;
using AILedger.Storage;

namespace AILedger.Cli.Findings;

// Only the composing host supplies this object. No field is populated from MCP messages, clientInfo,
// _meta, request IDs or environment inherited from an agent. Task 4 owns protecting/configuring it.
public sealed record FindingsHostConfiguration(string TaskWorkspaceRoot, string TaskId, string ActorId,
    string CorrelationId, string DiagnosticsDirectory, string? RunId = null, string? CausationId = null,
    bool AllowRecordFindings = false, bool AllowRunless = false, string? Provider = null,
    string? ProviderSessionId = null, bool AllowRecordAlternatives = false)
{
    internal AlternativesBinding AlternativesBinding => new(new(TaskId), new(ActorId),
        RunId is null ? null : new RunId(RunId), CorrelationId,
        CausationId is null ? null : new EventId(CausationId), AllowRecordAlternatives, AllowRunless);
    internal FindingsBinding Binding => new(new(TaskId), new(ActorId),
        RunId is null ? null : new RunId(RunId), CorrelationId,
        CausationId is null ? null : new EventId(CausationId), AllowRecordFindings, AllowRunless);
}

public sealed class FindingsMcpHost
{
    private readonly Func<CancellationToken, Task<FindingsHostConfiguration>> _readConfiguration;
    internal FindingsHostConfiguration Configuration { get; }
    internal IFindingsRecorder Recorder { get; }

    public FindingsMcpHost(FindingsHostConfiguration configuration, IFindingsRecorder recorder,
        Func<CancellationToken, Task<FindingsHostConfiguration>> readConfiguration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!Path.IsPathFullyQualified(configuration.TaskWorkspaceRoot) ||
            !Path.IsPathFullyQualified(configuration.DiagnosticsDirectory))
            throw new ArgumentException("Host workspace and diagnostics paths must be absolute.");
        _ = new TaskWorkspacePathResolver(configuration.TaskWorkspaceRoot).Resolve(new(configuration.TaskId));
        Configuration = configuration;
        Recorder = recorder;
        _readConfiguration = readConfiguration;
    }

    internal async Task<FindingsBinding> BindAsync(CancellationToken cancellationToken) =>
        (await ReadBindingAsync(cancellationToken).ConfigureAwait(false)).Binding;

    internal async Task<AlternativesBinding> BindAlternativesAsync(CancellationToken cancellationToken) =>
        (await ReadBindingAsync(cancellationToken).ConfigureAwait(false)).AlternativesBinding;

    private async Task<FindingsHostConfiguration> ReadBindingAsync(CancellationToken cancellationToken)
    {
        var current = await _readConfiguration(cancellationToken).ConfigureAwait(false);
        // Grants may be revoked on a running connection. Attribution and destination are pinned.
        if (current with { AllowRecordFindings = Configuration.AllowRecordFindings,
                AllowRunless = Configuration.AllowRunless,
                AllowRecordAlternatives = Configuration.AllowRecordAlternatives } != Configuration)
            throw new InvalidDataException("Host attribution changed; reconnect with the original binding for recovery.");
        return current;
    }

    internal static async Task<FindingsMcpHost> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Host configuration path must be absolute.");
        var configuration = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
        var recorder = new FileGovernedTaskService(configuration.TaskWorkspaceRoot, new CommandHandler(), new TaskReducer());
        return new(configuration, recorder, token => ReadAsync(path, token));
    }

    private static async Task<FindingsHostConfiguration> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            4096, FileOptions.Asynchronous);
        var bytes = new byte[16 * 1024 + 1];
        var length = await input.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false,
            cancellationToken).ConfigureAwait(false);
        if (length == bytes.Length) throw new InvalidDataException("Host configuration exceeds 16 KiB.");
        using var document = StrictJson.Parse(bytes.AsMemory(0, length));
        StrictJson.Validate(document.RootElement);
        StrictJson.Members(document.RootElement,
            ["task_workspace_root", "task_id", "actor_id", "correlation_id", "diagnostics_directory"],
            ["run_id", "causation_id", "allow_record_findings", "allow_runless", "provider", "provider_session_id", "allow_record_alternatives"]);
        return document.RootElement.Deserialize<FindingsHostConfiguration>(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new JsonException("Missing host configuration.");
    }
}
