using System.Text.Json;
using AILedger.Cli.Dispatch;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
namespace AILedger.Cli.Cognitive;

// The cache bounds transport work. File storage commits exact key/body/binding receipts with
// the governed events, so reconnect/restart does not depend on this instance surviving.
internal sealed class CognitiveHandoffSession(IGovernedTaskService service, CognitiveHostBinding binding,
    CognitiveWorkKind? assignment = null)
{
    internal IReadOnlyList<HostHandoffReceipt> Receipts => _receipts.Values.Select(row => row.Receipt).ToArray();
    internal bool HasUnknown { get; private set; }
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, (string Body, HostHandoffReceipt Receipt)> _receipts = new(StringComparer.Ordinal);

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
                if (request.Operation is RoutingAssessmentHandoff assessment && assignment is { } expected && assessment.Assessment.Work != expected)
                    throw new GovernanceException("Assessment does not match the host-assigned cognitive work.");
                if (request.Operation is RoutingAssessmentHandoff or DecisionResolutionHandoff or PreparationSelectionHandoff)
                    await EnsureJudgmentBasisAsync(run, token).ConfigureAwait(false);
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

    private async Task EnsureJudgmentBasisAsync(AgentRun run, CancellationToken token)
    {
        await foreach (var row in service.GetHistoryAsync(binding.TaskId, token).ConfigureAwait(false))
        {
            if (row.RecordedAt < run.StartedAt || row.CorrelationId == binding.RunId.Value ||
                row.Data is RunStarted or RunCompleted or ContextBuilt) continue;
            throw new GovernanceException("The governed basis changed outside this producer after briefing; refresh and reassess before recording a delayed judgment.");
        }
    }

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
