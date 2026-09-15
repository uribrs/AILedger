namespace AILedger.Core.Contracts;

public readonly record struct DecisionId(string Value)
{
    public override string ToString() => Value;
}
