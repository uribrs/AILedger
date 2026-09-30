using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Stages;
using static AILedger.Core.ContextBriefing.ContractObservations;

namespace AILedger.Core.ContextBriefing;

// Coordinator response projection only. Admission stays with the owning rules, including their
// distinct role predicates. No packet is stored as task truth or supplied to a blind reviewer.
public static partial class NextActionContracts
{
    public static bool IsCoordinator(GovernedTaskState state, ActorId actor) =>
        state.Roles.TryGetValue(actor, out var role) &&
        role.Role is RoleKind.Operator or RoleKind.PlanningLead or RoleKind.ImplementationLead;

    public static NextActionContract? Observe(GovernedTaskState state, ActorId actor,
        RunId? returnedRun = null, WorkItemId? selectedWork = null)
    {
        if (!IsCoordinator(state, actor)) return null;
        try
        {
            var run = returnedRun is { } id ? state.Runs.GetValueOrDefault(id) :
                state.Runs.Values.Where(r => r.Status != AgentRunStatus.Active &&
                    (selectedWork is null || WorkCoverage.Effective(r.WorkItemId, r.Assurance).Contains(selectedWork.Value)))
                    .OrderByDescending(r => r.EndedAt).ThenBy(r => r.Id.Value, StringComparer.Ordinal).FirstOrDefault();
            var work = selectedWork ?? run?.WorkItemId;
            var binding = new ContractBindings(work?.Value, run?.Id.Value, run?.ActorId.Value,
                run?.Assurance?.CandidateId, run?.SubjectRole == RoleKind.Verifier ? run.Id.Value : run?.Assurance?.VerifierRunId?.Value,
                run?.Assurance);
            return ContractPacketBudget.Apply(new(state.Version, "observed", SelectActions(state, actor, run, work), binding,
                run is null ? null : ObserveReturn(state, run)));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Delivery is observational. Never turn an already committed mutation into a refusal.
            return Unavailable(state.Version);
        }
    }

    public static NextActionContract Unavailable(long version) => new(version, "unknown", [],
        new(null, null, null, null, null, null), null,
        Diagnostic: "Contract observation unavailable; committed operation and provider outcome are unchanged. No readiness was established.");

    private static IReadOnlyList<ActionContract> SelectActions(GovernedTaskState state, ActorId actor,
        AgentRun? run, WorkItemId? work) => state.Stage switch
    {
        TaskStage.Discovery or TaskStage.Research => [Recon(state), Plan(state)],
        TaskStage.Design or TaskStage.Scope => HasPlan(state) ? [Worker(state, actor, work), Plan(state)] : [Plan(state)],
        TaskStage.Ready => [Worker(state, actor, work)],
        TaskStage.Execution or TaskStage.Repair when run?.SubjectRole == RoleKind.Worker =>
            [Verify(state, work), Recovery(state)],
        TaskStage.Execution or TaskStage.Repair => [Worker(state, actor, work)],
        TaskStage.Verification when run?.SubjectRole == RoleKind.Verifier && run.Status != AgentRunStatus.Active =>
            ObserveReturn(state, run).RequiredOutputPresence == "present"
                ? [Review(state, run, work), Recovery(state)]
                : [Verify(state, work), Recovery(state)],
        TaskStage.Verification => [Verify(state, work)],
        TaskStage.Review => [Review(state, run, work), Recovery(state)],
        _ => []
    };

    private static bool HasPlan(GovernedTaskState state) => ArtifactRevisionRules.Current(state)
        .Any(a => a.Kind == GovernedArtifactKind.OrchestrationPlan);

    private static ContractRequirement Stage(GovernedTaskState state, TaskStage target) =>
        Check("stage-" + target, $"{target} prerequisites (not a launch authorization). Backward transitions require a reason; Verification may require a recorded serial justification.",
            "StagePrerequisiteRules.EnsureSatisfied; StageTransitionPolicy", () =>
            {
                // Inspect prerequisites even before the legal edge is imminent. This does not
                // pretend a multi-edge route is an immediately executable transition.
                StagePrerequisiteRules.EnsureSatisfied(state, target, null);
            }, $"stage:{state.Stage}");

    private static ContractRequirement Dispatch(RoleKind role, GovernedTaskState state) =>
        new("dispatch", $"Operator dispatch on subject's behalf; subject must hold {role}, current authority/brief, viable owned scope and dependencies; no active scope intersection. Execution rechecks admission.",
            "unknown", "executable", "RunAdmission; RunDispatchRules; ContextGateRules; EntryActionStageRules",
            state.Roles.Values.Where(r => r.Role == role).Take(8).Select(r => r.ActorId.Value).ToArray(),
            ReferenceCount: state.Roles.Values.Count(r => r.Role == role));

    private static ContractRequirement HostPreparation() => Host("host-preparation",
        "Ordinary fresh launch prepares the required role-filtered brief, reviewer bindings/profile and configured assurance inputs/grants before run start and version probing. Configured area/check IDs accompany compatible briefs. No host check has run for this next dispatch yet: setup, physical candidate/repository validity and independent acceptance remain unknown here. Admission and mutable inputs are rechecked at use; preparation is not acceptance or future readiness.",
        "ContextManifestBudget; ProviderLauncher.Assurance; AssuranceHost");
}
