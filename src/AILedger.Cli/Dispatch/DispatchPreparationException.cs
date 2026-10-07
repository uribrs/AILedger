namespace AILedger.Cli.Dispatch;

internal sealed class DispatchPreparationException(DispatchFailureKind kind, string code, Exception cause)
    : Exception(cause.Message, cause)
{
    public DispatchFailureKind Kind { get; } = kind;
    public string Code { get; } = code;
    public Exception CompatibilityException { get; } = cause;
}
