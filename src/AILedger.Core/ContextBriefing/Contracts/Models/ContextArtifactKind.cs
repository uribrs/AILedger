namespace AILedger.Core.Contracts;

public enum ContextArtifactKind
{
    Rules,
    Skill,
    TaskGoal,
    Constraint,
    Claim,
    Decision,
    Evidence,
    WorkItem,
    StopCondition,
    UserRequest,
    PromptContract,
    OrchestrationPlan,
    VerifierOutput,
    // Appended: the enum's declaration order is the manifest sort order.
    Escalation,
    Alternative,
    Lesson,
    LessonMark,
    // The fifth governed workflow kind. It arrives last rather than beside VerifierOutput because
    // the declaration order is the sort order, and reordering would move every other kind in every
    // manifest a stored task has already produced.
    CodeReviewOutput
}
