namespace AILedger.Core.Contracts;

public readonly record struct AlternativeId(string Value)
{
    public override string ToString() => Value;
}
