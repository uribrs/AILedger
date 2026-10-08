using AILedger.Core.Contracts;

namespace AILedger.Storage;

public sealed partial class FileGovernedTaskService
{
    private static async Task EnsureReconSourcesAsync(GovernedTaskState? state, LedgerCommand command,
        CancellationToken token)
    {
        // Validate physical inputs only on new writes, after domain authorization/shape validation.
        // Exact durable retries return before here. Replay remains independent of today's files.
        if (command is RecordArtifactCommand { Kind: GovernedArtifactKind.InternalRecon } record)
            await ReconSourceFiles.EnsureCurrentAsync(record.Content, token).ConfigureAwait(false);
        if (state is not null && command is RequestStageTransitionCommand { WithoutPrerequisitesReason: null } transition &&
            ((state.Stage == TaskStage.Research && transition.TargetStage == TaskStage.Design) ||
             (state.Stage == TaskStage.Design && transition.TargetStage == TaskStage.Scope)))
        {
            var recon = ArtifactApplicability.Current(state).Single(a => a.Kind == GovernedArtifactKind.InternalRecon);
            await ReconSourceFiles.EnsureCurrentAsync(recon.Content, token).ConfigureAwait(false);
        }
    }
}
