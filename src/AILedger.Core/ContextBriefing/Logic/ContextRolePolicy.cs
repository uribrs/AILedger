using AILedger.Core.Application;
using AILedger.Core.Contracts;

namespace AILedger.Core.ContextBriefing;

// Owns which cognitive skills and task artifacts each role may see, including reviewer isolation.
internal static class ContextRolePolicy
{
    private static readonly IReadOnlySet<ContextArtifactKind> ReviewerExclusions =
        new HashSet<ContextArtifactKind>
        {
            // R5 (reviewer-isolation): recon carries producer planning intent.
            ContextArtifactKind.InternalRecon,
            ContextArtifactKind.UserRequest,
            ContextArtifactKind.PromptContract,
            ContextArtifactKind.OrchestrationPlan,
            ContextArtifactKind.VerifierOutput,
            // A second review that reads the first is not a second opinion.
            ContextArtifactKind.CodeReviewOutput,
            // An open escalation carries the leads' recommendation, which is intent.
            ContextArtifactKind.Escalation
        };

    // Ordered, not a set. The order a role is served its skills is part of the brief.
    private static readonly IReadOnlyDictionary<RoleKind, IReadOnlyList<string>> CanonicalRoleSkills =
        new Dictionary<RoleKind, IReadOnlyList<string>>
        {
            [RoleKind.Operator] = Skills("workflow-coordinator", "task-orchestrator"),
            [RoleKind.PlanningLead] = Skills(
                "workflow-coordinator",
                "prompt-contract-designer",
                "task-orchestrator",
                "technical-researcher"),
            [RoleKind.ImplementationLead] = Skills(
                "workflow-coordinator",
                "prompt-contract-designer",
                "task-orchestrator",
                "contract-driven-execution"),
            [RoleKind.Researcher] = Skills("technical-researcher"),
            [RoleKind.Worker] = Skills("contract-driven-execution"),
            [RoleKind.Verifier] = Skills("task-orchestrator"),
            [RoleKind.CodeReviewer] = Skills("code-reviewer")
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<RoleKind>> NoLessonAudiences =
        new Dictionary<string, IReadOnlyList<RoleKind>>(StringComparer.Ordinal);

    internal static bool IsAllowed(
        RoleKind role,
        ContextArtifact artifact,
        IReadOnlyDictionary<string, IReadOnlyList<RoleKind>> lessonAudiences)
    {
        // A lesson carrying no audience reaches every role; an addressed one reaches only its roles.
        if (artifact.Kind == ContextArtifactKind.Lesson &&
            lessonAudiences.TryGetValue(artifact.Id, out var audience) &&
            !audience.Contains(role))
        {
            return false;
        }

        if (role == RoleKind.CodeReviewer && ReviewerExclusions.Contains(artifact.Kind))
        {
            return false;
        }

        if (artifact.Kind != ContextArtifactKind.Skill)
        {
            return true;
        }

        if (artifact.RelatedIds.Contains(role.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return CanonicalRoleSkills.TryGetValue(role, out var skills) &&
               skills.Any(skill => IsSkillId(artifact.Id, skill));
    }

    internal static int SkillRank(RoleKind role, ContextArtifact artifact)
    {
        if (artifact.Kind != ContextArtifactKind.Skill ||
            !CanonicalRoleSkills.TryGetValue(role, out var skills))
        {
            return artifact.Kind == ContextArtifactKind.Skill ? int.MaxValue : 0;
        }

        for (var index = 0; index < skills.Count; index++)
        {
            if (IsSkillId(artifact.Id, skills[index]))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    internal static IReadOnlyList<ContextSkill> SkillsServed(
        RoleKind role,
        IReadOnlyList<ContextArtifact> availableArtifacts) =>
        ContextSkills.From(availableArtifacts
            .Where(artifact => artifact.Kind == ContextArtifactKind.Skill)
            .Where(artifact => IsAllowed(role, artifact, NoLessonAudiences))
            .OrderBy(artifact => SkillRank(role, artifact))
            .ThenBy(artifact => artifact.Id, StringComparer.Ordinal)
            .ThenBy(artifact => artifact.Content, StringComparer.Ordinal)
            .GroupBy(artifact => artifact.Id, StringComparer.Ordinal)
            .Select(group => group.First()));

    private static IReadOnlyList<string> Skills(params string[] names) => names;

    private static bool IsSkillId(string artifactId, string skillName) =>
        artifactId.Equals(skillName, StringComparison.OrdinalIgnoreCase) ||
        artifactId.EndsWith($"/{skillName}", StringComparison.OrdinalIgnoreCase) ||
        artifactId.EndsWith($":{skillName}", StringComparison.OrdinalIgnoreCase);
}
