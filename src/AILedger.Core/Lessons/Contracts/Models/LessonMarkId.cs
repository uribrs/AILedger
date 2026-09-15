namespace AILedger.Core.Contracts;

public readonly record struct LessonMarkId(string Value)
{
    public override string ToString() => Value;
}
