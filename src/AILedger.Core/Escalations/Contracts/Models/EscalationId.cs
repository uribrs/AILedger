namespace AILedger.Core.Contracts;

public readonly record struct EscalationId(string Value)
{
    public override string ToString() => Value;
}
