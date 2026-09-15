namespace AILedger.Core.Contracts;

public readonly record struct WorkItemId(string Value)
{
    public override string ToString() => Value;
}
