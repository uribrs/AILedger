using AILedger.Cli.Dispatch;
using AILedger.Core.Contracts;
namespace AILedger.Cli.Orchestration;

internal sealed partial class OrchestrationDriver
{
    private sealed record CognitiveResult(RoutingAssessment? Assessment = null, DriverResult? Stop = null);

    private async Task<CognitiveResult> DispatchAsync(CognitiveWorkKind work, WorkItemId? item, RunId? paired, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _attemptedAction = $"dispatch {work} for {item?.Value ?? "task"}";
        if (!options.Agents.TryGetValue(work, out var profile))
            return new(Stop: Result(DriverStatus.Unsupported, "unconfigured_cognitive_role", $"No trusted profile for {work}."));
        if (!_state!.Roles.TryGetValue(profile.Subject, out var assignment) || !RoleFits(work, assignment.Role))
            return new(Stop: Result(DriverStatus.Blocked, "incompatible_role", "Configured cognitive work does not match its governed role."));
        var run = new RunId("driver-" + Guid.NewGuid().ToString("N"));
        var input = new ProviderDispatchRequest(options.TaskId, options.ActorId, run, profile.Provider)
        {
            RoutingCandidate = _candidate,
            Recovery = work == CognitiveWorkKind.Findings ? _recovery : null,
            SubjectActorId = profile.Subject, WorkItemId = item, Model = profile.Model,
            WorkingDirectory = item is null ? options.WorkingDirectory : null,
            CognitiveWork = work, TimeoutSeconds = options.TimeoutSeconds, VerifierRun = paired,
            Candidate = work is CognitiveWorkKind.Verification or CognitiveWorkKind.Review ? _candidate : null
        };
        var service = work == CognitiveWorkKind.Verification || work == CognitiveWorkKind.Findings && options.AcceptancePrincipal is not null
            ? verifier : work == CognitiveWorkKind.Review ? reviewer : standard;
        var receipt = await service.LaunchAsync(input, token).ConfigureAwait(false);
        if (_dispatches.Any(d => d.RunId == receipt.RunId))
            return new(Stop: Result(DriverStatus.Blocked, "no_progress", "The same judgment has already been consumed without progress; retained work remains unresolved."));
        _dispatches.Add(receipt);
        if (receipt.StartRecording == LedgerRecordingStatus.Unknown || receipt.CompletionRecording == LedgerRecordingStatus.Unknown || receipt.CognitiveHandoffUnknown)
            return new(Stop: Result(DriverStatus.UnknownOutcome, "unknown_recording", "Preserve dispatch and handoff receipts; no automatic retry after uncertain execution."));
        if (receipt.Failure is { } failure)
            return new(Stop: Result(failure.Kind == DispatchFailureKind.Cancelled ? DriverStatus.Cancelled :
                failure.Kind == DispatchFailureKind.ProcessCleanupUnconfirmed ? DriverStatus.UnknownOutcome :
                failure.Kind == DispatchFailureKind.PreparationUnsupported ? DriverStatus.Unsupported : DriverStatus.Blocked,
                failure.Code ?? failure.Kind.ToString(), failure.Diagnostic +
                (DriverContinuity.IsInfrastructureFailure(receipt) ?
                    " Infrastructure stop: inspect the retained result and host configuration; no automatic Findings retry. A recorded failure does not attest process-tree termination. Reconcile uncertain execution before an explicit relaunch." : "")));
        if (receipt.Completed?.Status != AgentRunStatus.Completed || receipt.Retention.Status != ResultRetentionStatus.Retained)
            return new(Stop: Result(DriverStatus.UnknownOutcome, "incomplete_receipt", "No confirmed closure and retained result; reconcile before further dispatch."));
        await RefreshAsync(token).ConfigureAwait(false);
        if (OpenEscalations().Length > 0)
            return new(Stop: Result(DriverStatus.AwaitingHumanDecision, "open_escalation", "The producer recorded a business decision or investigated unknown; use the separate trusted resolution path."));
        // Completion is transport/lifecycle evidence only. An explicit recorded judgment is
        // mandatory even when output artifacts and all process statuses look successful.
        var producer = _state!.Runs[receipt.RunId];
        var assessment = producer.RoutingAssessment;
        if (assessment?.Disposition == RoutingDisposition.Proceed &&
            (producer.ProducerOutcome is { Outcome: not "reported-complete" } ||
             _state.Challenges.Values.Any(c => c.Status == ChallengeStatus.Open)))
            return new(Stop: Result(DriverStatus.Blocked, "contradictory_findings", "Routing assessment cannot override producer blockers or unresolved challenges."));
        if (assessment?.Disposition == RoutingDisposition.Proceed && work == CognitiveWorkKind.Verification)
        {
            var output = ArtifactApplicability.Current(_state).SingleOrDefault(a => a.Kind == GovernedArtifactKind.VerifierOutput && a.ProducerRunId == producer.Id);
            if (output is not null)
            {
                var findings = VerifierOutputDocuments.Observe(output);
                if (findings.ClaimStatuses.GetValueOrDefault("REJECTED") > 0 || findings.ClaimStatuses.GetValueOrDefault("NEVER-TESTED") > 0 ||
                    findings.AttentionDispositions.GetValueOrDefault("unresolved") > 0)
                    return new(Stop: Result(DriverStatus.Blocked, "contradictory_findings", "The owning verifier output contains unresolved or adverse dispositions. A generic Proceed does not override them."));
            }
        }
        if (assessment is null || assessment.Work != work)
            return new(Stop: Result(DriverStatus.Blocked, "missing_routing_assessment", "Producer did not record the assigned structured judgment; no correctness inferred from its return."));
        return new(assessment);
    }
    private static bool RoleFits(CognitiveWorkKind work, RoleKind role) => work switch
    {
        CognitiveWorkKind.Implementation or CognitiveWorkKind.Repair => role == RoleKind.Worker,
        CognitiveWorkKind.Verification => role == RoleKind.Verifier,
        CognitiveWorkKind.Review => role == RoleKind.CodeReviewer,
        CognitiveWorkKind.Research => role == RoleKind.Researcher,
        _ => role is RoleKind.PlanningLead or RoleKind.ImplementationLead
    };
}
