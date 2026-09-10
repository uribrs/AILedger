namespace AILedger.Core.Domain;

// Not a GovernanceException, and the difference is the whole point of the type.
//
// 'context build' must stay a read: a second brief for the same actor and the same skills has to
// append nothing and leave the version where it was. A caller that checks first and then submits
// cannot guarantee that — two agents briefing at the same instant both find nothing recorded and
// both append (VC2). So the check happens where the state is authoritative, inside the durable
// mutation lock, and the only way a rule can decline to append from there is to throw.
//
// It is a governed refusal in shape and not in meaning: nothing was wrong with the command and
// nothing needs repairing, so it must not reach the refusal journal, which is why it is not a
// GovernanceException. The caller catches it and reports the brief it already holds.
public sealed class ContextAlreadyBriefedException : InvalidOperationException
{
    public ContextAlreadyBriefedException(string message)
        : base(message)
    {
    }
}
