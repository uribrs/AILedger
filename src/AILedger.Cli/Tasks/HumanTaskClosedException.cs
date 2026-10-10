namespace AILedger.Cli.Tasks;

internal sealed class HumanTaskClosedException() : OperationCanceledException(
    "The human operator closed this task successfully and ended agent authority.");
