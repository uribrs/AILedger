using System.Text.Json;
using AILedger.Core.Assurance;
using AILedger.Cli.Dispatch;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
namespace AILedger.Cli.Orchestration;

// Deterministic router inside the durable host. Owning assurance validates explicit acceptance.
internal sealed partial class OrchestrationDriver(DriverKernel kernel, DriverHostOptions options,
    IProviderDispatchService standard, IProviderDispatchService verifier, IProviderDispatchService reviewer, DriverPreparation? preparation = null) : IOrchestrationDriver
{
    private readonly List<ProviderDispatchResult> _dispatches = [];
    private readonly List<EventId> _transitions = [];
    private GovernedTaskState? _state;
    private bool _replanned;
    private WorkItemId? _preparedWork;
    private bool _mutationInFlight;
    private int _invoked;

    public async Task<DriverResult> DriveAsync(DriverRequest request, CancellationToken token)
    {
        if (Interlocked.Exchange(ref _invoked, 1) != 0)
            return Result(DriverStatus.Unsupported, "single_invocation", "Compose a new driver only after reconciling the previous invocation; Part 5 owns recovery.");
        try
        {
            await RefreshAsync(token).ConfigureAwait(false);
            if (request.WorkItemId is null && preparation?.SingleSelectedWork(_state!) is { } prepared)
                request = request with { WorkItemId = prepared };
            if (_state!.Stage == TaskStage.Archive)
                return Result(DriverStatus.Archived, "already_archived", "The ledger is archived; terminal observation only.");
            if (_state.Runs.Values.Any(run => run.Status == AgentRunStatus.Active))
                return Result(DriverStatus.Blocked, "active_run", "An active run requires its owning launcher or reconciliation; no new dispatch.");
            if (request.WorkItemId is { } selected)
            {
                var latest = _state.Runs.Values.Where(r => r.WorkItemId == selected && r.SubjectRole is RoleKind.Worker or RoleKind.Researcher)
                    .OrderByDescending(r => r.StartedAt).FirstOrDefault();
                if (latest?.Status is AgentRunStatus.Failed or AgentRunStatus.Cancelled)
                {
                    var unresolved = await RecoverInterruptedWorkAsync(latest, request, token).ConfigureAwait(false);
                    if (unresolved is not null) return unresolved;
                }
            }
            _mutationInFlight = true;
            await kernel.BriefAsync(token).ConfigureAwait(false);
            _mutationInFlight = false;
            if (preparation is not null)
            {
                await preparation.StaffAsync(token).ConfigureAwait(false);
                await RefreshAsync(token).ConfigureAwait(false);
                _preparedWork = preparation.SingleSelectedWork(_state!);
            }
            while (true)
            {
                token.ThrowIfCancellationRequested();
                await RefreshAsync(token).ConfigureAwait(false);
                if (request.WorkItemId is null && _preparedWork is { } selectedWork) request = request with { WorkItemId = selectedWork };
                if (OpenEscalations().Length > 0)
                    return Result(DriverStatus.AwaitingHumanDecision, "open_escalation", "Resolve the attached business decision or investigated unknown through its separate trusted path.");
                DriverResult? stopped;
                try { stopped = await RouteStageAsync(request, token).ConfigureAwait(false); }
                catch (AssuranceRefusal error)
                { _mutationInFlight = false; stopped = Result(DriverStatus.AwaitingAcceptance, error.Code, error.Message); }
                catch (GovernanceException error)
                { stopped = Result(DriverStatus.Blocked, error.Kind.ToString(), error.Message); }
                if (stopped is { Status: DriverStatus.Blocked, Code: "Opaque" or "StaleBasis" })
                    stopped = await RecoverAsync(stopped, request, token).ConfigureAwait(false);
                if (stopped is not null) return stopped;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { return Result(_mutationInFlight ? DriverStatus.UnknownOutcome : DriverStatus.Cancelled,
            _mutationInFlight ? "unknown_mutation" : "cancelled", "Driver cancelled; retain receipts and reconcile any in-flight mutation before another invocation."); }
        catch (GovernanceException error)
        { return Result(DriverStatus.Blocked, "kernel_refusal", error.Message); }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { return Result(DriverStatus.UnknownOutcome, "unavailable_or_unknown", error.Message); }
    }

    private async Task<DriverResult?> RouteStageAsync(DriverRequest request, CancellationToken token)
    {
        switch (_state!.Stage)
        {
            case TaskStage.Discovery:
                if (!ArtifactApplicability.Current(_state).Any(a => a.Kind == GovernedArtifactKind.UserRequest))
                    return Result(DriverStatus.Blocked, "trusted_intake_required", "An operator-authored UserRequest must be prepared through the trusted intake path.");
                return await CognitivePhaseAsync(CognitiveWorkKind.Discovery, TaskStage.Research, token).ConfigureAwait(false);
            case TaskStage.Research:
                return await ResearchAsync(token).ConfigureAwait(false);
            case TaskStage.Design:
                return await CognitivePhaseAsync(CognitiveWorkKind.Design, TaskStage.Scope, token).ConfigureAwait(false);
            case TaskStage.Scope:
                return await CognitivePhaseAsync(CognitiveWorkKind.Scope, TaskStage.Ready, token).ConfigureAwait(false);
            case TaskStage.Ready:
                if (preparation is not null)
                {
                    await preparation.PrepareWorkAsync(token).ConfigureAwait(false);
                    await RefreshAsync(token).ConfigureAwait(false);
                    _replanned = false;
                    _preparedWork = preparation.SingleSelectedWork(_state!);
                }
                var selectedItem = request.WorkItemId ?? _preparedWork;
                if (_replanned || selectedItem is null || !_state.WorkItems.ContainsKey(selectedItem.Value))
                    return Result(DriverStatus.Unsupported, "prepared_scope_required", "Prepare current scoped work and role assignments through the trusted path, then explicitly select an item.");
                await TransitionAsync(TaskStage.Execution, request, token).ConfigureAwait(false);
                return null;
            case TaskStage.Execution:
            case TaskStage.Repair:
            case TaskStage.Verification:
            case TaskStage.Review:
                return await WorkPhaseAsync(request, token).ConfigureAwait(false);
            case TaskStage.Learn:
                if (_state.WorkItems.Values.Any(w => w.Status is not (WorkItemStatus.Completed or WorkItemStatus.Abandoned)))
                    return Result(DriverStatus.AwaitingAcceptance, "completion_boundary", "All work must be independently accepted/completed before closeout; remaining members are unresolved.");
                if (request.WorkItemId is { } completed && options.AcceptancePrincipal is not null)
                {
                    await kernel.ObserveCompletedWorkAsync(completed, token).ConfigureAwait(false);
                    var configuration = await PrepareAssuranceAsync(completed, token).ConfigureAwait(false);
                    if (configuration is not null) return configuration;
                }
                return await CognitivePhaseAsync(CognitiveWorkKind.Closeout, TaskStage.Archive, token, request).ConfigureAwait(false);
            case TaskStage.Archive:
                return Result(DriverStatus.Archived, "already_archived", "The ledger is archived. No completion or correctness is inferred by the driver.");
            default:
                return Result(DriverStatus.Unsupported, "unsupported_stage", "No routing contract exists for this stage.");
        }
    }

    private async Task<DriverResult?> CognitivePhaseAsync(CognitiveWorkKind work, TaskStage next, CancellationToken token, DriverRequest? request = null)
    {
        var result = await DispatchAsync(work, null, null, token).ConfigureAwait(false);
        if (result.Stop is not null) return result.Stop;
        if (result.Assessment!.Disposition != RoutingDisposition.Proceed)
            return Result(DriverStatus.Blocked, "cognitive_judgment_pending", result.Assessment.Rationale);
        await TransitionAsync(next, request ?? new(), token).ConfigureAwait(false);
        return null;
    }

    private async Task<DriverResult?> ResearchAsync(CancellationToken token)
    {
        var recon = await DispatchAsync(CognitiveWorkKind.Recon, null, null, token).ConfigureAwait(false);
        if (recon.Stop is not null) return recon.Stop;
        if (recon.Assessment!.Disposition != RoutingDisposition.Proceed)
            return Result(DriverStatus.Blocked, "recon_judgment_pending", recon.Assessment.Rationale);
        var artifact = ArtifactApplicability.Current(_state!).SingleOrDefault(a => a.Kind == GovernedArtifactKind.InternalRecon);
        if (artifact is null) return Result(DriverStatus.Blocked, "missing_recon", "The lead must file governed recon while active.");
        // InternalRecon is an existing strict typed contract, not prose or advisory action order.
        var document = JsonSerializer.Deserialize<InternalReconDocument>(artifact.Content, LedgerJson.CreateOptions())!;
        if (document.Assessments.Any(row => row.Domain == "external"))
        {
            var research = await DispatchAsync(CognitiveWorkKind.Research, null, null, token).ConfigureAwait(false);
            if (research.Stop is not null) return research.Stop;
            if (research.Assessment!.Disposition != RoutingDisposition.Proceed)
                return Result(DriverStatus.Blocked, "research_judgment_pending", research.Assessment.Rationale);
            // Research may settle claims and invalidate the recon hash. The lead re-assesses the
            // complete current claim set; the owning stage gate checks consultation/currency.
            recon = await DispatchAsync(CognitiveWorkKind.Recon, null, null, token).ConfigureAwait(false);
            if (recon.Stop is not null) return recon.Stop;
            if (recon.Assessment!.Disposition != RoutingDisposition.Proceed)
                return Result(DriverStatus.Blocked, "recon_judgment_pending", recon.Assessment.Rationale);
        }
        await TransitionAsync(TaskStage.Design, new(), token).ConfigureAwait(false);
        return null;
    }

    private async Task RefreshAsync(CancellationToken token) => _state = await kernel.ReadAsync(token).ConfigureAwait(false)
        ?? throw new InvalidDataException("Prepared task unavailable.");
    private Escalation[] OpenEscalations() => _state?.Escalations.Values.Where(e => e.Status == EscalationStatus.Open).ToArray() ?? [];
    private DriverResult Result(DriverStatus status, string code, string diagnostic) =>
        new(status, code, diagnostic, _state?.Stage, _state?.Version, _dispatches.ToArray(), _transitions.ToArray(), OpenEscalations());
    private async Task TransitionAsync(TaskStage next, DriverRequest request, CancellationToken token)
    {
        _attemptedAction = $"transition {_state!.Stage} to {next}";
        var backward = StageTransitionPolicy.IsBackward(_state!.Stage, next);
        _mutationInFlight = true;
        var outcome = await kernel.TransitionAsync(next, backward ? "Evidence-backed driver routing; see the producing run's structured assessment." : null,
            request.SerialJustification, _state.Version, token, next is TaskStage.Learn or TaskStage.Archive ? request.WorkItemId : null).ConfigureAwait(false);
        _mutationInFlight = false;
        _transitions.AddRange(outcome.Events.Select(e => e.EventId));
        _state = outcome.State;
    }
}
