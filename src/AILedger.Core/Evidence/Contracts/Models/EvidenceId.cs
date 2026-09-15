namespace AILedger.Core.Contracts;

public readonly record struct EvidenceId(string Value)
{
    public override string ToString() => Value;
}
