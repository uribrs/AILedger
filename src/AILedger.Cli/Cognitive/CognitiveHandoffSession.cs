using System.Text.Json;
using AILedger.Cli.Dispatch;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
namespace AILedger.Cli.Cognitive;

// The cache bounds transport work. File storage commits exact key/body/binding receipts with
// the governed events, so reconnect/restart does not depend on this instance surviving.
internal sealed class CognitiveHandoffSession(IGovernedTaskService service, CognitiveHostBinding binding,
    CognitiveWorkKind? assignment = null, ReconSourceAccess? sourceAccess = null)
{
    internal IReadOnlyList<HostHandoffReceipt> Receipts => _receipts.Values.Select(row => row.Receipt).ToArray();
    internal bool HasUnknown { get; private set; }
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, (string Body, HostHandoffReceipt Receipt)> _receipts = new(StringComparer.Ordinal);

    internal async Task<object> DescribeAsync(CancellationToken token)
    {
        var state = await service.GetStateAsync(binding.TaskId, token).ConfigureAwait(false);
        if (state is null || !state.Runs.TryGetValue(binding.RunId, out var run) ||
            run.Status != AgentRunStatus.Active || run.ActorId != binding.Subject ||
            !state.Roles.TryGetValue(binding.Subject, out var role) || role.Role != run.SubjectRole ||
            !role.Capabilities.Contains(Capability.BuildContext))
            return CognitiveHandoffTools.Describe();

        // Discovery only. Command admission still checks the current role, stage and purpose;
        // schema visibility cannot authorize a mutation or relax historical replay.
        if (role.Role == RoleKind.Researcher && state.Stage == TaskStage.Research)
            return CognitiveHandoffTools.Describe(["research"]);
        if (run.WorkItemId is null && run.Assurance is null &&
            role.Role is RoleKind.Operator or RoleKind.PlanningLead or RoleKind.ImplementationLead)
            return CognitiveHandoffTools.Describe(state.Stage switch
            {
                TaskStage.Research => ["recon"],
                TaskStage.Design => ["recon", "reconsideration"],
                _ => []
            });
        return CognitiveHandoffTools.Describe();
    }

    internal async Task<HostHandoffReceipt> InvokeAsync(CognitiveHostHandoff request, CancellationToken token)
    {
        var body = JsonSerializer.Serialize(request.Operation, request.Operation.GetType(), LedgerJson.CreateOptions());
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_receipts.TryGetValue(request.RequestId, out var prior) && prior.Body != body)
                return Refused(request, "Request ID already belongs to different content.");
            if (prior.Receipt?.Status == HostHandoffStatus.Unknown && service is not AILedger.Core.Authority.IDurableHostRequests)
                return prior.Receipt;
            if (HasUnknown && !_receipts.ContainsKey(request.RequestId)) return new(HostHandoffStatus.Unknown, request.RequestId, [],
                Diagnostic: "A prior session mutation is uncertain; reconcile it before submitting different content.");
            if (_receipts.Count >= 128 && !_receipts.ContainsKey(request.RequestId)) return Refused(request, "Session handoff capacity reached; no mutation attempted.");
            HostHandoffReceipt receipt;
            LedgerCommand? command = null;
            try
            {
                var state = await service.GetStateAsync(binding.TaskId, token).ConfigureAwait(false)
                    ?? throw new GovernanceException("Bound task unavailable.");
                var run = state.Runs[binding.RunId];
                if (run.Status != AgentRunStatus.Active || run.ActorId != binding.Subject)
                    throw new GovernanceException("Cognitive handoff requires the bound active producer.");
                if (request.Operation is ReconTemplateHandoff template)
                    return await ReadReconTemplateAsync(request, template, state, run, token).ConfigureAwait(false);
                if (request.Operation is GoverningArtifactHandoff { Kind: GoverningArtifactHandoffKind.InternalRecon } recon)
                    (sourceAccess ?? new ReconSourceAccess([], "/")).EnsureAllowed(recon.Markdown);
                if (request.Operation is RoutingAssessmentHandoff assessment && assignment is { } expected && assessment.Assessment.Work != expected)
                    throw new GovernanceException("Assessment does not match the host-assigned cognitive work.");
                if (request.Operation is RoutingAssessmentHandoff or DecisionResolutionHandoff or PreparationSelectionHandoff)
                    await EnsureJudgmentBasisAsync(state, run, request.Operation, token).ConfigureAwait(false);
                if (request.Operation is DecisionResolutionHandoff resolution)
                    ValidateResolution(state, run, resolution);
                command = Command(request.Operation, run, request.RequestId) with
                { HostRequest = new(1, $"{binding.TaskId.Value}/{binding.Subject.Value}/{binding.RunId.Value}/{binding.Session}", request.RequestId, body),
                  ExpectedVersion = request.Operation is DecisionResolutionHandoff resolutionBasis ? resolutionBasis.ExpectedVersion : state.Version };
                var outcome = await service.ExecuteAsync(binding.TaskId, command, token).ConfigureAwait(false);
                var consulted = outcome.Events.Select(e => e.Data).OfType<LessonsConsulted>().SingleOrDefault();
                var artifact = outcome.Events.Select(e => e.Data).OfType<ArtifactRecorded>().SingleOrDefault();
                receipt = new(HostHandoffStatus.Recorded, request.RequestId, outcome.Events.Select(e => e.EventId).ToArray(),
                    artifact?.Artifact.ArtifactId, Lessons: consulted?.ServedLessonIds.Select(id => outcome.State.Lessons[id]).ToArray(),
                    RecordId: Identity(command), TaskVersion: long.Parse(outcome.Events[^1].EventId.Value.Split(':')[^1], System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (GovernanceException error) { receipt = Refused(request, error.Message); }
            catch (NotSupportedException error) { receipt = new(HostHandoffStatus.Unsupported, request.RequestId, [], Diagnostic: error.Message); }
            catch (Exception error) when (command is null && error is (IOException or UnauthorizedAccessException or ArgumentException or JsonException))
            {
                receipt = Refused(request, "No mutation attempted: " + error.Message);
            }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                receipt = new(HostHandoffStatus.Unknown, request.RequestId, [],
                    ArtifactId: (command as RecordArtifactCommand)?.ArtifactId,
                    RecordId: command is null ? null : Identity(command),
                    Diagnostic: "Outcome unknown. Preserve this binding/key/body; do not repeat the mutation under a new key. Retry the original body/key/binding to reconcile the atomic event receipt.");
            }
            _receipts[request.RequestId] = (body, receipt);
            HasUnknown = _receipts.Values.Any(row => row.Receipt.Status == HostHandoffStatus.Unknown);
            return receipt;
        }
        finally { _gate.Release(); }
    }

    private async Task<HostHandoffReceipt> ReadReconTemplateAsync(CognitiveHostHandoff request,
        ReconTemplateHandoff template, GovernedTaskState state, AgentRun run, CancellationToken token)
    {
        // This is authoring input, not an artifact or a judgment. Do not cache a stale
        // claim hash as an idempotent write receipt or manufacture any ledger event.
        if (state.Stage is not (TaskStage.Research or TaskStage.Design) ||
            run.WorkItemId is not null || run.Assurance is not null ||
            run.SubjectRole is not (RoleKind.Operator or RoleKind.PlanningLead or RoleKind.ImplementationLead) ||
            !state.Roles.TryGetValue(binding.Subject, out var role) ||
            role.Role is not (RoleKind.Operator or RoleKind.PlanningLead or RoleKind.ImplementationLead) ||
            !role.Capabilities.Contains(Capability.BuildContext))
            throw new GovernanceException("Recon template requires a current task-wide lead at Research or Design.");
        var paths = (sourceAccess ?? new ReconSourceAccess([], "/")).Resolve(template.SourcePaths ?? []);
        var files = await ReconSourceFiles.ObserveAsync(paths, token).ConfigureAwait(false);
        return new(HostHandoffStatus.Observed, request.RequestId, [], TaskVersion: state.Version,
            ReconTemplate: JsonSerializer.SerializeToElement(InternalReconDocuments.CreateSourceReviewTemplate(state, files)));
    }

    private LedgerCommand Command(CognitiveHostOperation operation, AgentRun run, string requestId)
    {
        var id = "host-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{binding.TaskId}/{binding.Subject}/{binding.RunId}/{binding.Session}/{requestId}"))).ToLowerInvariant();
        return operation switch
        {
            GoverningArtifactHandoff artifact => new RecordArtifactCommand(binding.Subject, null, binding.RunId.Value,
                new(id), ArtifactKind(artifact.Kind), artifact.Title, artifact.Markdown, null, binding.RunId, artifact.Supersedes),
            LessonConsultationHandoff lesson => new ConsultLessonsCommand(binding.Subject, null, binding.RunId.Value,
                binding.RunId, lesson.Purpose, lesson.Question, lesson.Tags, lesson.Claims),
            RoutingAssessmentHandoff assessment => new RecordRoutingAssessmentCommand(binding.Subject, null,
                binding.RunId.Value, binding.RunId, assessment.Assessment),
            EscalationHandoff escalation => new RaiseEscalationCommand(binding.Subject, null, binding.RunId.Value,
                new(id), escalation.Kind, escalation.Question, run.WorkItemId, escalation.Options, escalation.Recommendation, escalation.EvidenceIds),
            DecisionProposalHandoff decision => new ProposeDecisionCommand(binding.Subject, null, binding.RunId.Value,
                new(id), decision.Statement, decision.Rationale, decision.Claims, null),
            DecisionResolutionHandoff resolution => new ResolveDecisionCommand(binding.Subject, null, binding.RunId.Value,
                resolution.Decision, resolution.Status) { ExpectedVersion = resolution.ExpectedVersion },
            PreparationSelectionHandoff selection => new SelectPreparationCommand(binding.Subject, null, binding.RunId.Value,
                binding.RunId, selection.Selection),
            LessonMarkHandoff mark => new MarkLessonBearingCommand(binding.Subject, null, binding.RunId.Value,
                mark.SourceKind, mark.SourceId, Class: mark.Class, Repo: mark.Repository, Tags: mark.Tags,
                Verify: mark.Verify, DoNot: mark.DoNot, Actor: mark.LessonActor, VerifyExpects: mark.VerifyExpects),
            CloseoutSynthesisHandoff closeout => new RecordArtifactCommand(binding.Subject, null, binding.RunId.Value,
                new(id), GovernedArtifactKind.CloseoutSynthesis, closeout.Title, closeout.Markdown, null, null, closeout.Supersedes),
            _ => throw new NotSupportedException("Unsupported cognitive operation.")
        };
    }

    private async Task EnsureJudgmentBasisAsync(GovernedTaskState state, AgentRun run,
        CognitiveHostOperation operation, CancellationToken token)
    {
        var workerReport = operation is RoutingAssessmentHandoff && run.SubjectRole == RoleKind.Worker &&
            run.WorkItemId is not null;
        var dependencies = workerReport
            ? new AILedger.Core.Application.ContextAssembler().BuildForRun(state, run.ActorId, run.Id, [], DateTimeOffset.UtcNow)
                .Artifacts.Where(artifact => artifact.Kind == ContextArtifactKind.Claim)
                .Select(artifact => new ClaimId(artifact.Id)).ToHashSet()
            : [];
        await foreach (var row in service.GetHistoryAsync(binding.TaskId, token).ConfigureAwait(false))
        {
            if (row.RecordedAt < run.StartedAt || row.CorrelationId == binding.RunId.Value ||
                row.Data is RunStarted or RunCompleted or ContextBuilt) continue;
            if (workerReport && IsIndependentWorkerReport(state, run, row, dependencies)) continue;
            throw new GovernanceException("The governed basis changed outside this producer after briefing; refresh and reassess before recording a delayed judgment.");
        }
    }

    private static bool IsIndependentWorkerReport(GovernedTaskState state, AgentRun run,
        LedgerEvent row, IReadOnlySet<ClaimId> dependencies)
    {
        var peer = state.Runs.Values.FirstOrDefault(candidate => candidate.Id.Value == row.CorrelationId);
        if (peer is not { SubjectRole: RoleKind.Worker, WorkItemId: { } peerWork } ||
            peer.ActorId != row.ActorId || peerWork == run.WorkItemId ||
            !state.WorkItems.TryGetValue(peerWork, out var other) ||
            !state.WorkItems.TryGetValue(run.WorkItemId!.Value, out var own) ||
            own.ResourceScope.Any(first => other.ResourceScope.Any(second => Overlaps(first, second))))
            return false;

        // Additive reports from disjoint workers are not changes to this worker's
        // contract. Linked dependency evidence and every governing mutation still refuse.
        return row.Data switch
        {
            ClaimAdded added => !dependencies.Contains(added.Claim.Id),
            EvidenceAdded added => !added.Evidence.Supports.Concat(added.Evidence.Refutes).Any(dependencies.Contains),
            AlternativeRecorded => true,
            ProducerOutcomeDeclared outcome => outcome.RunId == peer.Id,
            RoutingAssessmentRecorded assessment => assessment.RunId == peer.Id,
            _ => false
        };
    }

    private static bool Overlaps(string first, string second) =>
        first == second || first.StartsWith(second.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
        second.StartsWith(first.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static void ValidateResolution(GovernedTaskState state, AgentRun run, DecisionResolutionHandoff request)
    {
        if (run.SubjectRole != RoleKind.PlanningLead || !state.Decisions.TryGetValue(request.Decision, out var decision) ||
            decision.Provenance.ActorId != run.ActorId || decision.DependsOnClaims.Count == 0 ||
            decision.DependsOnClaims.Any(id => state.Claims[id].Status != ClaimStatus.Validated) ||
            request.Evidence.Count == 0 || request.Evidence.Any(id => !state.Evidence.ContainsKey(id)) ||
            request.Artifacts.Count == 0 || request.Artifacts.Any(id => !ArtifactApplicability.Current(state).Any(a => a.ArtifactId == id)))
            throw new GovernanceException("Routine decision requires its authorized planning producer, validated dependencies, current artifacts and evidence.");
        if (state.Escalations.Values.Any(e => e.Status == EscalationStatus.Open) || state.Challenges.Values.Any(c => c.Status == ChallengeStatus.Open))
            throw new GovernanceException("Resolve conflicting findings or trusted questions before routine decision admission.");
    }

    private static GovernedArtifactKind ArtifactKind(GoverningArtifactHandoffKind kind) => kind switch
    {
        GoverningArtifactHandoffKind.InternalRecon => GovernedArtifactKind.InternalRecon,
        GoverningArtifactHandoffKind.PromptContract => GovernedArtifactKind.PromptContract,
        GoverningArtifactHandoffKind.OrchestrationPlan => GovernedArtifactKind.OrchestrationPlan,
        _ => throw new NotSupportedException("Unsupported governing artifact kind.")
    };
    private static string? Identity(LedgerCommand command) => command switch
    {
        RecordArtifactCommand artifact => artifact.ArtifactId.Value,
        RaiseEscalationCommand escalation => escalation.EscalationId.Value,
        ProposeDecisionCommand decision => decision.DecisionId.Value,
        RecordRoutingAssessmentCommand assessment => assessment.RunId.Value,
        _ => null
    };
    private static HostHandoffReceipt Refused(CognitiveHostHandoff request, string diagnostic) =>
        new(HostHandoffStatus.Refused, request.RequestId, [], Diagnostic: diagnostic);
}
