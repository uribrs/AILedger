using System.Text.Json;
using AILedger.Core.Contracts;
namespace AILedger.Cli.Dispatch;

public enum GoverningArtifactHandoffKind { InternalRecon, PromptContract, OrchestrationPlan }
public abstract record CognitiveHostOperation
{
    private protected CognitiveHostOperation() { }
}
public sealed record GoverningArtifactHandoff(GoverningArtifactHandoffKind Kind, string Title,
    string Markdown, ArtifactId? Supersedes = null) : CognitiveHostOperation;
public sealed record ReconTemplateHandoff : CognitiveHostOperation;
public sealed record LessonConsultationHandoff(LessonConsultationPurpose Purpose, string Question,
    IReadOnlyList<string> Tags, IReadOnlyList<ClaimId> Claims) : CognitiveHostOperation;
public sealed record RoutingAssessmentHandoff(RoutingAssessment Assessment) : CognitiveHostOperation;
public sealed record EscalationHandoff(EscalationKind Kind, string Question, IReadOnlyList<string> Options,
    string? Recommendation, IReadOnlyList<EvidenceId> EvidenceIds) : CognitiveHostOperation;
public sealed record DecisionProposalHandoff(string Statement, string Rationale,
    IReadOnlyList<ClaimId> Claims) : CognitiveHostOperation;
public sealed record DecisionResolutionHandoff(DecisionId Decision, DecisionStatus Status, long ExpectedVersion,
    IReadOnlyList<EvidenceId> Evidence, IReadOnlyList<ArtifactId> Artifacts) : CognitiveHostOperation;
public sealed record PreparationSelectionHandoff(PreparationSelection Selection) : CognitiveHostOperation;
public sealed record LessonMarkHandoff(LessonSourceKind SourceKind, string SourceId, LessonClass Class,
    string Repository, IReadOnlyList<string> Tags, string Verify, string DoNot, LessonActor LessonActor,
    VerifyExpectation VerifyExpects) : CognitiveHostOperation;
public sealed record CloseoutSynthesisHandoff(string Title, string Markdown, ArtifactId? Supersedes = null) : CognitiveHostOperation;

// Supplied only by the authenticated live host. No request can choose these identities.
public sealed record CognitiveHostBinding(TaskId TaskId, ActorId Subject, RunId RunId, string Session);
public sealed record CognitiveHostHandoff(string RequestId, CognitiveHostOperation Operation);
public enum HostHandoffStatus { Unsupported, Refused, Recorded, Unknown, Observed }
public sealed record HostHandoffReceipt(HostHandoffStatus Status, string RequestId,
    IReadOnlyList<EventId> EventIds, ArtifactId? ArtifactId = null, string? Diagnostic = null,
    IReadOnlyList<Lesson>? Lessons = null, string? RecordId = null, long? TaskVersion = null,
    JsonElement? ReconTemplate = null);
public enum CognitiveHostOperationKind { GoverningArtifact, LessonConsultation, RoutingAssessment, Escalation, DecisionProposal, DecisionResolution, PreparationSelection, LessonMark, CloseoutSynthesis, ReconTemplate }
public sealed record CognitiveOperationSupport(CognitiveHostOperationKind Operation, bool Available, string RequiredHostImplementation);
public static class DispatchHostHandoffs
{
    public static IReadOnlyList<CognitiveOperationSupport> Support { get; } = Array.AsReadOnly(
        Enum.GetValues<CognitiveHostOperationKind>().Select(kind => new CognitiveOperationSupport(kind, true,
            kind == CognitiveHostOperationKind.ReconTemplate
                ? "Live authenticated cognitive_handoff; read-only current template for an active task-wide lead at Research/Design. No events or judgments."
                : "Live authenticated cognitive_handoff; exact session/key/body retries. Existing role, capability and stage gates apply.")).ToArray());
}
