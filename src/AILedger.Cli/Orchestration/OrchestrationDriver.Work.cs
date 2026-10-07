using AILedger.Cli.Assurance;
using AILedger.Cli.Dispatch;
using AILedger.Cli.Providers;
using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
namespace AILedger.Cli.Orchestration;

internal sealed partial class OrchestrationDriver
{
    private string? _candidate;
    private RunId? _pairedVerifier;
    private async Task<DriverResult?> WorkPhaseAsync(DriverRequest request, CancellationToken token)
    {
        if (request.WorkItemId is not { } work || !_state!.WorkItems.TryGetValue(work, out var item))
            return Result(DriverStatus.Unsupported, "prepared_work_required", "Select one existing prepared work item. Bundles and automatic scope creation are unsupported.");
        if (item.Status == WorkItemStatus.Completed && _state.Stage == TaskStage.Review && options.AcceptancePrincipal is not null)
        {
            // The event log proves completion happened; a fresh admission still owns continuation.
            try { await TransitionAsync(TaskStage.Learn, request, token).ConfigureAwait(false); return null; }
            catch (AssuranceRefusal error) { _mutationInFlight = false; return Result(DriverStatus.AwaitingAcceptance, error.Code, error.Message); }
        }
        if (item.Status is WorkItemStatus.Blocked or WorkItemStatus.Stale or WorkItemStatus.Completed or WorkItemStatus.Abandoned)
            return Result(DriverStatus.Blocked, "work_not_runnable", "The selected work requires trusted scope/acceptance handling; the driver cannot reopen it.");
        if (preparation is not null) preparation.ValidateSelectedWork(_state!, work);
        var assurancePreparation = await PrepareAssuranceAsync(work, token).ConfigureAwait(false);
        if (assurancePreparation is not null) return assurancePreparation;
        var kind = _state.Stage switch
        {
            TaskStage.Verification => CognitiveWorkKind.Verification,
            TaskStage.Review => CognitiveWorkKind.Review,
            TaskStage.Repair => CognitiveWorkKind.Repair,
            _ => CognitiveWorkKind.Implementation
        };
        var pairing = ReviewPair(kind, work);
        if (pairing.Stop is not null) return pairing.Stop;
        if (kind == CognitiveWorkKind.Review && options.AcceptancePrincipal is { } acceptingPrincipal)
        {
            // Prepared/restarted Review entry has not necessarily crossed this driver's verifier gate.
            try { await kernel.ValidateRequiredChecksAsync(acceptingPrincipal, pairing.Run!.Value, token).ConfigureAwait(false); }
            catch (AssuranceRefusal error) { return Result(DriverStatus.Blocked, error.Code, error.Message); }
        }
        if (kind == CognitiveWorkKind.Review && options.AcceptancePrincipal is not null &&
            _state.Runs.Values.Any(r => r.SubjectRole == RoleKind.CodeReviewer && r.Status == AgentRunStatus.Completed &&
                r.Assurance?.CandidateId == _candidate && r.Assurance!.WorkItemIds.Contains(work)))
            return await CompleteAcceptedAsync(work, request, token).ConfigureAwait(false);
        var result = await DispatchAsync(kind, work, pairing.Run, token).ConfigureAwait(false);
        if (result.Stop is not null) return result.Stop;
        if (kind == CognitiveWorkKind.Verification) _pairedVerifier = _dispatches[^1].RunId;
        var assessment = result.Assessment!;
        if (assessment.Disposition == RoutingDisposition.Blocked)
        {
            // Interpretation is bounded cognitive work. The lead sees the governed record; the
            // driver neither parses the rationale nor makes an engineering issue a user decision.
            var judgment = await DispatchAsync(CognitiveWorkKind.Findings, null, null, token).ConfigureAwait(false);
            if (judgment.Stop is not null) return judgment.Stop;
            assessment = judgment.Assessment!;
            if (assessment.Disposition == RoutingDisposition.Proceed)
                return Result(DriverStatus.Blocked, "finding_disposition_required", "A generic findings assessment cannot override the original blocked judgment; an applicable finding disposition is required (Part 6).");
        }
        if (assessment.Disposition == RoutingDisposition.Repair &&
            WorkItemRunObservations.LatestCompletedWork(_state!, work)?.RoutingAssessment?.Work == CognitiveWorkKind.Repair)
            return await InvestigateRepairFailureAsync(work, request, token).ConfigureAwait(false);
        return await ApplyWorkJudgmentAsync(kind, work, assessment, request, token).ConfigureAwait(false);
    }

    private (RunId? Run, DriverResult? Stop) ReviewPair(CognitiveWorkKind kind, WorkItemId work)
    {
        if (kind != CognitiveWorkKind.Review) return (null, null);
        var prior = _pairedVerifier is { } known ? _state!.Runs[known] : _state!.Runs.Values
            .Where(r => r.SubjectRole == RoleKind.Verifier && r.Assurance?.WorkItemIds.Contains(work) == true)
            .OrderByDescending(r => r.EndedAt).FirstOrDefault();
        if (prior is null || prior.Status != AgentRunStatus.Completed || prior.Assurance?.CandidateId != _candidate)
            return (null, Result(DriverStatus.Blocked, "candidate_changed_or_unverified", "Current configured inputs differ from the verified candidate, or no paired verifier exists. Fresh independent verification is required."));
        if (_pairedVerifier is null && prior.RoutingAssessment?.Disposition != RoutingDisposition.Proceed)
            return (null, Result(DriverStatus.Blocked, "missing_verifier_judgment", "A prepared review requires an explicit current verifier routing judgment."));
        return (prior.Id, null);
    }

    private async Task<DriverResult?> ApplyWorkJudgmentAsync(CognitiveWorkKind kind, WorkItemId work,
        RoutingAssessment assessment, DriverRequest request, CancellationToken token)
    {
        switch (assessment.Disposition)
        {
            case RoutingDisposition.Blocked:
                return Result(DriverStatus.Blocked, "unresolved_judgment", assessment.Rationale);
            case RoutingDisposition.Replan:
                await ReplanAsync(request, token).ConfigureAwait(false);
                return null;
            case RoutingDisposition.Repair:
                if (_state!.Stage == TaskStage.Execution)
                    // No verification finding exists yet. Stay in the admitted worker stage and
                    // request a fresh bounded repair, rather than inventing a verification output.
                    return await RepairInExecutionAsync(work, request, token).ConfigureAwait(false);
                if (_state.Stage != TaskStage.Repair) await TransitionAsync(TaskStage.Repair, request, token).ConfigureAwait(false);
                return null;
            case RoutingDisposition.Proceed:
                if (kind == CognitiveWorkKind.Review && options.AcceptancePrincipal is not null)
                    return await CompleteAcceptedAsync(work, request, token).ConfigureAwait(false);
                if (kind == CognitiveWorkKind.Review)
                    return Result(DriverStatus.AwaitingAcceptance, "acceptance_bridge_required",
                        "Blind governed review returned an explicit judgment. Separate requirements-aware independent task-13 review, acceptance and governed work completion remain pending (Part 6). No work was completed.");
                if (kind == CognitiveWorkKind.Verification && options.AcceptancePrincipal is { } acceptingPrincipal)
                {
                    try
                    {
                        await kernel.ValidateRequiredChecksAsync(acceptingPrincipal, _pairedVerifier!.Value, token).ConfigureAwait(false);
                    }
                    catch (AssuranceRefusal error) { return Result(DriverStatus.Blocked, error.Code, error.Message); }
                }
                await TransitionAsync(kind == CognitiveWorkKind.Verification ? TaskStage.Review : TaskStage.Verification, request, token).ConfigureAwait(false);
                return null;
            default:
                return Result(DriverStatus.Unsupported, "unknown_disposition", "Unsupported judgment; no action dispatched.");
        }
    }

    private async Task<DriverResult?> CompleteAcceptedAsync(WorkItemId work, DriverRequest request, CancellationToken token)
    {
        try
        {
            _mutationInFlight = true;
            var outcome = await kernel.CompleteAsync(work, _state!.Version, token).ConfigureAwait(false);
            _mutationInFlight = false;
            _state = outcome.State;
            _transitions.AddRange(outcome.Events.Select(e => e.EventId));
            await TransitionAsync(TaskStage.Learn, request, token).ConfigureAwait(false);
            return null;
        }
        catch (AssuranceRefusal error)
        {
            _mutationInFlight = false;
            if (error.Code == "unresolved_findings" && options.Agents.ContainsKey(CognitiveWorkKind.Findings) &&
                _investigated.Add("assurance-findings/" + _candidate))
            {
                _recovery = new(_state!.Stage, _state.Version, "acceptance/completion", error.Code, error.Message, null);
                var judgment = await DispatchAsync(CognitiveWorkKind.Findings, null, null, token).ConfigureAwait(false);
                if (judgment.Stop is not null) return judgment.Stop;
                if (judgment.Assessment!.Disposition is RoutingDisposition.Repair or RoutingDisposition.Replan)
                    return await ApplyWorkJudgmentAsync(CognitiveWorkKind.Findings, work, judgment.Assessment, request, token).ConfigureAwait(false);
                // Re-enter owning admission once. Proceed itself supplies no disposition or acceptance.
                return await CompleteAcceptedAsync(work, request, token).ConfigureAwait(false);
            }
            return Result(DriverStatus.AwaitingAcceptance, error.Code, error.Message);
        }
    }

    private async Task<DriverResult?> RepairInExecutionAsync(WorkItemId work, DriverRequest request, CancellationToken token)
    {
        // A repair count is never a success criterion. Continue only on an explicit judgment.
        while (true)
        {
            var preparationStop = await PrepareAssuranceAsync(work, token).ConfigureAwait(false);
            if (preparationStop is not null) return preparationStop;
            var repair = await DispatchAsync(CognitiveWorkKind.Repair, work, null, token).ConfigureAwait(false);
            if (repair.Stop is not null) return repair.Stop;
            switch (repair.Assessment!.Disposition)
            {
                case RoutingDisposition.Repair:
                    return await InvestigateRepairFailureAsync(work, request, token).ConfigureAwait(false);
                case RoutingDisposition.Proceed:
                    await TransitionAsync(TaskStage.Verification, request, token).ConfigureAwait(false);
                    return null;
                case RoutingDisposition.Replan:
                    await ReplanAsync(request, token).ConfigureAwait(false);
                    return null;
                default: return Result(DriverStatus.Blocked, "repair_judgment_pending", repair.Assessment.Rationale);
            }
        }
    }

    private async Task<DriverResult?> InvestigateRepairFailureAsync(WorkItemId work, DriverRequest request, CancellationToken token)
    {
        _recovery = new(_state!.Stage, _state.Version, "repair-induced or repeated defect", "repair_investigation",
            "Retain original findings and independent dispositions. Investigate root cause and scope/dependency assumptions, then request Replan for Design-stage lesson reconsideration; unresolved judgment must remain Blocked.",
            WorkItemRunObservations.LatestCompletedWork(_state, work)?.Id);
        var investigation = await DispatchAsync(CognitiveWorkKind.Findings, null, null, token).ConfigureAwait(false);
        if (investigation.Stop is not null) return investigation.Stop;
        if (investigation.Assessment!.Disposition != RoutingDisposition.Replan)
            return Result(DriverStatus.Blocked, "repair_investigation_unresolved", "Repeated or introduced defect needs evidenced scope/design reconsideration; generic Proceed or another patch cannot close the investigation.");
        await ReplanAsync(request, token).ConfigureAwait(false);
        return null;
    }

    private async Task ReplanAsync(DriverRequest request, CancellationToken token)
    {
        _replanned = true;
        if (_state!.Stage == TaskStage.Review) await TransitionAsync(TaskStage.Repair, request, token).ConfigureAwait(false);
        if (_state.Stage is TaskStage.Repair or TaskStage.Verification) await TransitionAsync(TaskStage.Execution, request, token).ConfigureAwait(false);
        await TransitionAsync(TaskStage.Design, request, token).ConfigureAwait(false);
    }

    private async Task<DriverResult?> PrepareAssuranceAsync(WorkItemId work, CancellationToken token)
    {
        var configured = options.Dispatch;
        if (configured.AssuranceAuthority is null || configured.AssuranceStore is null ||
            !options.Agents.TryGetValue(CognitiveWorkKind.Verification, out var profile))
            return Result(DriverStatus.Blocked, "missing_assurance_configuration", "Supply protected task-13 authority/store and an independently assigned verifier before implementation.");
        if (!_state!.Roles.TryGetValue(profile.Subject, out var assignment) || assignment.Role != RoleKind.Verifier)
            return Result(DriverStatus.Blocked, "invalid_verifier_profile", "Configured verifier must hold the governed Verifier role.");
        var lastWorker = WorkItemRunObservations.LatestCompletedWork(_state, work);
        if (lastWorker is not null && string.Equals(lastWorker.Provider, profile.Provider, StringComparison.OrdinalIgnoreCase) ||
            options.Agents.TryGetValue(CognitiveWorkKind.Implementation, out var workerProfile) &&
            string.Equals(workerProfile.Provider, profile.Provider, StringComparison.OrdinalIgnoreCase))
            return Result(DriverStatus.Blocked, "independent_provider_required", "Choose a verifier provider independent of the actual and configured working provider.");
        try
        {
            var grants = ProviderGrantResolver.Resolve(_state, options.ActorId, work, null, [], configured.LedgerRoot);
            var policy = await AssuranceHost.PrepareAsync(configured.AssuranceAuthority, configured.AssuranceStore, profile.Subject.Value,
                assignment.Role.ToString(), new[] { grants.WorkingDirectory }.Concat(grants.AdditionalDirectories), configured.LedgerRoot, token).ConfigureAwait(false);
            if (options.AcceptancePrincipal is not null && (policy.Governed is not { SchemaVersion: 1 } association ||
                association.TaskId != _state.TaskId.Value || !association.WorkItemIds.SequenceEqual(new[] { work.Value })))
                return Result(DriverStatus.Blocked, "missing_governed_association", "The supported completion profile requires an explicit association with this task and selected single work member.");
            var longestBatch = Math.Min(300, policy.Checks.OrderByDescending(c => c.TimeoutSeconds).Take(8).Sum(c => c.TimeoutSeconds));
            if (options.OwnerLifetime.TotalSeconds < (longestBatch + 30) * 1.5)
                return Result(DriverStatus.Blocked, "owner_lease_too_short", "Owner lease must cover a bounded check batch plus IO margin while heartbeat waits for the task lock.");
            _candidate = await AILedger.Providers.Assurance.AssuranceConfiguration.CaptureBindingIdentityAsync(policy, profile.Subject.Value, token).ConfigureAwait(false);
            return null;
        }
        catch (AssuranceRefusal refusal)
        { return Result(refusal.Code is "incompatible_context" or "unsupported" ? DriverStatus.Unsupported : DriverStatus.Blocked, refusal.Code, refusal.Message); }
        catch (Exception error) when (error is IOException or ArgumentException or System.Text.Json.JsonException)
        { return Result(DriverStatus.Blocked, "assurance_configuration_unavailable", error.Message); }
    }
}
