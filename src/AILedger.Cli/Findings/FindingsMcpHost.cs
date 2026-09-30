using System.Text.Json;
using System.Text.Json.Serialization;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Findings;
using AILedger.Core.Alternatives;
using AILedger.Core.ClaimDispositions;
using AILedger.Core.Artifacts;
using AILedger.Storage;
using AILedger.Core.Inspection;

namespace AILedger.Cli.Findings;

// Only the composing host supplies this object. No field is populated from MCP messages, clientInfo,
// _meta, request IDs or environment inherited from an agent. Task 4 owns protecting/configuring it.
public sealed record FindingsHostConfiguration(string TaskWorkspaceRoot, string TaskId, string ActorId,
    string CorrelationId, string DiagnosticsDirectory, string? RunId = null, string? CausationId = null,
    bool AllowRecordFindings = false, bool AllowRunless = false, string? Provider = null,
    string? ProviderSessionId = null, bool AllowRecordAlternatives = false, bool AllowSubmitArtifact = false, bool AllowRecordClaimDispositions = false, bool AllowInspect = false, string? CognitiveRoot = null, bool AllowDeclareProducerOutcome = false)
{
    internal ClaimDispositionsBinding ClaimDispositionsBinding => new(new(TaskId), new(ActorId),
        RunId is null ? null : new RunId(RunId), CorrelationId,
        CausationId is null ? null : new EventId(CausationId), AllowRecordClaimDispositions, AllowRunless);
    internal ArtifactSubmissionBinding ArtifactSubmissionBinding => new(new(TaskId), new(ActorId),
        new RunId(RunId ?? ""), CorrelationId,
        CausationId is null ? null : new EventId(CausationId), AllowSubmitArtifact);
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
    internal IFindingsRecorder? Recorder { get; }
    internal AILedger.Core.Assurance.IAssuranceService? Assurance { get; }
    internal bool AssuranceOnly { get; }

    public FindingsMcpHost(FindingsHostConfiguration configuration, IFindingsRecorder? recorder,
        Func<CancellationToken, Task<FindingsHostConfiguration>> readConfiguration,
        AILedger.Core.Assurance.IAssuranceService? assurance = null, bool assuranceOnly = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!Path.IsPathFullyQualified(configuration.TaskWorkspaceRoot) ||
            !Path.IsPathFullyQualified(configuration.DiagnosticsDirectory))
            throw new ArgumentException("Host workspace and diagnostics paths must be absolute.");
        _ = new TaskWorkspacePathResolver(configuration.TaskWorkspaceRoot).Resolve(new(configuration.TaskId));
        Configuration = configuration;
        if (!assuranceOnly && recorder is null) throw new ArgumentNullException(nameof(recorder));
        Recorder = recorder; Assurance = assurance; AssuranceOnly = assuranceOnly;
        _readConfiguration = readConfiguration;
    }

    internal async Task<FindingsBinding> BindAsync(CancellationToken cancellationToken) =>
        (await ReadBindingAsync(cancellationToken).ConfigureAwait(false)).Binding;

    internal async Task<AlternativesBinding> BindAlternativesAsync(CancellationToken cancellationToken) =>
        (await ReadBindingAsync(cancellationToken).ConfigureAwait(false)).AlternativesBinding;

    internal async Task<ArtifactSubmissionBinding> BindArtifactSubmissionAsync(CancellationToken cancellationToken) =>
        (await ReadBindingAsync(cancellationToken).ConfigureAwait(false)).ArtifactSubmissionBinding;

    internal async Task<ClaimDispositionsBinding> BindClaimDispositionsAsync(CancellationToken cancellationToken) =>
        (await ReadBindingAsync(cancellationToken).ConfigureAwait(false)).ClaimDispositionsBinding;

    internal async Task<InspectionBinding> BindInspectionAsync(CancellationToken cancellationToken)
    {
        var current = await ReadBindingAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ContextArtifact>? artifacts = null;
        // No agent payload or ambient environment selects the cognitive source.
        if (current.AllowInspect && current.CognitiveRoot is { } root)
        {
            try { artifacts = await new CognitiveArtifactLoader().LoadAsync(root, cancellationToken).ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) { }
        }
        return new(new(current.TaskId), new(current.ActorId), current.RunId is null ? null : new RunId(current.RunId),
            current.CorrelationId, current.CausationId is null ? null : new EventId(current.CausationId),
            current.AllowInspect, current.AllowRunless, current.AllowRecordFindings, current.AllowRecordAlternatives,
            current.AllowSubmitArtifact, current.AllowRecordClaimDispositions, artifacts);
    }

    internal Task<FindingsHostConfiguration> BindProducerOutcomeAsync(CancellationToken token) => ReadBindingAsync(token);

    private async Task<FindingsHostConfiguration> ReadBindingAsync(CancellationToken cancellationToken)
    {
        var current = await _readConfiguration(cancellationToken).ConfigureAwait(false);
        // Grants may be revoked on a running connection. Attribution and destination are pinned.
        if (current with { AllowRecordFindings = Configuration.AllowRecordFindings,
                AllowRunless = Configuration.AllowRunless,
                AllowRecordAlternatives = Configuration.AllowRecordAlternatives,
                AllowSubmitArtifact = Configuration.AllowSubmitArtifact,
                AllowRecordClaimDispositions = Configuration.AllowRecordClaimDispositions,
                AllowInspect = Configuration.AllowInspect,
                AllowDeclareProducerOutcome = Configuration.AllowDeclareProducerOutcome } != Configuration)
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
            ["run_id", "causation_id", "allow_record_findings", "allow_runless", "provider", "provider_session_id", "allow_record_alternatives", "allow_submit_artifact", "allow_record_claim_dispositions", "allow_inspect", "cognitive_root", "allow_declare_producer_outcome"]);
        return document.RootElement.Deserialize<FindingsHostConfiguration>(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new JsonException("Missing host configuration.");
    }
}
