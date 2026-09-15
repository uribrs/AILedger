namespace AILedger.Core.Contracts;

public readonly record struct RunId(string Value)
{
    public override string ToString() => Value;
}
