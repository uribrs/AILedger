namespace AILedger.Core.Contracts;

public readonly record struct LessonId(string Value)
{
    public override string ToString() => Value;
}
