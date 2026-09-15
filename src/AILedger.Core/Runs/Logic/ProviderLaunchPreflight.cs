using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Runs;

namespace AILedger.Core.Application;

// What a launcher must ask before it starts anything. The kernel still refuses the launch at
// command time — this is the same rule, run earlier, so that the refusal arrives before a provider
// binary has been executed rather than after (VC3).
//
// It is a thin wrapper on purpose. The order the refusals speak in belongs to RunDispatchRules and is
// stated there once; restating it here would be a second copy of the rule and the next change would
// move only one of them.
public static class ProviderLaunchPreflight
{
    // The waiver the gate would produce is discarded here rather than returned: the launch records
    // it from the command below, and a pre-flight that emitted one would record a door opened for a
    // run that may still be refused on a rule this check does not carry.
    //
    // workItemId is the item the launch names. It is last and optional only so that the call site in
    // the CLI, which a concurrent change owns, keeps compiling; a launch that does not pass it skips
    // the coordinating-role refusal and pays for a version probe before the kernel refuses it, which
    // is the defect this parameter exists to close. It is not an argument any caller should omit.
    public static void EnsurePermitted(
        GovernedTaskState state,
        ActorId actorId,
        ActorId? subjectActorId,
        IReadOnlyList<ContextSkill>? servedNow,
        string? withoutBriefReason = null,
        EvidenceId? staleBriefEvidenceId = null,
        WorkItemId? workItemId = null) =>
        _ = RunDispatchRules.EnsurePermitted(
            state, actorId, subjectActorId, servedNow, isProviderLaunch: true,
            workItemId, withoutBriefReason, staleBriefEvidenceId);
}
