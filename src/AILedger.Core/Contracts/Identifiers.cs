namespace AILedger.Core.Contracts;

public readonly record struct TaskId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ActorId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct RunId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct EventId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct LessonId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct LessonMarkId(string Value)
{
    public override string ToString() => Value;
}
