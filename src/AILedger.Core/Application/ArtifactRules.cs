using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Replay counterparts: ValidateArtifactRecorded, ValidateArtifactProducer, ValidateArtifactRevision, ValidateVerifierOutput.
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
        if (command.Kind == GovernedArtifactKind.WorkflowRetrospective)
        {
            EnsureRetrospectiveEntryCondition(state);
            ValidateWorkflowRetrospective(command.Content);
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
    
        // A retrospective is filed once the task is archived, when no run is active by definition, so
        // it can no more name a producer run than a user request can. Requiring one would have cost a
        // permanently cancelled run on every task the feature measures: an operator-held run carries
        // no provider session and can never close 'completed'. The role is therefore read from the
        // actor's own assignment, and it is the same set the producer-run path admits below.
        if (kind == GovernedArtifactKind.WorkflowRetrospective)
        {
            if (producerRunId is not null)
            {
                throw new GovernanceException(
                    "A workflow-retrospective artifact is recorded after closeout and cannot name a producer run.");
            }
            if (actorRole is not (RoleKind.Operator or RoleKind.PlanningLead or RoleKind.ImplementationLead))
            {
                throw new GovernanceException(
                    "Only an operator, planning lead, or implementation lead can record a governing workflow artifact.");
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

    // Command-time only, and deliberately absent from TaskTransitionValidator. Every other arm added
    // to the replay copy keys on a field that only the new event carries, which makes it safe by
    // construction: no history written before that event existed can reach it. This one is different.
    // It keys on the task's aggregate state — its stage, its work items and its runs — which every
    // history has always carried. A later change to what counts as a live work item or an active run
    // would then retroactively refuse a history that was legal when it was written, and that has
    // twice made a live task permanently unreadable here. Command time may demand this; replay may
    // not.
    private static void EnsureRetrospectiveEntryCondition(GovernedTaskState state)
    {
        if (state.Stage != TaskStage.Archive)
        {
            throw new GovernanceException(RetrospectiveStageRefusal(state.Stage));
        }

        // Live is the same set ScopeOccupancyRules holds an area against: an item that is neither
        // completed, stale nor abandoned still belongs to somebody who could be briefed again.
        var live = state.WorkItems.Values
            .Where(item => item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Stale
                or WorkItemStatus.Abandoned))
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (live is not null)
        {
            throw new GovernanceException(RetrospectiveLiveWorkRefusal(live.Id, live.Status));
        }

        var active = state.Runs.Values
            .Where(run => run.Status == AgentRunStatus.Active)
            .OrderBy(run => run.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (active is not null)
        {
            throw new GovernanceException(RetrospectiveActiveRunRefusal(active.Id));
        }
    }

    // The three refusals of the entry condition, and the retrospective table's header and cell
    // vocabulary, are frozen surface: the CLI help line and the kernel tests both quote them. They
    // are written once here so the two rule copies and the two other work items cannot drift apart,
    // on the same grounds as TableRefusal below.
    internal static string RetrospectiveStageRefusal(TaskStage stage) =>
        $"A workflow retrospective is recorded only at stage 'Archive'; this task is at stage '{stage}'.";

    internal static string RetrospectiveLiveWorkRefusal(WorkItemId workItemId, WorkItemStatus status) =>
        $"A workflow retrospective is recorded only when no work item is live; '{workItemId}' is still " +
        $"live in status '{status}'.";

    internal static string RetrospectiveActiveRunRefusal(RunId runId) =>
        $"A workflow retrospective is recorded only when no run is active; run '{runId}' is still active.";

    internal static readonly IReadOnlyList<string> RetrospectiveTableHeader =
        ["dimension", "score", "confidence", "controllable", "evidence"];

    internal static readonly IReadOnlyList<string> RetrospectiveDimensionIds =
        ["D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "D10"];

    internal static readonly IReadOnlySet<string> RetrospectiveScores =
        new HashSet<string>(StringComparer.Ordinal) { "0", "1", "2", "3", "4", "5", "unmeasured" };

    internal static readonly IReadOnlySet<string> RetrospectiveConfidences =
        new HashSet<string>(StringComparer.Ordinal) { "low", "medium", "high" };

    internal static readonly IReadOnlySet<string> RetrospectiveControllable =
        new HashSet<string>(StringComparer.Ordinal) { "yes", "no", "not-applicable" };

    internal static string RetrospectiveTableRefusal() =>
        $"A workflow retrospective must carry the '{string.Join(" | ", RetrospectiveTableHeader)}' table " +
        $"with exactly the rows {string.Join(", ", RetrospectiveDimensionIds)}, each once. Allowed cell " +
        $"values are score {string.Join("/", RetrospectiveScores.Order(StringComparer.Ordinal))}, " +
        $"confidence {string.Join("/", RetrospectiveConfidences.Order(StringComparer.Ordinal))}, " +
        $"controllable {string.Join("/", RetrospectiveControllable.Order(StringComparer.Ordinal))}, and a " +
        "non-empty evidence cell. The vocabulary is checked and the score itself is not: every score " +
        "from 0 to 5 is equally acceptable here, because no rule in this kernel may read one.";

    // The format check, and only the format check. It reads the score cell to test that it is in the
    // vocabulary and never to decide anything else, so a body whose ten scores are all '0' is
    // accepted exactly as one whose scores are all '5' is. Nothing downstream may do more than this.
    private static void ValidateWorkflowRetrospective(string content)
    {
        var rows = ReadMarkdownTable(content, RetrospectiveTableHeader, RetrospectiveTableRefusal());
        var ids = rows.Select(row => row[0]).ToArray();
        EnsureUnique(ids, "Workflow retrospective dimension IDs", StringComparer.Ordinal);
        foreach (var dimensionId in RetrospectiveDimensionIds)
        {
            var row = rows.SingleOrDefault(candidate => candidate[0] == dimensionId)
                ?? throw new GovernanceException(
                    $"Workflow retrospective must score dimension '{dimensionId}'. " +
                    RetrospectiveTableRefusal());
            if (!RetrospectiveScores.Contains(row[1]) ||
                !RetrospectiveConfidences.Contains(row[2]) ||
                !RetrospectiveControllable.Contains(row[3]) ||
                string.IsNullOrWhiteSpace(row[4]))
            {
                throw new GovernanceException(
                    $"Workflow retrospective row '{dimensionId}' uses a value outside the frozen vocabulary. " +
                    RetrospectiveTableRefusal());
            }
        }

        var unknown = ids.Except(RetrospectiveDimensionIds, StringComparer.Ordinal).ToArray();
        if (unknown.Length != 0)
        {
            throw new GovernanceException(
                $"Workflow retrospective scores '{string.Join("', '", unknown)}', which is not a dimension. " +
                RetrospectiveTableRefusal());
        }
    }

    private static IReadOnlyList<string[]> ReadMarkdownTable(
        string content,
        IReadOnlyList<string> header,
        string? refusal = null)
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

        throw new GovernanceException(refusal ?? TableRefusal(header));
    }

    // C3: the kernel enforces part of task-orchestrator's specification and used to name neither
    // the skill nor the fact that it was a part. An actor learning the format from this refusal
    // therefore learned the columns and missed the cap, the naming convention and the mandatory
    // case — which is exactly what happened, and it cost a verifier run. Enforcing the rest is not
    // in this change's scope; saying that the rest exists is.
    //
    // Written once and used by both copies of the rule, so the two cannot drift apart.
    internal static string TableRefusal(IReadOnlyList<string> header) =>
        $"Artifact body is missing the required '{string.Join(" | ", header)}' table. " +
        "This format is specified by the 'task-orchestrator' skill, and the kernel checks only " +
        "part of it — the columns and the R-prefixed ids. The skill also caps attention items at " +
        "five, fixes the naming convention as 'R1 (descriptive-name)', and makes an item " +
        "mandatory when an artifact trace finds a design-invalidating interaction. Passing this " +
        "check is not the same as meeting the specification; read the skill.";

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
