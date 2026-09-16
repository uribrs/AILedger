using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Artifacts;

internal static class ArtifactRevisionRules
{
    internal static void Ensure(
        GovernedTaskState state,
        GovernedArtifactKind kind,
        WorkItemId? workItemId,
        ArtifactId artifactId,
        ArtifactId? supersedesArtifactId)
    {
        var current = Current(state)
            .Where(item => item.Kind == kind && item.WorkItemId == workItemId)
            .ToArray();
        if (supersedesArtifactId is not { } predecessorId)
        {
            if (current.Length != 0)
            {
                throw new GovernanceException(
                    $"A current '{kind}' artifact already exists for this scope; a revision must supersede it. " +
                    NameCurrent(current));
            }

            return;
        }

        if (predecessorId == artifactId)
        {
            throw new GovernanceException("An artifact cannot supersede itself.");
        }

        var predecessor = Get(state.Artifacts, predecessorId, "superseded artifact");
        if (predecessor.Kind != kind || predecessor.WorkItemId != workItemId)
        {
            throw new GovernanceException(
                "An artifact can only supersede the current artifact of the same kind and work scope.");
        }

        if (!current.Any(item => item.ArtifactId == predecessorId))
        {
            throw new GovernanceException($"Artifact '{predecessorId}' is not current and cannot be superseded.");
        }
    }

    internal static IReadOnlyList<GovernedArtifact> Current(GovernedTaskState state) =>
        ArtifactApplicability.Current(state);

    /// <summary>
    /// Names the artifact a revision has to supersede. The rule admits one current artifact per
    /// kind and work scope, so this is normally a single id and the sentence is a command the
    /// caller can run. Histories written before the rule existed can hold more than one, and there
    /// the caller has to choose — so that case states the list and does not pretend to be a command.
    /// </summary>
    /// <remarks>
    /// The two forms are separate because '--supersedes' takes one value. Joining several ids into
    /// the runnable sentence produced 'Pass --supersedes A1 or A2', which a caller pasting it back
    /// is refused for — the same failure the disclosure exists to remove. Ordered by id so two
    /// readers of one state are shown the same list.
    /// </remarks>
    internal static string NameCurrent(IReadOnlyList<GovernedArtifact> current)
    {
        var ids = current.Select(item => item.ArtifactId.Value)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        return ids.Length == 1
            ? $"Pass --supersedes {ids[0]}."
            : $"Current artifacts for this scope: {string.Join(", ", ids)}. Pass --supersedes with one of them.";
    }
}
