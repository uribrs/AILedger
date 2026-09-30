using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Runs;
using AILedger.Core.WorkItems;
using static AILedger.Core.ContextBriefing.ContractObservations;

namespace AILedger.Core.ContextBriefing;

public static partial class NextActionContracts
{
    private static ActionContract Verify(GovernedTaskState state, WorkItemId? work) => new(
        "verify or recover missing verifier output", "Operator", "Verifier",
        "AssuranceBinding: {schemaVersion:1,workItemIds:[exact members],workVersions:[{workItemId,workingRunId}],candidateId:<64 lowercase hex>,verifierRunId:null}. Fresh session, provider independent from each member's latest working provider. " +
        "Submit inline submit_artifact {schema_version:1,request_id,kind:verifier-output,title,content[,supersedes_artifact_id]}; host binds producer/work/candidate.\n" + VerifierOutputDocuments.AuthoringShape,
        [Stage(state, TaskStage.Verification), LatestWork(state, work), PlanShape(state),
         new("output", "Active Verifier owns output at Verification, exactly bound to work or candidate/member/provenance union. Completion requires applicable output. A failed run cannot file retroactively: dispatch a fresh authorized verifier, or replan malformed upstream artifacts.",
             "unknown", "executable", "RunLifecycleRules; ArtifactAuthorityRules; AssuranceArtifactRules; VerifierOutputRules", []),
         Dispatch(RoleKind.Verifier, state),
         Guidance("acceptance", "Reconcile producer evidence and blockers before choosing verification versus repair/replanning. A reported-complete declaration is self-report; process termination and output filing do not establish technical success.", "contract-driven-execution; workflow-coordinator"),
         HostPreparation()]);

    private static ContractRequirement LatestWork(GovernedTaskState state, WorkItemId? work) => work is null
        ? new("working-provenance", "Select member work and latest completed real working run per member.", "missing", "executable", "AssuranceRules.LatestWork", [])
        : Check("working-provenance", "Selected member needs an unambiguous latest completed real Worker/Researcher run; remaining members and physical candidate require selection/checking.",
            "AssuranceRules.LatestWork", () => { AssuranceRules.LatestWork(state, work.Value); }, work.Value.Value);

    private static ActionContract Review(GovernedTaskState state, AgentRun? run, WorkItemId? work) => new(
        "isolated review after explicit findings reconciliation", "Operator", "CodeReviewer",
        "Fresh review uses identical candidateId, exact workItemIds/workVersions and verifierRunId naming the current completed independent verifier. Submit inline submit_artifact {schema_version:1,request_id,kind:code-review-output,title,content[,supersedes_artifact_id]}. Nonblank output required; guidance: severity, problem, impact, fix and code citation.",
        [Stage(state, TaskStage.Review), VerifierPair(state, run, work),
         Dispatch(RoleKind.CodeReviewer, state),
         Guidance("branch-decision", "A filed verifier report can contain unresolved findings. Decide review versus repair explicitly from evidence; no automatic favorable verdict or acceptance is inferred.", "workflow-coordinator"),
         Host("blind-profile", "Blind reviewer must receive code-only permitted context: no coordinator packet, task requirements, plan or verifier narratives. Ordinary launch refuses the current requirements-aware assurance profile for this role before run start. Selected setup is uninspected here; use an independently authorized compatible context without widening blind review or omitting required assurance.",
             "ContextAssembler; ContextRolePolicy; AssuranceHost"),
         Host("independent-assurance", "Task-13 reports/check receipts and explicit independent acceptance live in the configured assurance store, not this task projection. Their outcome, current applicability, grants and configured area/check identifiers are unknown here; existing scoped assurance tools are authoritative when supplied.",
             "AssuranceContracts; AssuranceHost"), HostPreparation()]);

    private static ContractRequirement VerifierPair(GovernedTaskState state, AgentRun? run, WorkItemId? work)
    {
        if (work is null) return new("verifier-pair", "Select work and completed current verifier before review.",
            "missing", "executable", "AssuranceRules; WorkItemVerificationRules", []);
        return Check("verifier-pair", "Every member requires current verification after latest work. Bound review must retain identical membership, candidate and working provenance, and exact verifier pair.",
            "AssuranceRules.QualifiesVerifier; WorkItemVerificationRules.HasVerifierRunAfterLatestWork", () =>
            {
                if (run?.Assurance is { } assurance)
                {
                    var verifier = run.SubjectRole == RoleKind.Verifier ? run :
                        assurance.VerifierRunId is { } pair ? state.Runs.GetValueOrDefault(pair) : null;
                    if (verifier?.Assurance is null || !AssuranceRules.Same(assurance, verifier.Assurance, includePair: false))
                        throw new GovernanceException("A completed verifier with identical candidate, members and provenance must be selected.");
                    foreach (var member in assurance.WorkItemIds)
                        if (!AssuranceRules.QualifiesVerifier(state, verifier, member))
                            throw new GovernanceException($"Current independent verifier output is missing for member '{member}'.");
                }
                else if (!WorkItemVerificationRules.HasVerifierRunAfterLatestWork(state, work.Value))
                    throw new GovernanceException("Current verifier after latest work is missing.");
            }, work.Value.Value, run?.Id.Value ?? "verifier unselected");
    }

    private static ActionContract Recovery(GovernedTaskState state) => new(
        "choose repair/replanning branch", "Operator", "PlanningLead then Worker",
        "Use existing legal stage transitions; backward edges require --reason. Repair needs open challenge or current VerifierOutput. Replanning through Design/Research requires refreshed recon and applicable reconsideration consultation before forward Scope. New working provenance needs fresh verification/review bindings.",
        [new("legal-routes", "Legal immediate targets only; edges do not certify prerequisites.", "satisfied", "executable", "StageTransitionPolicy.LegalTargets",
             StageTransitionPolicy.LegalTargets(state.Stage).Select(s => s.ToString()).ToArray()),
         Stage(state, TaskStage.Repair),
         Guidance("reconcile", "Missing outputs: recover with a new authorized producer while preserving partial findings. Blocked/partial declarations: reconcile cited blockers. Undeclared or historical outcomes remain unknown. Choose evidence-based repair, resumed investigation or replan; no branch is accepted automatically.", "workflow-coordinator")]);
}
