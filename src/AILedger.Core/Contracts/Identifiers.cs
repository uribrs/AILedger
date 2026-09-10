namespace AILedger.Core.Contracts;

public readonly record struct TaskId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ActorId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ClaimId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct EvidenceId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct DecisionId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ChallengeId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct WorkItemId(string Value)
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

public readonly record struct EscalationId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct AlternativeId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ConstraintId(string Value)
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

public readonly record struct ArtifactId(string Value)
{
    public override string ToString() => Value;
}

// The bracket around one coordinating conversation. It is not a RunId and must not be one: a run
// carries a provider, a model, a provider session id and a timeout, and all four are null for a
// coordinator (D1, ALT1).
public readonly record struct CoordinatorSessionId(string Value)
{
    public override string ToString() => Value;
}
