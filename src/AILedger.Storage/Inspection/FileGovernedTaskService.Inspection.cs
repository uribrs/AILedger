using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Findings;
using AILedger.Core.Alternatives;
using AILedger.Core.Artifacts;
using AILedger.Core.ClaimDispositions;
using AILedger.Core.Inspection;
using AILedger.Storage.Inspection;

namespace AILedger.Storage;

public sealed partial class FileGovernedTaskService : ITaskInspector
{
    private async Task<InspectionSnapshotData> ReadInspectionAsync(InspectionBinding binding, CancellationToken token)
    {
        ValidateInspectionBinding(binding);
        var directory = _pathResolver.Resolve(binding.TaskId);
        var path = Path.Combine(directory, _layout.EventsFileName);
        if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A task workspace cannot be a symbolic link or reparse point.");
        if (!File.Exists(path)) throw new FindingsRequestException("task_not_found", "The bound task does not exist.");
        // Only the existing synchronization file may be opened/created. No recovery, projection repair,
        // receipt re-flush, context.built or refusal journal writes occur on this path.
        await using var lease = await _mutationLock.AcquireAsync(Path.Combine(directory, _layout.LockFileName), token)
            .ConfigureAwait(false);
        var findings = new List<FindingsReceipt>();
        var alternatives = new List<AlternativesReceipt>();
        var artifacts = new List<ArtifactSubmissionReceipt>();
        var dispositions = new List<ClaimDispositionsReceipt>();
        GovernedTaskState? state = null;
        await foreach (var e in ReadEventsAsync(path, token, receipts: findings, alternativeReceipts: alternatives,
                           artifactReceipts: artifacts, dispositionReceipts: dispositions).ConfigureAwait(false))
        {
            ValidateEventEnvelope(binding.TaskId, e, state?.Version ?? 0);
            try { state = _reducer.Apply(state, e); }
            catch (GovernanceException error) { throw new InvalidDataException("Invalid event history.", error); }
        }
        if (state is null) throw new FindingsRequestException("task_not_found", "The bound task has no committed history.");
        await EnsureEventLogHasNotLostHistoryAsync(directory, state, token).ConfigureAwait(false);
        ValidateBoundRun(new FindingsBinding(binding.TaskId, binding.ActorId, binding.RunId,
            binding.CorrelationId, binding.CausationId), state);
        // Reuse the actual read authority and run/reviewer selection boundary, before any state is returned.
        _ = InspectionManifest(state, binding, "relevant");
        return new(state) { EventLogBytes = new FileInfo(path).Length, Findings = findings, Alternatives = alternatives, Artifacts = artifacts, Dispositions = dispositions };
    }

    private static void ValidateInspectionBinding(InspectionBinding binding)
    {
        if (binding is null || !binding.AllowInspect || string.IsNullOrWhiteSpace(binding.TaskId.Value) ||
            string.IsNullOrWhiteSpace(binding.ActorId.Value) || string.IsNullOrWhiteSpace(binding.CorrelationId) ||
            binding.RunId is null && !binding.AllowRunless ||
            binding.RunId is { } run && (string.IsNullOrWhiteSpace(run.Value) || binding.CorrelationId != run.Value))
            throw new FindingsRequestException("authorization_denied", "Invalid trusted inspection binding or grant.");
    }

    private static ContextManifest InspectionManifest(GovernedTaskState state, InspectionBinding binding, string selection)
    {
        if (selection is not ("relevant" or "task"))
            throw new FindingsRequestException("invalid_request", "Selection must be relevant or task.");
        var assembler = new ContextAssembler();
        var artifacts = binding.CognitiveArtifacts ?? [];
        if (binding.RunId is { } run)
        {
            var manifest = assembler.BuildForRun(state, binding.ActorId, run, artifacts, DateTimeOffset.UtcNow);
            // Bound reviewers cannot use a task-wide read to undo frozen narrative isolation.
            if (selection == "relevant" || manifest.Role == RoleKind.CodeReviewer) return manifest;
        }
        return assembler.Build(state, binding.ActorId, null, artifacts, DateTimeOffset.UtcNow);
    }

    private static InspectionSnapshot Snapshot(GovernedTaskState state, InspectionBinding binding)
    {
        var manifest = InspectionManifest(state, binding, "relevant");
        var freshness = "missing";
        if (state.ContextBuilds.TryGetValue(binding.ActorId, out var build))
        {
            var current = binding.CognitiveArtifacts is null ? null : new ContextAssembler().SkillsServed(manifest.Role, binding.CognitiveArtifacts);
            freshness = current is null ? "unknown" : current.Count == 0 || build.Skills.Count == 0 ? "unusable" :
                ContextSkills.SameAs(build.Skills, current) ? "current" : "stale";
        }
        return new(state.TaskId.Value, binding.ActorId.Value, binding.RunId?.Value, state.Version,
            $"{state.TaskId.Value}:{state.Version:D10}", DateTimeOffset.UtcNow, state.Stage.ToString(), manifest.Role.ToString(),
            manifest.Capabilities.Select(c => c.ToString()).ToArray(), manifest.Assurance?.CandidateId, freshness);
    }

    private static void CheckVersion(long? expected, GovernedTaskState state)
    {
        if (expected is { } version && version != state.Version)
            throw new FindingsRequestException("stale_snapshot", "Task version changed. Inspect again and reconsider the proposed action.");
    }

    private static InspectionDiagnostic InspectionFailure(Exception error, string action, string? reference = null) =>
        new(error is FindingsRequestException request ? request.Code : error is GovernanceException ? "kernel_refused" : "inspection_unavailable",
            action, error is FindingsRequestException item ? item.ItemPath ?? reference : reference,
            error is GovernanceException || error is FindingsRequestException { Code: "kernel_refused" } ? error.Message : "Current authorized snapshot and valid references", error.Message,
            Recovery(action));

    private static string Recovery(string action) => action switch
    {
        "prepare_work" => "Inspect dependencies and recorded grants. Use CLI context build with the host cognitive root for a missing/stale brief; use explicit operator work/assignment commands for scope or grant changes, then check again.",
        "complete_work" => "Inspect the work/dependencies. Finish required working/verifier/reviewer runs through the existing host, settle named escalations, then check again before CLI work complete.",
        "transition_stage" => "Satisfy the kernel's named prerequisite through the existing authorized operation, then check again before CLI stage transition. Inspection cannot waive or advance a stage.",
        "submit_artifact" => "Inspect the bound run and current output; correct content/kind/supersession and submit through submit_artifact while the producer run is active. Physical candidate inspection remains the assurance host's responsibility.",
        "record_claim_dispositions" => "Retrieve the current claim and cited evidence; reconsider expected status/direction/rationale. Record missing evidence with record_findings. Only an authorized actor may resolve claims.",
        "record_findings" or "record_alternatives" => "Retrieve named references and correct the proposal. Missing grants require an explicit operator assignment change. For a committed key conflict, retain the original request; do not alter a retry.",
        _ => "Inspect the current task again; follow the returned retrieval references. Correct inputs or restore the trusted host/history. Historical or protected material has no inspection bypass; use the existing authorized CLI path where permitted."
    };

    private static bool InspectionError(Exception error) => error is FindingsRequestException or GovernanceException or
        IOException or InvalidDataException or UnauthorizedAccessException or JsonException or OperationCanceledException or ArgumentException;
}
