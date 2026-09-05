namespace AILedger.Core.Domain;

public sealed class GovernanceException : InvalidOperationException
{
    public GovernanceException(string message)
        : base(message)
    {
    }
}
