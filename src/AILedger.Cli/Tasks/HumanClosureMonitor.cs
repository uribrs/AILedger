using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;

namespace AILedger.Cli.Tasks;

// Cancellation reaches the adapter's existing process-tree cleanup. The closure event itself
// records loss of authority, not proof that an OS process has stopped.
internal sealed class HumanClosureMonitor : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationTokenSource _execution;
    private readonly Task _watch;

    private HumanClosureMonitor(IGovernedTaskService service, TaskId task, CancellationToken token)
    {
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        _execution = CancellationTokenSource.CreateLinkedTokenSource(token);
        _watch = WatchAsync(service, task);
    }

    internal CancellationToken Token => _execution.Token;

    internal static async Task<HumanClosureMonitor> StartAsync(IGovernedTaskService service, TaskId task, CancellationToken token)
    {
        var state = await service.GetStateAsync(task, token).ConfigureAwait(false);
        if (state?.HumanClosure is not null)
            throw new GovernanceException("Human operator closed the task; provider execution is fenced.");
        return new(service, task, token);
    }

    private async Task WatchAsync(IGovernedTaskService service, TaskId task)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
            {
                GovernedTaskState? state;
                try
                {
                    state = await service.GetStateAsync(task, _lifetime.Token).ConfigureAwait(false);
                }
                catch (TaskMutationLockTimeoutException)
                {
                    // Assurance checks retain the task lock through receipt commit. Contention
                    // is not loss of the task view; retry the authoritative read after the lease.
                    continue;
                }
                if (state?.HumanClosure is null) continue;
                await _execution.CancelAsync().ConfigureAwait(false);
                return;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or GovernanceException)
        {
            // Losing the authoritative task view while executing is not permission to continue.
            await _execution.CancelAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _watch.ConfigureAwait(false);
        _execution.Dispose();
        _lifetime.Dispose();
    }
}
