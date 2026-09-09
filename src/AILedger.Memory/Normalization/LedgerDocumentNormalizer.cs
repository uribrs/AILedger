using AILedger.Core.Contracts;
using AILedger.Memory.Contracts;
using AILedger.Memory.Ingestion;
using System.IO;
using System.Linq;

namespace AILedger.Memory.Normalization;

public sealed class LedgerDocumentNormalizer
{
    public IReadOnlyList<MemoryDocument> Normalize(
        SourceDescriptor source,
        IReadOnlyList<LocatedLedgerEvent> events)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return [];
        }

        GovernedTaskState? state = null;
        foreach (var located in events)
        {
            try
            {
                state = HistoricalLedgerProjector.Apply(state, located.Event);
            }
            catch (KeyNotFoundException exception)
            {
                var reference = DescribeMissingReference(state, located.Event.Data);
                throw new InvalidDataException(
                    $"Invalid historical event in '{source.CanonicalPath}' at line {located.LineNumber}: " +
                    $"{reference.Kind} '{reference.Id}' does not exist.",
                    exception);
            }
            catch (Exception exception) when (IsIncompleteBodyFailure(exception))
            {
                throw IncompleteBody(source, located, DescribeRecord(located.Event.Data), exception);
            }
        }

        var task = state ?? throw new InvalidDataException("The event history did not open a task.");
        var documents = new List<MemoryDocument>();
        documents.AddRange(task.Lessons.Values.Select(lesson => NormalizeRecord(
            source, events, "lesson", lesson.Id.Value, () => NormalizeLesson(source, events, lesson, task))));
        documents.AddRange(task.Decisions.Values.Select(decision => NormalizeRecord(
            source, events, "decision", decision.Id.Value, () => NormalizeDecision(source, events, decision, task))));
        documents.AddRange(task.Constraints.Values.Select(constraint => NormalizeRecord(
            source, events, "constraint", constraint.Id.Value, () => NormalizeConstraint(source, events, constraint, task))));
        documents.AddRange(task.Alternatives.Values.Select(alternative => NormalizeRecord(
            source, events, "alternative", alternative.Id.Value, () => NormalizeAlternative(source, events, alternative, task))));
        documents.AddRange(task.Claims.Values.Select(claim => NormalizeRecord(
            source, events, "claim", claim.Id.Value, () => NormalizeClaim(source, events, claim, task))));
        documents.AddRange(task.WorkItems.Values.Select(work => NormalizeRecord(
            source, events, "work item", work.Id.Value, () => NormalizeWork(source, events, work, task))));
        documents.AddRange(task.Runs.Values.Select(run => NormalizeRecord(
            source, events, "run", run.Id.Value, () => NormalizeRun(source, events, run, task))));
        documents.AddRange(task.Artifacts.Values.Select(artifact => NormalizeRecord(
            source, events, "artifact", artifact.ArtifactId.Value,
            () => NormalizeArtifact(source, events, artifact, task))));

        return documents.OrderBy(document => document.Id, StringComparer.Ordinal).ToArray();
    }

    private static (string Kind, string Id) DescribeMissingReference(
        GovernedTaskState? state,
        LedgerEventData data) => data switch
    {
        ClaimResolved item => ("claim", item.ClaimId.Value),
        DecisionResolved item => ("decision", item.DecisionId.Value),
        DecisionInvalidated item => ("decision", item.DecisionId.Value),
        ChallengeDisposed item => ("challenge", item.ChallengeId.Value),
        WorkItemInvalidated item => ("work item", item.WorkItemId.Value),
        RunStarted item when item.Run.WorkItemId is { } workItemId => ("work item", workItemId.Value),
        RunCompleted item when state?.Runs.TryGetValue(item.RunId, out var run) == true &&
            run.WorkItemId is { } workItemId => ("work item", workItemId.Value),
        RunCompleted item => ("run", item.RunId.Value),
        EscalationResolved item => ("escalation", item.EscalationId.Value),
        ConstraintSuperseded item => ("constraint", item.ConstraintId.Value),
        WorkItemCompleted item => ("work item", item.WorkItemId.Value),
        WorkItemBlocked item => ("work item", item.WorkItemId.Value),
        WorkItemUnblocked item => ("work item", item.WorkItemId.Value),
        WorkItemAbandoned item => ("work item", item.WorkItemId.Value),
        DecisionOverturned item => ("decision", item.DecisionId.Value),
        _ => ("record referenced by " + data.GetType().Name, "unknown")
    };

    private static MemoryDocument NormalizeRecord(
        SourceDescriptor source,
        IReadOnlyList<LocatedLedgerEvent> events,
        string recordKind,
        string recordId,
        Func<MemoryDocument> normalize)
    {
        try
        {
            return normalize();
        }
        catch (Exception exception) when (IsIncompleteBodyFailure(exception))
        {
            var located = events.LastOrDefault(item =>
            {
                var reference = DescribeRecord(item.Event.Data);
                return reference is not null &&
                    reference.Value.Kind == recordKind &&
                    reference.Value.Id == recordId;
            }) ?? events[^1];
            throw IncompleteBody(source, located, (recordKind, recordId), exception);
        }
    }

    private static bool IsIncompleteBodyFailure(Exception exception) =>
        exception is ArgumentException or NullReferenceException;

    private static InvalidDataException IncompleteBody(
        SourceDescriptor source,
        LocatedLedgerEvent located,
        (string Kind, string? Id)? reference,
        Exception exception)
    {
        var eventType = located.Event.Data?.GetType().Name ?? "missing";
        var record = reference is { } value && !string.IsNullOrWhiteSpace(value.Id)
            ? $" {value.Kind} '{value.Id}'"
            : string.Empty;
        return new InvalidDataException(
            $"Invalid historical event in '{source.CanonicalPath}' at line {located.LineNumber}: " +
            $"event type '{eventType}' has an incomplete{record} body.",
            exception);
    }

    private static (string Kind, string? Id)? DescribeRecord(LedgerEventData? data) => data switch
    {
        ClaimAdded item => ("claim", item.Claim?.Id.Value),
        EvidenceAdded item => ("evidence", item.Evidence?.Id.Value),
        DecisionProposed item => ("decision", item.Decision?.Id.Value),
        ChallengeRaised item => ("challenge", item.Challenge?.Id.Value),
        WorkItemAdded item => ("work item", item.WorkItem?.Id.Value),
        RunStarted item => ("run", item.Run?.Id.Value),
        EscalationRaised item => ("escalation", item.Escalation?.Id.Value),
        AlternativeRecorded item => ("alternative", item.Alternative?.Id.Value),
        ConstraintAdded item => ("constraint", item.Constraint?.Id.Value),
        LessonMinted item => ("lesson", item.Lesson?.Id.Value),
        LessonRecalled item => ("lesson", item.Lesson?.Id.Value),
        LessonMarked item => ("lesson mark", item.Mark?.Id.Value),
        ArtifactRecorded item => ("artifact", item.Artifact?.ArtifactId.Value),
        ClaimResolved item => ("claim", item.ClaimId.Value),
        DecisionResolved item => ("decision", item.DecisionId.Value),
        DecisionInvalidated item => ("decision", item.DecisionId.Value),
        ChallengeDisposed item => ("challenge", item.ChallengeId.Value),
        WorkItemInvalidated item => ("work item", item.WorkItemId.Value),
        RunCompleted item => ("run", item.RunId.Value),
        EscalationResolved item => ("escalation", item.EscalationId.Value),
        ConstraintSuperseded item => ("constraint", item.ConstraintId.Value),
        WorkItemCompleted item => ("work item", item.WorkItemId.Value),
        WorkItemBlocked item => ("work item", item.WorkItemId.Value),
        WorkItemUnblocked item => ("work item", item.WorkItemId.Value),
        WorkItemAbandoned item => ("work item", item.WorkItemId.Value),
        DecisionOverturned item => ("decision", item.DecisionId.Value),
        _ => null
    };

    private static MemoryDocument NormalizeLesson(
        SourceDescriptor source, IReadOnlyList<LocatedLedgerEvent> events, Lesson lesson, GovernedTaskState task)
    {
        var contributing = Select(events, data => data switch
        {
            LessonMinted item => item.Lesson.Id == lesson.Id,
            LessonRecalled item => item.Lesson.Id == lesson.Id,
            _ => false
        });
        var superseded = task.Lessons.Values.Any(item => item.SupersedesLessonId == lesson.Id);
        return Create(source, MemoryDocumentKind.Lesson, lesson.Id.Value,
            MemoryDocumentFactory.Text(
                ("Lesson", lesson.Statement), ("Outcome", lesson.Outcome), ("Class", lesson.Class),
                ("Source kind", lesson.SourceKind), ("Source record", lesson.SourceRecordId),
                ("Verify", lesson.Verify), ("Do not", lesson.DoNot), ("Established by", lesson.Actor)),
            superseded ? MemoryLifecycle.Superseded : MemoryLifecycle.Active,
            MemoryAuthority.MintedLesson, contributing, lesson.Provenance.ActorId.Value,
            tags: lesson.Tags ?? [], relatedIds: lesson.Citations.Append(lesson.SourceRecordId),
            relations: lesson.SupersedesLessonId is { } previous
                ? [new DocumentRelation { Kind = DocumentRelationKind.Supersedes, TargetId = previous.Value }]
                : []);
    }

    private static MemoryDocument NormalizeDecision(
        SourceDescriptor source, IReadOnlyList<LocatedLedgerEvent> events, Decision decision, GovernedTaskState task)
    {
        var contributing = Select(events, data => data switch
        {
            DecisionProposed item => item.Decision.Id == decision.Id,
            DecisionResolved item => item.DecisionId == decision.Id,
            DecisionInvalidated item => item.DecisionId == decision.Id,
            DecisionOverturned item => item.DecisionId == decision.Id,
            ClaimDependenciesRepointed item =>
                decision.DependsOnClaims.Contains(item.ReplacementClaimId) ||
                decision.DependsOnClaims.Contains(item.SupersededClaimId),
            _ => false
        });
        var lifecycle = decision.Status switch
        {
            DecisionStatus.Accepted => MemoryLifecycle.Active,
            DecisionStatus.Superseded => MemoryLifecycle.Superseded,
            DecisionStatus.Invalidated => MemoryLifecycle.Rejected,
            _ => MemoryLifecycle.Unresolved
        };
        var relations = decision.DependsOnClaims.Select(id => new DocumentRelation
        {
            Kind = DocumentRelationKind.DependsOn,
            TargetId = id.Value
        }).ToList();
        if (decision.Supersedes is { } previous)
        {
            relations.Add(new DocumentRelation { Kind = DocumentRelationKind.Supersedes, TargetId = previous.Value });
        }

        return Create(source, MemoryDocumentKind.Decision, decision.Id.Value,
            MemoryDocumentFactory.Text(("Decision", decision.Statement), ("Status", decision.Status),
                ("Rationale", decision.Rationale),
                ("Depends on claims", decision.DependsOnClaims.Select(id => id.Value))),
            lifecycle,
            decision.Status == DecisionStatus.Accepted ? MemoryAuthority.AcceptedDecision : MemoryAuthority.HistoricalRecord,
            contributing, decision.Provenance.ActorId.Value,
            relatedIds: decision.DependsOnClaims.Select(id => id.Value), relations: relations);
    }

    private static MemoryDocument NormalizeConstraint(
        SourceDescriptor source, IReadOnlyList<LocatedLedgerEvent> events, Constraint constraint, GovernedTaskState task)
    {
        var contributing = Select(events, data => data switch
        {
            ConstraintAdded item => item.Constraint.Id == constraint.Id,
            ConstraintSuperseded item => item.ConstraintId == constraint.Id,
            _ => false
        });
        var active = constraint.Status == ConstraintStatus.Active;
        return Create(source, MemoryDocumentKind.Constraint, constraint.Id.Value,
            MemoryDocumentFactory.Text(("Constraint", constraint.Statement), ("Status", constraint.Status),
                ("Source", constraint.Source), ("Scope", constraint.Scope)),
            active ? MemoryLifecycle.Active : MemoryLifecycle.Superseded,
            active ? MemoryAuthority.ActiveConstraint : MemoryAuthority.HistoricalRecord,
            contributing, constraint.Provenance.ActorId.Value, tags: constraint.Scope, relatedIds: constraint.Scope);
    }

    private static MemoryDocument NormalizeAlternative(
        SourceDescriptor source, IReadOnlyList<LocatedLedgerEvent> events, Alternative alternative, GovernedTaskState task)
    {
        var contributing = Select(events, data => data is AlternativeRecorded item && item.Alternative.Id == alternative.Id);
        var relations = alternative.ReplacedByDecisionId is { } replacement
            ? new[] { new DocumentRelation { Kind = DocumentRelationKind.Replaces, TargetId = replacement.Value } }
            : [];
        return Create(source, MemoryDocumentKind.Alternative, alternative.Id.Value,
            MemoryDocumentFactory.Text(("Rejected alternative", alternative.Statement),
                ("Rejected because", alternative.RejectionRationale),
                ("Replaced by decision", alternative.ReplacedByDecisionId?.Value)),
            MemoryLifecycle.Rejected, MemoryAuthority.ResolvedAlternative,
            contributing, alternative.Provenance.ActorId.Value,
            relatedIds: alternative.ReplacedByDecisionId is { } id ? [id.Value] : [], relations: relations);
    }

    private static MemoryDocument NormalizeClaim(
        SourceDescriptor source, IReadOnlyList<LocatedLedgerEvent> events, Claim claim, GovernedTaskState task)
    {
        var evidence = SelectEvidence(source, events, claim, task);
        var evidenceIds = evidence.Select(item => item.Id.Value).ToHashSet(StringComparer.Ordinal);
        var contributing = Select(events, data => data switch
        {
            ClaimAdded item => item.Claim.Id == claim.Id,
            ClaimResolved item => item.ClaimId == claim.Id,
            EvidenceAdded item => evidenceIds.Contains(item.Evidence.Id.Value),
            _ => false
        });
        var lifecycle = claim.Status switch
        {
            ClaimStatus.Validated => MemoryLifecycle.Resolved,
            ClaimStatus.Rejected => MemoryLifecycle.Rejected,
            ClaimStatus.Superseded => MemoryLifecycle.Superseded,
            _ => MemoryLifecycle.Unresolved
        };
        var evidenceText = evidence.Select(item =>
            $"{item.Id.Value} [{item.SourceType}] {item.Citation}: {item.Summary}");
        var relations = evidence.SelectMany(item =>
            item.Supports.Contains(claim.Id)
                ? new[] { new DocumentRelation { Kind = DocumentRelationKind.Supports, TargetId = item.Id.Value } }
                : item.Refutes.Contains(claim.Id)
                    ? new[] { new DocumentRelation { Kind = DocumentRelationKind.Refutes, TargetId = item.Id.Value } }
                    : []).ToList();
        if (claim.SupersededByClaimId is { } replacement)
        {
            relations.Add(new DocumentRelation { Kind = DocumentRelationKind.Supersedes, TargetId = replacement.Value });
        }

        return Create(source, MemoryDocumentKind.ClaimEvidence, claim.Id.Value,
            MemoryDocumentFactory.Text(("Claim", claim.Statement), ("Status", claim.Status),
                ("Consequence if wrong", claim.ConsequenceIfWrong), ("Evidence", evidenceText)),
            lifecycle,
            claim.Status is ClaimStatus.Validated or ClaimStatus.Rejected
                ? MemoryAuthority.ResolvedClaim : MemoryAuthority.UnresolvedRecord,
            contributing, claim.Provenance.ActorId.Value,
            relatedIds: evidence.Select(item => item.Id.Value), relations: relations);
    }

    private static IReadOnlyList<Evidence> SelectEvidence(
        SourceDescriptor source,
        IReadOnlyList<LocatedLedgerEvent> events,
        Claim claim,
        GovernedTaskState task)
    {
        var claimEvidenceIds = claim.EvidenceIds;
        ArgumentNullException.ThrowIfNull(claimEvidenceIds);
        var listedEvidenceIds = claimEvidenceIds.ToHashSet();
        var selected = new List<Evidence>();
        foreach (var evidence in task.Evidence.Values)
        {
            try
            {
                var isListed = listedEvidenceIds.Contains(evidence.Id);
                var supports = evidence.Supports.Contains(claim.Id);
                var refutes = evidence.Refutes.Contains(claim.Id);
                if (isListed || supports || refutes)
                {
                    selected.Add(evidence);
                }
            }
            catch (Exception exception) when (IsIncompleteBodyFailure(exception))
            {
                var located = events.LastOrDefault(item =>
                    item.Event.Data is EvidenceAdded added && added.Evidence?.Id == evidence.Id);
                if (located is not null)
                {
                    throw IncompleteBody(
                        source, located, ("evidence", evidence.Id.Value), exception);
                }

                throw;
            }
        }

        return selected.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToArray();
    }

    private static MemoryDocument NormalizeWork(
        SourceDescriptor source, IReadOnlyList<LocatedLedgerEvent> events, WorkItem work, GovernedTaskState task)
    {
        var contributing = Select(events, data => data switch
        {
            WorkItemAdded item => item.WorkItem.Id == work.Id,
            WorkItemInvalidated item => item.WorkItemId == work.Id,
            WorkItemCompleted item => item.WorkItemId == work.Id,
            WorkItemBlocked item => item.WorkItemId == work.Id,
            WorkItemUnblocked item => item.WorkItemId == work.Id,
            WorkItemAbandoned item => item.WorkItemId == work.Id,
            RunStarted item => item.Run.WorkItemId == work.Id,
            RunCompleted item => task.Runs.TryGetValue(item.RunId, out var run) && run.WorkItemId == work.Id,
            ClaimDependenciesRepointed item =>
                work.DependsOnClaims.Contains(item.ReplacementClaimId) ||
                work.DependsOnClaims.Contains(item.SupersededClaimId),
            _ => false
        });
        var lifecycle = work.Status switch
        {
            WorkItemStatus.Completed => MemoryLifecycle.Resolved,
            WorkItemStatus.Stale => MemoryLifecycle.Superseded,
            WorkItemStatus.Abandoned => MemoryLifecycle.Rejected,
            _ => MemoryLifecycle.Active
        };
        return Create(source, MemoryDocumentKind.WorkSummary, work.Id.Value,
            MemoryDocumentFactory.Text(("Work item", work.Title), ("Status", work.Status),
                ("Owner", work.Owner?.Value), ("Scope", work.ResourceScope),
                ("Depends on claims", work.DependsOnClaims.Select(id => id.Value)),
                ("Block reason", work.BlockReason), ("Abandon reason", work.AbandonReason)),
            lifecycle, MemoryAuthority.RunOrWorkSummary, contributing, work.Owner?.Value,
            workItemId: work.Id.Value, tags: work.ResourceScope,
            relatedIds: work.DependsOnClaims.Select(id => id.Value),
            relations: work.DependsOnClaims.Select(id => new DocumentRelation
            {
                Kind = DocumentRelationKind.DependsOn,
                TargetId = id.Value
            }));
    }

    private static MemoryDocument NormalizeRun(
        SourceDescriptor source, IReadOnlyList<LocatedLedgerEvent> events, AgentRun run, GovernedTaskState task)
    {
        var contributing = Select(events, data => data switch
        {
            RunStarted item => item.Run.Id == run.Id,
            RunCompleted item => item.RunId == run.Id,
            _ => false
        });
        var lifecycle = run.Status == AgentRunStatus.Active ? MemoryLifecycle.Active : MemoryLifecycle.Resolved;
        return Create(source, MemoryDocumentKind.RunSummary, run.Id.Value,
            MemoryDocumentFactory.Text(("Run", run.Id.Value), ("Status", run.Status),
                ("Actor", run.ActorId.Value), ("Work item", run.WorkItemId?.Value),
                ("Provider", run.Provider), ("Model", run.Model), ("Provider version", run.ProviderVersion),
                ("Started", run.StartedAt), ("Ended", run.EndedAt)),
            lifecycle, MemoryAuthority.RunOrWorkSummary, contributing, run.ActorId.Value,
            workItemId: run.WorkItemId?.Value, runId: run.Id.Value,
            relatedIds: run.WorkItemId is { } workId ? [workId.Value] : [],
            relations: run.WorkItemId is { } itemId
                ? [new DocumentRelation { Kind = DocumentRelationKind.BelongsTo, TargetId = itemId.Value }]
                : []);
    }

    private static MemoryDocument NormalizeArtifact(
        SourceDescriptor source, IReadOnlyList<LocatedLedgerEvent> events, GovernedArtifact artifact, GovernedTaskState task)
    {
        var contributing = Select(events, data => data is ArtifactRecorded item && item.Artifact.ArtifactId == artifact.ArtifactId);
        var superseded = task.Artifacts.Values.Any(item => item.SupersedesArtifactId == artifact.ArtifactId);
        var related = new[] { artifact.WorkItemId?.Value, artifact.ProducerRunId?.Value, artifact.SupersedesArtifactId?.Value };
        return Create(source, MemoryDocumentKind.Artifact, artifact.ArtifactId.Value,
            MemoryDocumentFactory.Text(("Artifact", artifact.Title), ("Kind", artifact.Kind),
                ("Content", artifact.Content), ("Work item", artifact.WorkItemId?.Value),
                ("Producer run", artifact.ProducerRunId?.Value)),
            superseded ? MemoryLifecycle.Superseded : MemoryLifecycle.Active,
            MemoryAuthority.HistoricalRecord, contributing, artifact.Provenance.ActorId.Value,
            workItemId: artifact.WorkItemId?.Value, runId: artifact.ProducerRunId?.Value,
            relatedIds: related,
            relations: artifact.SupersedesArtifactId is { } previous
                ? [new DocumentRelation { Kind = DocumentRelationKind.Supersedes, TargetId = previous.Value }]
                : []);
    }

    private static MemoryDocument Create(
        SourceDescriptor source,
        MemoryDocumentKind kind,
        string recordIdentity,
        string text,
        MemoryLifecycle lifecycle,
        MemoryAuthority authority,
        IReadOnlyList<LocatedLedgerEvent> contributing,
        string? actorId,
        string? workItemId = null,
        string? runId = null,
        IEnumerable<string?>? tags = null,
        IEnumerable<string?>? relatedIds = null,
        IEnumerable<DocumentRelation>? relations = null)
    {
        if (contributing.Count == 0)
        {
            throw new InvalidDataException($"No canonical event was found for {kind} '{recordIdentity}'.");
        }

        var last = contributing[^1];
        return MemoryDocumentFactory.Create(
            source, kind, recordIdentity, text, last.Event.RecordedAt, last.Event.EventId.Value,
            authority, lifecycle,
            contributing.Select(item => new MemoryCitation
            {
                Kind = MemoryCitationKind.Event,
                SourcePath = Path.GetFullPath(source.CanonicalPath),
                LineNumber = item.LineNumber,
                EventId = item.Event.EventId.Value,
                RecordId = recordIdentity
            }).ToArray(),
            workItemId, runId, actorId, tags, relatedIds, relations);
    }

    private static IReadOnlyList<LocatedLedgerEvent> Select(
        IReadOnlyList<LocatedLedgerEvent> events,
        Func<LedgerEventData, bool> predicate) =>
        events.Where(item => predicate(item.Event.Data)).OrderBy(item => item.LineNumber).ToArray();
}
