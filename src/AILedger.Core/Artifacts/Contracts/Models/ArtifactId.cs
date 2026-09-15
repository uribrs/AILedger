namespace AILedger.Core.Contracts;

public readonly record struct ArtifactId(string Value)
{
    public override string ToString() => Value;
}
