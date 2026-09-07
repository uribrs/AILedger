using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Command-time counterparts: ValidateArtifactRecorded, ValidateArtifactProducer, ValidateArtifactRevision, ValidateVerifierOutput.
internal static class ArtifactRules
{
    internal static IReadOnlyList<LedgerEventData> RecordArtifact(
        GovernedTaskState state,
        RecordArtifactCommand command,
        DateTimeOffset now)
    {
        RequireId(command.ArtifactId.Value, nameof(command.ArtifactId));
        EnsureNew(state.Artifacts, command.ArtifactId, "artifact");
        RequireText(command.Title, nameof(command.Title));
        RequireText(command.Content, nameof(command.Content));
    
        var assignment = Get(state.Roles, command.ActorId, "actor role");
        EnsureArtifactScope(command.Kind, command.WorkItemId);
        EnsureArtifactAuthority(state, command.Kind, command.ActorId, assignment.Role,
            command.WorkItemId, command.ProducerRunId);
        EnsureArtifactRevision(state, command.Kind, command.WorkItemId,
            command.ArtifactId, command.SupersedesArtifactId);
    
        if (command.Kind == GovernedArtifactKind.VerifierOutput)
        {
            ValidateVerifierOutput(state, command.WorkItemId!.Value, command.Content);
        }
    
        return [new ArtifactRecorded(new GovernedArtifact(
            command.ArtifactId,
            command.Kind,
            command.Title.Trim(),
            command.Content,
            command.WorkItemId,
            command.ProducerRunId,
            command.SupersedesArtifactId,
            new Provenance(command.ActorId, now, "artifact.record")))];
    }

    private static void EnsureArtifactScope(GovernedArtifactKind kind, WorkItemId? workItemId)
    {
        var workScoped = kind is GovernedArtifactKind.VerifierOutput or GovernedArtifactKind.CodeReviewOutput;
        if (workScoped != workItemId.HasValue)
        {
            throw new GovernanceException(workScoped
                ? $"A '{kind}' artifact must name its work item."
                : $"A '{kind}' artifact is task-wide and cannot name a work item.");
        }
    }

    private static void EnsureArtifactAuthority(
        GovernedTaskState state,
        GovernedArtifactKind kind,
        ActorId actorId,
        RoleKind actorRole,
        WorkItemId? workItemId,
        RunId? producerRunId)
    {
        if (kind == GovernedArtifactKind.UserRequest)
        {
            if (actorRole != RoleKind.Operator || producerRunId is not null)
            {
                throw new GovernanceException(
                    "A user-request artifact must be operator-authored and cannot name a producer run.");
            }
            return;
        }
    
        if (producerRunId is not { } runId)
        {
            throw new GovernanceException($"A '{kind}' artifact must name its active producer run.");
        }
    
        var run = Get(state.Runs, runId, "producer run");
        if (run.Status != AgentRunStatus.Active || run.ActorId != actorId)
        {
            throw new GovernanceException(
                $"Artifact producer run '{runId}' must be active and belong to actor '{actorId}'.");
        }
        if (workItemId is not null && run.WorkItemId != workItemId)
        {
            throw new GovernanceException($"Artifact work item must match producer run '{runId}'.");
        }
    
        var requiredRole = kind switch
        {
            GovernedArtifactKind.VerifierOutput => RoleKind.Verifier,
            GovernedArtifactKind.CodeReviewOutput => RoleKind.CodeReviewer,
            _ => (RoleKind?)null
        };
        if (requiredRole is { } role && run.SubjectRole != role)
        {
            throw new GovernanceException($"A '{kind}' artifact requires a matching active {role} run.");
        }
        if (requiredRole is null && run.SubjectRole is not
            (RoleKind.Operator or RoleKind.PlanningLead or RoleKind.ImplementationLead))
        {
            throw new GovernanceException(
                "Only an operator, planning lead, or implementation lead can record a governing workflow artifact.");
        }
    }

    private static void EnsureArtifactRevision(
        GovernedTaskState state,
        GovernedArtifactKind kind,
        WorkItemId? workItemId,
        ArtifactId artifactId,
        ArtifactId? supersedesArtifactId)
    {
        var current = CurrentArtifacts(state)
            .Where(item => item.Kind == kind && item.WorkItemId == workItemId)
            .ToArray();
        if (supersedesArtifactId is not { } predecessorId)
        {
            if (current.Length != 0)
            {
                throw new GovernanceException(
                    $"A current '{kind}' artifact already exists for this scope; a revision must supersede it.");
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

    internal static IReadOnlyList<GovernedArtifact> CurrentArtifacts(GovernedTaskState state)
    {
        var superseded = state.Artifacts.Values
            .Where(item => item.SupersedesArtifactId is not null)
            .Select(item => item.SupersedesArtifactId!.Value)
            .ToHashSet();
        return state.Artifacts.Values.Where(item => !superseded.Contains(item.ArtifactId)).ToArray();
    }

    private static void ValidateVerifierOutput(
        GovernedTaskState state,
        WorkItemId workItemId,
        string content)
    {
        var workItem = Get(state.WorkItems, workItemId, "work item");
        var assumptions = ReadMarkdownTable(content, ["id", "status", "name", "citation", "actor"]);
        var assumptionIds = assumptions.Select(row => row[0]).ToArray();
        EnsureUnique(assumptionIds, "Verifier assumption disposition IDs", StringComparer.Ordinal);
        foreach (var claimId in workItem.DependsOnClaims.Where(id =>
                     state.Claims[id].Status is ClaimStatus.Open or ClaimStatus.Validated))
        {
            var row = assumptions.SingleOrDefault(candidate => candidate[0] == claimId.Value)
                ?? throw new GovernanceException($"Verifier output must dispose dependent claim '{claimId}'.");
            if (row[1] is not ("VALIDATED" or "REJECTED" or "NEVER-TESTED") ||
                string.IsNullOrWhiteSpace(row[2]) || string.IsNullOrWhiteSpace(row[3]) ||
                string.IsNullOrWhiteSpace(row[4]))
            {
                throw new GovernanceException(
                    $"Verifier disposition for claim '{claimId}' must be terminal and carry a name, citation, and actor.");
            }
        }
    
        var plan = CurrentArtifacts(state).SingleOrDefault(item =>
            item.Kind == GovernedArtifactKind.OrchestrationPlan);
        if (plan is null)
        {
            throw new GovernanceException("A verifier output requires a current orchestration plan.");
        }
        var attentionIds = plan.Content.Contains("No material attention items", StringComparison.OrdinalIgnoreCase)
            ? Array.Empty<string>()
            : ReadMarkdownTable(plan.Content,
                    ["id", "name", "failure mode", "causal path and impact", "planned handling", "source"])
                .Select(row => row[0]).Where(IsAttentionId).ToArray();
        EnsureUnique(attentionIds, "Orchestration-plan attention item IDs", StringComparer.Ordinal);
    
        var dispositions = ReadMarkdownTable(content, ["id", "final disposition", "name", "evidence"]);
        var dispositionIds = dispositions.Select(row => row[0]).ToArray();
        EnsureUnique(dispositionIds, "Verifier attention disposition IDs", StringComparer.Ordinal);
        foreach (var attentionId in attentionIds)
        {
            var row = dispositions.SingleOrDefault(candidate => candidate[0] == attentionId)
                ?? throw new GovernanceException($"Verifier output must dispose attention item '{attentionId}'.");
            if (row[1] is not ("handled" or "accepted-risk" or "not-applicable" or "unresolved") ||
                string.IsNullOrWhiteSpace(row[2]) || string.IsNullOrWhiteSpace(row[3]))
            {
                throw new GovernanceException(
                    $"Verifier attention disposition '{attentionId}' must use an allowed value and carry a name and evidence.");
            }
        }
    }

    private static IReadOnlyList<string[]> ReadMarkdownTable(string content, IReadOnlyList<string> header)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var cells = SplitMarkdownRow(lines[index]);
            if (!cells.SequenceEqual(header, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
    
            var rows = new List<string[]>();
            for (var rowIndex = index + 2; rowIndex < lines.Length; rowIndex++)
            {
                var row = SplitMarkdownRow(lines[rowIndex]);
                if (row.Length != header.Count)
                {
                    break;
                }
                rows.Add(row);
            }
            return rows;
        }
    
        throw new GovernanceException($"Artifact body is missing the required '{string.Join(" | ", header)}' table.");
    }

    private static string[] SplitMarkdownRow(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('|') || !trimmed.EndsWith('|'))
        {
            return [];
        }
        return trimmed[1..^1].Split('|').Select(cell => cell.Trim()).ToArray();
    }

    private static bool IsAttentionId(string value)
    {
        if (value.Length < 2 || value[0] != 'R')
        {
            return false;
        }
        var digitCount = value.Skip(1).TakeWhile(char.IsDigit).Count();
        return digitCount > 0 && (digitCount == value.Length - 1 ||
            digitCount == value.Length - 2 && char.IsLetter(value[^1]));
    }
}
