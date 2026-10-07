using AILedger.Cli.Dispatch;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
namespace AILedger.Cli.Orchestration;

internal sealed partial class OrchestrationDriver
{
    private DriverRecoveryObservation? _recovery;
    private string _attemptedAction = "startup";
    private readonly HashSet<string> _investigated = new(StringComparer.Ordinal);

    private async Task<DriverResult?> RecoverInterruptedWorkAsync(AgentRun interrupted, DriverRequest request, CancellationToken token)
    {
        var unknown = Result(DriverStatus.UnknownOutcome, "interrupted_work", "Unsuccessful work may have changed physical inputs; no old assurance is reused.");
        if (!options.Agents.ContainsKey(CognitiveWorkKind.Findings)) return unknown;
        if (!AILedger.Core.Assurance.GovernedAssuranceRules.HasStoppedEvidence(_state!, interrupted))
            return Result(DriverStatus.UnknownOutcome, "process_termination_unconfirmed", "Failed/cancelled execution requires trusted process-tree termination evidence before physical reconciliation. Lease expiry is not termination.");
        var configuration = await PrepareAssuranceAsync(request.WorkItemId!.Value, token).ConfigureAwait(false);
        if (configuration is not null) return configuration;
        await kernel.BriefAsync(token).ConfigureAwait(false);
        await RefreshAsync(token).ConfigureAwait(false);
        _recovery = new(_state!.Stage, _state.Version, "interrupted work", "physical_candidate_reconciliation",
            "Inspect current physical inputs against the interrupted run and retained evidence. A repair requires fresh independent verification; Proceed cannot reuse old assurance.", interrupted.Id);
        var finding = await DispatchAsync(CognitiveWorkKind.Findings, null, null, token).ConfigureAwait(false);
        if (finding.Stop is not null) return finding.Stop;
        if (finding.Assessment!.Disposition == RoutingDisposition.Replan)
        { await ReplanAsync(request, token).ConfigureAwait(false); return null; }
        if (finding.Assessment.Disposition == RoutingDisposition.Repair)
        {
            if (_state.Stage is TaskStage.Verification or TaskStage.Review) await TransitionAsync(TaskStage.Repair, request, token).ConfigureAwait(false);
            return null;
        }
        return unknown;
    }

    private async Task<DriverResult?> RecoverAsync(DriverResult refusal, DriverRequest request, CancellationToken token)
    {
        var prior = DriverContinuity.Basis(_state!, _candidate);
        var key = prior + "/" + refusal.Code;
        if (!_investigated.Add(key)) return Result(DriverStatus.Blocked, "no_progress", "Unchanged refusal after investigation; evidence and unresolved work are retained.");
        await RefreshAsync(token).ConfigureAwait(false);
        await kernel.BriefAsync(token).ConfigureAwait(false);
        await RefreshAsync(token).ConfigureAwait(false);
        if (refusal.Code == nameof(GovernanceRefusalKind.StaleBasis)) return null;
        if (!options.Agents.ContainsKey(CognitiveWorkKind.Findings)) return refusal;
        _recovery = new(_state!.Stage, _state.Version, _attemptedAction, refusal.Code, refusal.Diagnostic, _dispatches.LastOrDefault()?.RunId);
        var investigation = await DispatchAsync(CognitiveWorkKind.Findings, null, null, token).ConfigureAwait(false);
        if (investigation.Stop is not null) return investigation.Stop;
        if (investigation.Assessment!.Disposition == RoutingDisposition.Replan && _state.Stage is TaskStage.Execution or TaskStage.Verification or TaskStage.Repair or TaskStage.Review)
        {
            await ReplanAsync(request, token).ConfigureAwait(false);
            return null;
        }
        // A Proceed label cannot repair a missing prerequisite. Only changed authoritative basis
        // permits a fresh attempt, whose owning kernel rules still decide admission.
        if (DriverContinuity.Basis(_state, _candidate) != prior) return null;
        return Result(DriverStatus.Blocked, "investigation_unresolved", "Investigation returned without repairing the refused prerequisite. The owning diagnostic remains retained; no waiver or acceptance was created.");
    }
}
