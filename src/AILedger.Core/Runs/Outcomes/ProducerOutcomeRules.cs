using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Runs;

internal static class ProducerOutcomeRules
{
    internal static IReadOnlyList<LedgerEventData> Record(GovernedTaskState state, DeclareProducerOutcomeCommand command)
    {
        var run = Get(state.Runs, command.RunId, "producer run");
        EnsureOwner(state, command.ActorId, run);
        ValidateShape(command.Declaration);
        // One terminal declaration per run. Exact retries remain readable after termination;
        // different content cannot overwrite a prior declaration, including on a lost response.
        if (run.ProducerOutcome is { } prior)
        {
            if (!Same(prior, command.Declaration))
                throw new GovernanceException("Producer outcome already declared; retry the identical declaration.");
            return [];
        }
        EnsureNewDeclaration(state, command.ActorId, run, command.Declaration);
        return [new ProducerOutcomeDeclared(run.Id, command.Declaration with
        {
            OutputEvidenceIds = command.Declaration.OutputEvidenceIds.ToArray(),
            BlockerEvidenceIds = command.Declaration.BlockerEvidenceIds.ToArray()
        })];
    }

    internal static void ValidateEvent(GovernedTaskState state, ActorId actor, ProducerOutcomeDeclared declared)
    {
        var run = Get(state.Runs, declared.RunId, "producer run");
        EnsureOwner(state, actor, run);
        ValidateShape(declared.Declaration);
        if (run.ProducerOutcome is not null) throw new GovernanceException("Producer outcome already declared.");
        EnsureNewDeclaration(state, actor, run, declared.Declaration);
    }

    private static void EnsureOwner(GovernedTaskState state, ActorId actor, AgentRun run)
    {
        var assignment = Get(state.Roles, actor, "actor");
        if (run.ActorId != actor || run.SubjectRole is not (RoleKind.Worker or RoleKind.Researcher) ||
            assignment.Role != run.SubjectRole || !assignment.Capabilities.Contains(Capability.AddEvidence))
            throw new GovernanceException("Only the owning Worker/Researcher with AddEvidence may declare its run outcome.");
    }

    private static void EnsureNewDeclaration(GovernedTaskState state, ActorId actor, AgentRun run, ProducerOutcome declaration)
    {
        if (run.Status != AgentRunStatus.Active)
            throw new GovernanceException("A new producer outcome requires its active run.");
        foreach (var id in declaration.OutputEvidenceIds.Concat(declaration.BlockerEvidenceIds))
        {
            var evidence = Get(state.Evidence, id, "outcome evidence");
            if (evidence.Provenance.ActorId != actor || evidence.Provenance.RecordedAt < run.StartedAt)
                throw new GovernanceException("Outcome evidence must be recorded by this producer during its run.");
        }
    }

    private static void ValidateShape(ProducerOutcome declaration)
    {
        if (declaration is null || declaration.Outcome is not ("reported-complete" or "blocked" or "partial") ||
            declaration.OutputEvidenceIds is null || declaration.BlockerEvidenceIds is null ||
            declaration.OutputEvidenceIds.Count + declaration.BlockerEvidenceIds.Count is < 1 or > 16)
            throw new GovernanceException("Outcome requires reported-complete/blocked/partial and 1–16 existing evidence references.");
        var ids = declaration.OutputEvidenceIds.Concat(declaration.BlockerEvidenceIds).ToArray();
        if (ids.Any(id => string.IsNullOrWhiteSpace(id.Value) || id.Value.Length > 256) || ids.Distinct().Count() != ids.Length)
            throw new GovernanceException("Outcome evidence IDs must be unique, nonblank and at most 256 characters.");
        if (declaration.Outcome == "blocked" && declaration.BlockerEvidenceIds.Count == 0 ||
            declaration.Outcome == "reported-complete" &&
            (declaration.OutputEvidenceIds.Count == 0 || declaration.BlockerEvidenceIds.Count != 0))
            throw new GovernanceException("Blocked requires blocker evidence; reported-complete requires output evidence and no blockers.");
    }

    private static bool Same(ProducerOutcome left, ProducerOutcome right) => left.Outcome == right.Outcome &&
        left.OutputEvidenceIds.SequenceEqual(right.OutputEvidenceIds) && left.BlockerEvidenceIds.SequenceEqual(right.BlockerEvidenceIds);
}
