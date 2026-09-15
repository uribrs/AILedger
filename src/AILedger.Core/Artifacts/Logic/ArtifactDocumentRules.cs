using AILedger.Core.Contracts;

namespace AILedger.Core.Artifacts;

internal static class ArtifactDocumentRules
{
    internal static void Validate(
        GovernedTaskState state,
        GovernedArtifactKind kind,
        WorkItemId? workItemId,
        string content)
    {
        switch (kind)
        {
            case GovernedArtifactKind.VerifierOutput:
                VerifierOutputRules.Validate(state, workItemId!.Value, content);
                break;
            case GovernedArtifactKind.WorkflowRetrospective:
                WorkflowRetrospectiveRules.Validate(content);
                break;
        }
    }
}
