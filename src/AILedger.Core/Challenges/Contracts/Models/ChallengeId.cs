namespace AILedger.Core.Contracts;

public readonly record struct ChallengeId(string Value)
{
    public override string ToString() => Value;
}
