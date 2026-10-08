using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using static AILedger.Core.ContextBriefing.ContractObservations;

namespace AILedger.Core.ContextBriefing;

public static partial class NextActionContracts
{
    private static ActionContract Recon(GovernedTaskState state) => new(
        "recon/research before planning", "Operator", "PlanningLead/ImplementationLead; Researcher for external questions",
        "InternalRecon strict JSON: {schemaVersion:1,taskId,claimSetHash,assessments:[{claimId,domain:internal|external}],report}. " +
        "Every historical claim exactly once, report nonblank. Research return: cited findings, limitations, implement-now/verify-first recommendation and evidence IDs; declare_producer_outcome for Researcher.",
        [Artifact(state, GovernedArtifactKind.InternalRecon),
         new("claim-set", "Bind recon consultation and artifact to current task and claim hash; active own task-wide lead producer at Research/Design.",
             "unknown", "executable", "InternalReconDocuments; InternalReconRules", [state.TaskId.Value, InternalReconDocuments.ComputeClaimSetHash(state)],
             $"{state.Claims.Count} historical claims require assessments; recon template remains available through artifact template."),
         Stage(state, TaskStage.Design),
         Guidance("research", "External assessments need completed Researcher consultation in this Research episode and resolution of open external claims. Researcher findings supplement lead recon; claim resolution requires ResolveClaim.",
             "technical-researcher; StagePrerequisiteRules.EnsureResearchConsulted"), HostPreparation()]);

    private static ActionContract Plan(GovernedTaskState state) => new(
        PlanningAction(state), "Operator", "PlanningLead/ImplementationLead",
        OrchestrationPlanDocuments.AttentionTableTemplate +
        "| R1 | descriptive-name | failure | cause and impact | test: named artifact | citation |\n" +
        $"Or state '{OrchestrationPlanDocuments.NoMaterialAttentionItems} — <task-specific reason>'. " +
        "Recognized IDs: uppercase R + digits + optional trailing letter, unique; no name in ID cell. Reason and substantive cells are workflow obligations; parser admission is narrower.",
        [Stage(state, TaskStage.Design), Stage(state, TaskStage.Scope),
         Artifact(state, GovernedArtifactKind.PromptContract),
         PlanShape(state),
         new("producer", "Task-wide active own Operator/PlanningLead/ImplementationLead producer; RecordArtifact capability. PromptContract at Design, OrchestrationPlan at Design/Scope. Revision names current predecessor via --supersedes.",
             "unknown", "executable", "ArtifactAuthorityRules; ArtifactRevisionRules; EntryActionStageRules", []),
         Guidance("planning-sections", "Preflight Evidence; Source Obligation Map; Proposed Change Walkthrough; Consequential Assumptions and Recon Stop. These four sections are workflow guidance: current plan admission does not enforce their schemas. PromptContract guidance: Role, Goal, Context, Constraints, Success Criteria, Execution Rules, Output Format, Stop Conditions.",
             "task-orchestrator; workflow-coordinator; prompt-contract-designer"),
         Guidance("replanning", "After returning from a later stage, complete reconsideration lesson consultation in the new episode before Scope; refresh recon when claim hash changes. Do not waive recon/consultations.", "StagePrerequisiteRules; task-orchestrator")]);

    private static string PlanningAction(GovernedTaskState state)
    {
        var current = ArtifactRevisionRules.Current(state);
        if (!current.Any(artifact => artifact.Kind == GovernedArtifactKind.PromptContract))
            return "author/revise planning contract before planning dispatch";
        return current.Any(artifact => artifact.Kind == GovernedArtifactKind.OrchestrationPlan)
            ? "reconcile planning prerequisites before execution"
            : "author orchestration plan from the current prompt contract";
    }

    private static ContractRequirement PlanShape(GovernedTaskState state)
    {
        var plan = ArtifactRevisionRules.Current(state).FirstOrDefault(a => a.Kind == GovernedArtifactKind.OrchestrationPlan);
        return plan is null ? Artifact(state, GovernedArtifactKind.OrchestrationPlan) :
            Check("plan-shape", "Current plan uses the shared attention contract. Historical malformed plans require authorized revision; history remains readable.",
                "OrchestrationPlanDocuments.ReadAttentionIdsForVerification", () =>
                { OrchestrationPlanDocuments.ReadAttentionIdsForVerification(plan); }, plan.ArtifactId.Value);
    }

    private static ActionContract Worker(GovernedTaskState state, ActorId actor, WorkItemId? work) => new(
        "prepare work and dispatch worker", "Operator", "Worker",
        "work add: id,title,owner,depends-on claims,scope; multiple scopes require not-split-because alternative. Worker return: changed outputs, validation commands/results, residual risks and execution_notes.md, then declare_producer_outcome with existing output/blocker evidence IDs.",
        [Stage(state, TaskStage.Ready), Stage(state, TaskStage.Execution),
         Artifact(state, GovernedArtifactKind.UserRequest), Artifact(state, GovernedArtifactKind.PromptContract), PlanShape(state),
         new("work-selection", "Select a viable owned work item at Ready with current claim dependencies, disjoint scopes and a current operator brief.",
             work is null ? "missing" : "unknown", "executable", "WorkItemLifecycleRules; ContextGateRules",
             work is null ? [] : [work.Value.Value]),
         Dispatch(RoleKind.Worker, state),
         Guidance("role-boundary", "Ready staffing accepts a broader working-role predicate than Verification entry (Worker) or work completion (Worker/Researcher). Staffing is not proof work ran.", "StagePrerequisiteRules; WorkItemVerificationRules"),
         HostPreparation()]);
}
