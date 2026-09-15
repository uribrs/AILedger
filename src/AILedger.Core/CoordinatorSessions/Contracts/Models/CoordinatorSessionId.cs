namespace AILedger.Core.Contracts;

// The bracket around one coordinating conversation. It is not a RunId and must not be one: a run
// carries a provider, a model, a provider session id and a timeout, and all four are null for a
// coordinator (D1, ALT1).
public readonly record struct CoordinatorSessionId(string Value)
{
    public override string ToString() => Value;
}
