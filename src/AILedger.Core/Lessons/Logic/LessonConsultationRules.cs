using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Lessons;

// Command-time selection for a mid-task consultation. The store candidates arrive on the command,
// injected by the durable service; this decides which of them the consulting run may be served and
// which already-served task lessons a reconsideration reviews. Replay never re-derives either:
// LessonEventValidator.ValidateConsulted checks only the event's own integrity.
internal static class LessonConsultationRules
{
    internal static IReadOnlyList<LedgerEventData> Consult(GovernedTaskState state, ConsultLessonsCommand command)
    {
        RequireText(command.Question, nameof(command.Question));
        var run = Get(state.Runs, command.RunId, "run");
        if (run.ActorId != command.ActorId || run.Status != AgentRunStatus.Active)
        {
            throw new GovernanceException(
                $"Lesson consultation requires an active run '{command.RunId}' belonging to the consulting actor.");
        }

        var role = SubjectRole(run);
        EnsureRunFitsPurpose(command.Purpose, run, role);
        var tags = NormalizeTags(command.Tags);
        var claimIds = NormalizeClaims(state, command.Purpose, command.ClaimIds);

        var candidates = command.Candidates ?? [];
        EnsureUnique(candidates.Select(lesson => lesson.Id).ToArray(), "Consulted lesson IDs");
        var visible = candidates
            .Where(lesson => lesson.SourceTaskId != state.TaskId && IsVisible(lesson, role))
            .ToArray();
        var newLessons = visible
            .Where(lesson => !state.Lessons.ContainsKey(lesson.Id))
            .OrderBy(lesson => lesson.Id.Value, StringComparer.Ordinal)
            .ToArray();
        foreach (var lesson in newLessons)
        {
            LessonEventValidator.ValidateConsultedLesson(state, lesson);
        }

        var served = visible.Select(lesson => lesson.Id);
        if (command.Purpose == LessonConsultationPurpose.Reconsideration)
        {
            // UR1: reconsideration reviews what was already served, with its audience kept (K10).
            served = served.Concat(state.Lessons.Values
                .Where(lesson => IsVisible(lesson, role))
                .Select(lesson => lesson.Id));
        }

        return [new LessonsConsulted(
            run.Id,
            command.Purpose,
            command.Question.Trim(),
            tags,
            claimIds,
            InternalReconDocuments.ComputeClaimSetHash(state),
            served.Distinct().OrderBy(id => id.Value, StringComparer.Ordinal).ToArray(),
            newLessons)];
    }

    // The same audience rule ContextRolePolicy applies to a manifest: no audience reaches every role.
    internal static bool IsVisible(Lesson lesson, RoleKind role) =>
        lesson.Audience is not { Count: > 0 } audience || audience.Contains(role);

    // The role recorded at run start (PD10), as CompletedRoles and EnsureProducer read it. The actor's
    // current assignment answers a different question, so a run that recorded no role cannot consult.
    private static RoleKind SubjectRole(AgentRun run) =>
        run.SubjectRole ?? throw new GovernanceException(
            $"A lesson consultation requires a run that recorded its subject role at start; run '{run.Id}' did not.");

    private static void EnsureRunFitsPurpose(LessonConsultationPurpose purpose, AgentRun run, RoleKind role)
    {
        switch (purpose)
        {
            case LessonConsultationPurpose.Recon or LessonConsultationPurpose.Reconsideration:
                if (run.WorkItemId is not null || run.Assurance is not null ||
                    role is not (RoleKind.Operator or RoleKind.PlanningLead or RoleKind.ImplementationLead))
                {
                    throw new GovernanceException(
                        $"A {purpose.ToString().ToLowerInvariant()} lesson consultation requires a task-wide " +
                        "operator or lead run without assurance binding.");
                }
                break;
            case LessonConsultationPurpose.Research:
                if (role != RoleKind.Researcher)
                {
                    throw new GovernanceException("A research lesson consultation requires a Researcher run.");
                }
                break;
        }
    }

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags)
    {
        var trimmed = (tags ?? []).Select(tag => tag?.Trim() ?? "").ToArray();
        if (trimmed.Length == 0)
        {
            throw new GovernanceException("A lesson consultation requires at least one tag.");
        }
        if (trimmed.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("A lesson consultation cannot carry an empty tag.");
        }
        EnsureUnique(trimmed, "Consultation tags", StringComparer.Ordinal);
        return trimmed.OrderBy(tag => tag, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<ClaimId> NormalizeClaims(
        GovernedTaskState state,
        LessonConsultationPurpose purpose,
        IReadOnlyList<ClaimId>? claimIds)
    {
        var ids = claimIds ?? [];
        EnsureUnique(ids, "Consultation claim IDs");
        EnsureReferencesExist(state.Claims, ids, "claim");
        if (purpose == LessonConsultationPurpose.Research && ids.Count == 0)
        {
            throw new GovernanceException("A research lesson consultation must name at least one claim.");
        }
        return ids.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
    }
}
