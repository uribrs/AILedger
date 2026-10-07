using AILedger.Core.Assurance;
using AILedger.Core.Contracts;

namespace AILedger.Storage;

public sealed partial class FileGovernedTaskService
{
    public string WorkspaceRoot => _pathResolver.WorkspaceRoot;
    private IWorkCompletionAdmission? _completionAdmission;

    // Read-only trusted observation for closeout scheduling. The eventual stage append acquires
    // fresh admission again; this return value is not a reservation or a serialized authority.
    public async Task ObserveCompletedWorkAsync(TaskId task, WorkItemId work, CancellationToken token)
    {
        var directory = _pathResolver.Resolve(task);
        await using var gate = await _mutationLock.AcquireAsync(Path.Combine(directory, _layout.LockFileName), token).ConfigureAwait(false);
        await CheckCoordinationAsync(directory, token).ConfigureAwait(false);
        var state = await ReplayAsync(task, directory, token).ConfigureAwait(false);
        if (state is null || state.WorkItems.GetValueOrDefault(work)?.Status != WorkItemStatus.Completed || _completionAdmission is null)
            throw new AssuranceRefusal("missing_acceptance", "Closeout scheduling needs completed work and a configured acceptance observer.");
        await using var admission = await _completionAdmission.AcquireAsync(state, work, token).ConfigureAwait(false);
        await admission.RevalidateAsync(token).ConfigureAwait(false);
        await CheckCoordinationAsync(directory, token).ConfigureAwait(false);
    }

    // Trusted composition only; the validator is never supplied by a provider request.
    public FileGovernedTaskService BindCompletionAdmission(IWorkCompletionAdmission admission)
    {
        var service = CopyService(_commandHandler);
        service._completionAdmission = admission;
        return service;
    }
}
