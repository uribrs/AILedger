namespace AILedger.Core.Contracts;

public enum GovernedArtifactKind
{
    UserRequest,
    PromptContract,
    OrchestrationPlan,
    VerifierOutput,
    CodeReviewOutput,
    // Appended last so that no already-serialized value moves. It scores how the kernel governed one
    // finished task across ten dimensions, and it is the one kind no agent is ever briefed on: a
    // score an actor can read about itself is a score an actor optimises. It is task-wide, carries no
    // producer run, and is recorded only after closeout.
    WorkflowRetrospective,
    // Appended last so existing serialized enum names and values remain unchanged. This task-wide,
    // no-producer artifact preserves closeout findings and retention judgements in the event log.
    CloseoutSynthesis
}
