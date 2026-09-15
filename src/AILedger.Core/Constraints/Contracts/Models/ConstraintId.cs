namespace AILedger.Core.Contracts;

public readonly record struct ConstraintId(string Value)
{
    public override string ToString() => Value;
}
