namespace AILedger.Core.Contracts;

// Which cognition established the lesson, and deliberately not RoleKind. This vocabulary is the
// ledger row format's, and the two do not line up: Executor and Recon have no role here, and
// Operator, PlanningLead, Worker and CodeReviewer mean nothing to a recalled row.
public enum LessonActor
{
    Researcher,
    Executor,
    Verifier,
    Recon
}
