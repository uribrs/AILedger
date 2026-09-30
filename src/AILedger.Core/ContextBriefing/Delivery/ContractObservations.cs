using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Runs;

namespace AILedger.Core.ContextBriefing;

internal static class ContractObservations
{
    internal static ContractRequirement Check(string id, string requirement, string source, Action check,
        params string[] references)
    {
        try { check(); return new(id, requirement, "satisfied", "executable", source, references); }
        catch (GovernanceException error)
        {
            return new(id, requirement, "missing", "executable", source, references, error.Message);
        }
    }

    internal static ContractRequirement Artifact(GovernedTaskState state, GovernedArtifactKind kind)
    {
        var artifacts = ArtifactRevisionRules.Current(state).Where(a => a.Kind == kind).ToArray();
        return new(kind.ToString(), $"Current {kind} required; presence does not establish technical acceptance.",
            artifacts.Length == 0 ? "missing" : "satisfied", "executable", "ArtifactRevisionRules.Current",
            artifacts.Take(8).Select(a => a.ArtifactId.Value).ToArray(), ReferenceCount: artifacts.Length);
    }

    internal static ContractRequirement Guidance(string id, string requirement, string source) =>
        new(id, requirement, "unknown", "workflow-guidance", source, []);

    internal static ContractRequirement Host(string id, string requirement, string source) =>
        new(id, requirement, "unknown", "host-dependent", source, []);

    internal static RunReturnObservation ObserveReturn(GovernedTaskState state, AgentRun run)
    {
        var outputs = ArtifactRevisionRules.Current(state).Where(a => a.ProducerRunId == run.Id).ToArray();
        var kind = run.SubjectRole switch
        {
            RoleKind.Verifier => GovernedArtifactKind.VerifierOutput,
            RoleKind.CodeReviewer => GovernedArtifactKind.CodeReviewOutput,
            _ => (GovernedArtifactKind?)null
        };
        var presence = "unknown";
        if (kind is { } required)
        {
            presence = run.Assurance is null
                ? outputs.Any(a => a.Kind == required) ? "present" : "missing"
                : run.Assurance.WorkItemIds.All(member => AssuranceRules.HasOutput(state, run, required, member))
                    ? "present" : "missing";
        }
        else if (run.SubjectRole is RoleKind.Worker or RoleKind.Researcher)
            presence = run.ProducerOutcome is null ? "unknown" :
                run.ProducerOutcome.OutputEvidenceIds.Count == 0 ? "missing" : "referenced-self-report";
        else if (outputs.Length != 0) presence = "present";
        return new(run.Id.Value, run.Status.ToString(), presence,
            outputs.Select(a => a.ArtifactId.Value).Concat(run.ProducerOutcome?.OutputEvidenceIds.Select(id => id.Value) ?? []).Take(8).ToArray(),
            run.ProducerOutcome?.Outcome ?? "unknown", run.ProducerOutcome,
            VerifierDispositions: outputs.Where(a => a.Kind == GovernedArtifactKind.VerifierOutput)
                .Take(8).Select(VerifierOutputDocuments.Observe).ToArray(),
            OutputReferenceCount: outputs.Length + (run.ProducerOutcome?.OutputEvidenceIds.Count ?? 0));
    }
}
