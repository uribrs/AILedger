using System.Security.Cryptography;
using System.Text;
using AILedger.Core.Contracts;

namespace AILedger.Core.Application;

// The one place a served skill is turned into a digest and two digest lists are compared. Both the
// event and the gate go through it, so a brief recorded by 'context build' and the freshness check
// 'work add' performs cannot disagree about what "the same skills" means.
public static class ContextSkills
{
    public static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty)));

    // Order is preserved, not sorted here. The caller has already put the skills in the order the
    // role is served them, and that order is part of the brief: the coordinator is the entry point
    // and the orchestrator is what it hands off to.
    public static IReadOnlyList<ContextSkill> From(IEnumerable<ContextArtifact> artifacts) =>
        artifacts
            .Where(artifact => artifact.Kind == ContextArtifactKind.Skill)
            .Select(artifact => new ContextSkill(artifact.Id, Hash(artifact.Content)))
            .ToArray();

    public static bool SameAs(IReadOnlyList<ContextSkill> recorded, IReadOnlyList<ContextSkill> served)
    {
        if (recorded.Count != served.Count)
        {
            return false;
        }

        for (var index = 0; index < recorded.Count; index++)
        {
            if (!string.Equals(recorded[index].SkillId, served[index].SkillId, StringComparison.Ordinal) ||
                !string.Equals(recorded[index].ContentHash, served[index].ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    // Asked inside the durable mutation lock, by the rule that would otherwise append. A zero-event
    // outcome cannot reach disk — the durable service refuses one — so a repeat brief declines by
    // throwing ContextAlreadyBriefedException, which is what leaves the version where it was (IC2,
    // VC2). Asking it before submitting instead is what let two concurrent briefs both append.
    public static bool AlreadyRecorded(
        GovernedTaskState state,
        ActorId actorId,
        IReadOnlyList<ContextSkill> skills) =>
        state.ContextBuilds.TryGetValue(actorId, out var build) && SameAs(build.Skills, skills);
}
