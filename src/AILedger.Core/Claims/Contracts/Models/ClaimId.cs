namespace AILedger.Core.Contracts;

public readonly record struct ClaimId(string Value)
{
    public override string ToString() => Value;
}
