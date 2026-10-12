namespace AILedger.Storage;

// Only proven lock contention has this type. Other I/O failures must remain fatal to monitors.
public sealed class TaskMutationLockTimeoutException : IOException
{
    internal TaskMutationLockTimeoutException(TimeSpan wait, Exception? inner = null)
        : base($"Could not acquire task mutation lock within {wait.TotalSeconds:0} seconds.", inner) { }
}
