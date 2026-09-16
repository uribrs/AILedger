using AILedger.Core.Contracts;

namespace AILedger.Core.Runs.Batch;

public sealed record BatchPreflightRequest(IReadOnlyList<BatchPreflightMember> Members);

public sealed record BatchPreflightMember(
    string Id,
    PlannedWorkItemPreflight? WorkItem = null,
    ProviderLaunchPreflightRequest? ProviderLaunch = null,
    BatchPreflightPreparationRefusal? PreparationRefusal = null);

public sealed record BatchPreflightPreparationRefusal(
    BatchPreflightMemberKind Kind,
    string Refusal);

public sealed record PlannedWorkItemPreflight(
    ActorId ActorId,
    WorkItemId WorkItemId,
    string Title,
    ActorId? Owner,
    IReadOnlyList<ClaimId> DependsOnClaims,
    IReadOnlyList<string> ResourceScope,
    AlternativeId? NotSplitJustification = null,
    string? BaseRef = null,
    IReadOnlyList<ContextSkill>? SkillsServedNow = null,
    string? WithoutBriefReason = null,
    EvidenceId? StaleBriefEvidenceId = null);

public sealed record ProviderLaunchPreflightRequest(
    ActorId ActorId,
    ActorId? SubjectActorId,
    WorkItemId? WorkItemId,
    IReadOnlyList<ContextSkill>? SkillsServedNow = null,
    string? WithoutBriefReason = null,
    EvidenceId? StaleBriefEvidenceId = null,
    RunId? RunId = null,
    string? Provider = null,
    AssuranceBinding? Assurance = null);

public sealed record BatchPreflightResult(
    long CheckedTaskVersion,
    bool Admissible,
    IReadOnlyList<BatchPreflightMemberResult> Members,
    IReadOnlyList<BatchPreflightCause> Causes);

public sealed record BatchPreflightMemberResult(
    string Id,
    BatchPreflightMemberKind Kind,
    bool Admissible,
    string? Refusal = null);

public sealed record BatchPreflightCause(
    string Refusal,
    IReadOnlyList<string> AffectedMembers);

public enum BatchPreflightMemberKind
{
    WorkItem,
    ProviderLaunch
}
