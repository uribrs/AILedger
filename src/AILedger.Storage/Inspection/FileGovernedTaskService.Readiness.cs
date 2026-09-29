using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Findings;
using AILedger.Core.Alternatives;
using AILedger.Core.Artifacts;
using AILedger.Core.ClaimDispositions;
using AILedger.Core.Inspection;
using AILedger.Storage.Findings;
using AILedger.Storage.Alternatives;
using AILedger.Storage.Artifacts;
using AILedger.Storage.ClaimDispositions;
using AILedger.Storage.Inspection;

namespace AILedger.Storage;

public sealed partial class FileGovernedTaskService
{
    private static readonly string[] ReadinessActions = ["record_findings", "record_alternatives", "submit_artifact",
        "record_claim_dispositions", "prepare_work", "complete_work", "transition_stage"];

    public async Task<ReadinessResult> CheckReadinessAsync(InspectionBinding binding, ReadinessQuery query, CancellationToken cancellationToken)
    {
        InspectionSnapshot? snapshot = null;
        var execution = query.Action is "prepare_work" ? "CLI work add" : query.Action is "complete_work" ? "CLI work complete" :
            query.Action is "transition_stage" ? "CLI stage transition" : query.Action;
        try
        {
            var data = await ReadInspectionAsync(binding, cancellationToken).ConfigureAwait(false);
            snapshot = Snapshot(data.State, binding);
            if (query.SchemaVersion != 1 || query.ExpectedVersion < 1)
                throw new FindingsRequestException("invalid_request", "Readiness requires schema_version=1 and the inspected expected_version.");
            if (!ReadinessActions.Contains(query.Action, StringComparer.Ordinal))
                return new("unsupported", query.Action, snapshot, new("unsupported_action", query.Action, null,
                    "An explicitly supported action", "No readiness evaluator is implemented for this action.",
                    "Use the existing authorized CLI/host path. Work dispatch, run completion, decision acceptance, waivers and other actions are not covered."), execution);
            CheckVersion(query.ExpectedVersion, data.State);
            ValidateProposal(query);
            var outcome = ReadinessCandidate(data, binding, query);
            if (outcome is not null && outcome.State.Version > _maximumEventsPerTask)
                throw new FindingsRequestException("capacity_exceeded", "The action would exceed the task event limit.");
            if (outcome is not null && data.EventLogBytes + ProspectiveBytes(data.State, binding, query, outcome) > _maximumEventLogBytes)
                throw new FindingsRequestException("capacity_exceeded", EventLogWouldExceedByteLimit().Message + " Inspection conservatively includes any uncommitted tail; only the existing write path may repair it.");
            // Candidate checks below deliberately never invent a filesystem inspection from a ledger identity.
            var assurance = snapshot.CandidateId is not null || query.WorkId is { } work &&
                data.State.Runs.Values.Any(r => r.Assurance?.WorkItemIds.Contains(new WorkItemId(work)) == true);
            if (assurance && query.Action is "submit_artifact" or "complete_work")
                return new("unknown", query.Action, snapshot, new("candidate_uninspected", query.Action, query.WorkId ?? snapshot.CandidateId,
                    "Current physical candidate identity", "Ledger admission passed; the physical candidate was not inspected by this read endpoint.",
                    "Use the existing frozen assurance/host candidate inspection before execution; do not infer a pass from recorded candidate identity."),
                    execution, "uninspected");
            return new("ready", query.Action, snapshot, null, execution,
                EnvironmentInspection: query.Action == "prepare_work" ? "cognitive_skills_observed; repository_base_ref_not_captured" : "ledger_only");
        }
        catch (Exception e) when (InspectionError(e))
        {
            var status = e is FindingsRequestException or GovernanceException ? "blocked" : "unknown";
            return new(status, query.Action, snapshot, InspectionFailure(e, query.Action, query.WorkId ?? query.Work?.Id ?? query.Transition?.Stage.ToString()), execution);
        }
    }

    private int ProspectiveBytes(GovernedTaskState state, InspectionBinding binding, ReadinessQuery query, CommandOutcome outcome)
    {
        // Fingerprints and transaction IDs have fixed lengths; production receipt builders preserve
        // operation metadata and dependency-event sizing. None of these provisional IDs escape.
        var fingerprint = new string('0', 64);
        if (query.Findings is { } findings)
            return SerializeAppend(outcome.Events, receipt: CreateFindingsReceipt(new(binding.TaskId, binding.ActorId,
                binding.RunId, binding.CorrelationId, binding.CausationId), FindingsValidation.Snapshot(findings), fingerprint, outcome)).Length;
        if (query.Alternatives is { } alternatives)
            return SerializeAppend(outcome.Events, alternativesReceipt: CreateAlternativesReceipt(new(binding.TaskId, binding.ActorId,
                binding.RunId, binding.CorrelationId, binding.CausationId), AlternativesValidation.Snapshot(alternatives), fingerprint, outcome)).Length;
        if (query.Artifact is { } artifact)
            return SerializeAppend(outcome.Events, artifactReceipt: CreateArtifactSubmissionReceipt(new(binding.TaskId, binding.ActorId,
                binding.RunId!.Value, binding.CorrelationId, binding.CausationId), ArtifactSubmissionIdentity.Validate(artifact), fingerprint, outcome)).Length;
        if (query.Dispositions is { } dispositions)
            return SerializeAppend(outcome.Events, dispositionsReceipt: CreateClaimDispositionsReceipt(state, new(binding.TaskId, binding.ActorId,
                binding.RunId, binding.CorrelationId, binding.CausationId), ClaimDispositionsValidation.Snapshot(dispositions), fingerprint, outcome)).Length;
        return SerializeAppend(outcome.Events).Length;
    }

    private static void ValidateProposal(ReadinessQuery query)
    {
        var count = new object?[] { query.Findings, query.Alternatives, query.Artifact, query.Dispositions,
            query.Work, query.WorkId, query.Transition }.Count(value => value is not null);
        var matches = query.Action switch
        {
            "record_findings" => query.Findings is not null, "record_alternatives" => query.Alternatives is not null,
            "submit_artifact" => query.Artifact is not null, "record_claim_dispositions" => query.Dispositions is not null,
            "prepare_work" => query.Work is not null, "complete_work" => !string.IsNullOrWhiteSpace(query.WorkId),
            "transition_stage" => query.Transition is not null, _ => false
        };
        if (count != 1 || !matches) throw new FindingsRequestException("invalid_request", "Supply exactly the matching typed proposal for this action.");
    }

    private CommandOutcome? ReadinessCandidate(InspectionSnapshotData data, InspectionBinding binding, ReadinessQuery query)
    {
        var state = data.State;
        switch (query.Action)
        {
            case "record_findings":
            {
                var b = new FindingsBinding(binding.TaskId, binding.ActorId, binding.RunId, binding.CorrelationId,
                    binding.CausationId, binding.AllowRecordFindings, binding.AllowRunless);
                ValidateFindingsBinding(b);
                var request = FindingsValidation.Snapshot(query.Findings!);
                if (CheckObservedReceipt(data, binding, query.Action, request.RequestId, FindingsFingerprint.Compute(b, request))) return null;
                var attempt = new FindingsAttempt();
                try { return BuildFindingsCandidate(state, b, request, attempt); }
                catch (GovernanceException e) { throw new FindingsRequestException("kernel_refused", e.Message, attempt.ItemPath); }
            }
            case "record_alternatives":
            {
                var b = new AlternativesBinding(binding.TaskId, binding.ActorId, binding.RunId, binding.CorrelationId,
                    binding.CausationId, binding.AllowRecordAlternatives, binding.AllowRunless);
                ValidateAlternativesBinding(b);
                var request = AlternativesValidation.Snapshot(query.Alternatives!);
                if (CheckObservedReceipt(data, binding, query.Action, request.RequestId, AlternativesFingerprint.Compute(b, request))) return null;
                var attempt = new AlternativesAttempt();
                try { return BuildAlternativesCandidate(state, b, request, attempt); }
                catch (GovernanceException e) { throw new FindingsRequestException("kernel_refused", e.Message, attempt.ItemPath); }
            }
            case "submit_artifact":
            {
                var b = new ArtifactSubmissionBinding(binding.TaskId, binding.ActorId, binding.RunId ?? new RunId(""),
                    binding.CorrelationId, binding.CausationId, binding.AllowSubmitArtifact);
                ValidateArtifactSubmissionBinding(b);
                var request = ArtifactSubmissionIdentity.Validate(query.Artifact!);
                if (CheckObservedReceipt(data, binding, query.Action, request.RequestId, ArtifactSubmissionIdentity.Compute(b, request))) return null;
                return BuildArtifactSubmissionCandidate(state, b, request, new ArtifactSubmissionAttempt());
            }
            case "record_claim_dispositions":
            {
                var b = new ClaimDispositionsBinding(binding.TaskId, binding.ActorId, binding.RunId, binding.CorrelationId,
                    binding.CausationId, binding.AllowRecordClaimDispositions, binding.AllowRunless);
                ValidateClaimDispositionsBinding(b);
                var request = ClaimDispositionsValidation.Snapshot(query.Dispositions!);
                if (CheckObservedReceipt(data, binding, query.Action, request.RequestId, ClaimDispositionsFingerprint.Compute(b, request))) return null;
                var attempt = new ClaimDispositionsAttempt();
                try { return BuildClaimDispositionsCandidate(state, b, request, attempt); }
                catch (GovernanceException e) { throw new FindingsRequestException("kernel_refused", e.Message, attempt.ItemPath); }
            }
            default:
                return _commandHandler.Handle(state, ReadinessCommand(state, binding, query), DateTimeOffset.UtcNow);
        }
    }

    private static LedgerCommand ReadinessCommand(GovernedTaskState state, InspectionBinding binding, ReadinessQuery query)
    {
        if (query.Work is { } work)
        {
            if (string.IsNullOrWhiteSpace(work.Id) || work.Id.Length > 256 || string.IsNullOrWhiteSpace(work.Title) || work.Title.Length > 8192 ||
                work.Scope is null || work.Claims is null || work.Scope.Count > 32 || work.Claims.Count > 32 ||
                work.Scope.Concat(work.Claims).Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 4096))
                throw new FindingsRequestException("invalid_request", "Work preparation requires bounded identifiers, title, claims and scope.");
            var skills = binding.CognitiveArtifacts is null ? null : new ContextAssembler().SkillsServed(state.Roles[binding.ActorId].Role, binding.CognitiveArtifacts);
            return new AddWorkItemCommand(binding.ActorId, binding.CausationId, binding.CorrelationId, new(work.Id), work.Title,
                work.Owner is null ? null : new ActorId(work.Owner), work.Claims.Select(id => new ClaimId(id)).ToArray(), work.Scope,
                work.NotSplitJustification is null ? null : new AlternativeId(work.NotSplitJustification), SkillsServedNow: skills);
        }
        if (query.WorkId is { } id)
        {
            if (id.Length > 256) throw new FindingsRequestException("invalid_request", "Work ID exceeds 256 characters.");
            return new CompleteWorkItemCommand(binding.ActorId, binding.CausationId, binding.CorrelationId, new(id));
        }
        var transition = query.Transition!;
        if (transition.Reason?.Length > 8192 || transition.SerialJustification?.Length > 256)
            throw new FindingsRequestException("invalid_request", "Transition text exceeds bounds.");
        return new RequestStageTransitionCommand(binding.ActorId, binding.CausationId, binding.CorrelationId,
            transition.Stage, Reason: transition.Reason,
            SerialJustification: transition.SerialJustification is null ? null : new AlternativeId(transition.SerialJustification));
    }

    private static bool CheckObservedReceipt(InspectionSnapshotData data, InspectionBinding binding, string action, string requestId, string fingerprint)
    {
        IEnumerable<IRecordingReceiptIdentity> receipts = action switch
        {
            "record_findings" => data.Findings, "record_alternatives" => data.Alternatives,
            "submit_artifact" => data.Artifacts, _ => data.Dispositions
        };
        var receipt = receipts.SingleOrDefault(r => r.ActorId == binding.ActorId.Value && r.RequestId == requestId);
        if (receipt is null) return false;
        AuthorizeInspectionReceipt(data.State, binding, action, receipt);
        if (receipt.PayloadFingerprint != fingerprint)
            throw new FindingsRequestException("idempotency_conflict", "This key already committed different content or attribution; the new submission was not applied.");
        return true;
    }

    private static bool ReceiptAuthorized(GovernedTaskState state, InspectionBinding binding, string action, IRecordingReceiptIdentity receipt)
    {
        try { AuthorizeInspectionReceipt(state, binding, action, receipt); return true; }
        catch (Exception e) when (e is GovernanceException or FindingsRequestException) { return false; }
    }

    private static void AuthorizeInspectionReceipt(GovernedTaskState state, InspectionBinding binding, string action, IRecordingReceiptIdentity receipt)
    {
        var actor = binding.ActorId;
        var cause = binding.CausationId;
        var correlation = binding.CorrelationId;
        var policy = new AuthorizationPolicy();
        switch (receipt)
        {
            case FindingsReceipt r when binding.AllowRecordFindings:
                if (r.Findings.Count > 0) policy.Authorize(state, new AddClaimCommand(actor, cause, correlation, new(r.Findings[0].ClaimId), "receipt access", null));
                if (r.Evidence.Count > 0) policy.Authorize(state, new AddEvidenceCommand(actor, cause, correlation, new(r.Evidence[0].EvidenceId), "receipt access", "receipt access", "receipt access", [], []));
                break;
            case AlternativesReceipt r when binding.AllowRecordAlternatives:
                policy.Authorize(state, new RecordAlternativeCommand(actor, cause, correlation, new(r.Alternatives[0].AlternativeId), "receipt access", "receipt access", null, null)); break;
            case ArtifactSubmissionReceipt r when binding.AllowSubmitArtifact:
                policy.Authorize(state, new RecordArtifactCommand(actor, cause, correlation, new(r.Artifact.ArtifactId), GovernedArtifactKind.VerifierOutput, "receipt access", "receipt access", null, binding.RunId, null)); break;
            case ClaimDispositionsReceipt r when binding.AllowRecordClaimDispositions:
                policy.Authorize(state, new ResolveClaimCommand(actor, cause, correlation, new(r.Dispositions[0].ClaimId), r.Dispositions[0].Status, [])); break;
            default: throw new FindingsRequestException("authorization_denied", "The trusted host grant for " + action + " is absent.");
        }
    }
}
