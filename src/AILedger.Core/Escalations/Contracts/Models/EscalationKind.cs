namespace AILedger.Core.Contracts;

// Only two things may interrupt the operator: a tradeoff no amount of research settles, and a
// question the code and the sources cannot answer.
public enum EscalationKind
{
    BusinessDecision,
    TrueUnknown
}
