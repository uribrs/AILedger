using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Storage;

// Every rule in this kernel is written twice — once in CommandHandler, once in
// TaskTransitionValidator — so the artifact record has to survive a real commit and a real replay,
// not only a handler call. The body is the part that has never travelled through the log before:
// 27 KB of markdown, front matter and all, has to come back byte for byte.
public sealed class ArtifactStoreReplayTests
{
    private const string ContractBody = "---\ntitle: Prompt contract\n---\n\n# Goal\n\n  Keep the indentation\n";

    [Fact]
    public async Task ArtifactsCommitAndReplayThroughTheFileStore()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("artifact-task");
        var actor = new ActorId("operator");
        var verifier = new ActorId("verifier");
        var area = Directory.CreateDirectory(Path.Combine(root.Path, "w1")).FullName;
        var writer = Service(root.Path);

        await Run(writer, taskId, new OpenTaskCommand(actor, null, "a1", taskId, "Task", "Goal"));
        await ContextBrief.RecordAsync(writer, taskId.Value);
        await Run(writer, taskId, Record(actor, "a2", "A1", GovernedArtifactKind.UserRequest, "The request"));
        await Run(writer, taskId, new AddWorkItemCommand(
            actor, null, "a2w", new WorkItemId("W1"), "Verified work", actor, [], [area]));
        await Run(writer, taskId, new StartRunCommand(
            actor, null, "a2r", new RunId("RP"), new WorkItemId("W1"), "codex", null, null, null, null, actor));
        await Run(writer, taskId, Record(
            actor, "a3", "A2", GovernedArtifactKind.PromptContract, ContractBody,
            producerRun: new RunId("RP")));
        // A revision, so the chain and not only a single record goes through the store.
        await Run(writer, taskId, Record(
            actor, "a4", "A3", GovernedArtifactKind.PromptContract, "The revised contract",
            producerRun: new RunId("RP"), supersedes: "A2"));
        await Run(writer, taskId, Record(
            actor, "a5", "A4", GovernedArtifactKind.OrchestrationPlan, ArtifactCommands.PlanBody,
            producerRun: new RunId("RP")));
        await Run(writer, taskId, new CompleteRunCommand(
            actor, null, "a6", new RunId("RP"), AgentRunStatus.Completed, "session-rp"));
        await Run(writer, taskId, new AssignRoleCommand(
            actor, null, "a7", verifier, RoleKind.Verifier, [Capability.BuildContext, Capability.RecordArtifact]));
        await Run(writer, taskId, new StartRunCommand(
            actor, null, "a8", new RunId("RV"), new WorkItemId("W1"), "claude", null, null, null, null, verifier));
        await Run(writer, taskId, Record(
            verifier, "a9", "A5", GovernedArtifactKind.VerifierOutput, ArtifactCommands.VerifierBody,
            workItem: new WorkItemId("W1"), producerRun: new RunId("RV")));
        await Run(writer, taskId, new CompleteRunCommand(
            actor, null, "a10", new RunId("RV"), AgentRunStatus.Completed, "session-rv"));

        // A separate service instance models a separate process, and the state file is removed so
        // the answer can only come from replaying events.jsonl.
        File.Delete(Path.Combine(root.Path, taskId.Value, "state.json"));
        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.NotNull(replayed);
        Assert.Equal(5, replayed.Artifacts.Count);
        Assert.Equal(ContractBody, replayed.Artifacts[new ArtifactId("A2")].Content);
        Assert.Equal(new ArtifactId("A2"), replayed.Artifacts[new ArtifactId("A3")].SupersedesArtifactId);
        Assert.Equal(new RunId("RV"), replayed.Artifacts[new ArtifactId("A5")].ProducerRunId);
        Assert.Equal(new WorkItemId("W1"), replayed.Artifacts[new ArtifactId("A5")].WorkItemId);
        Assert.Equal(AgentRunStatus.Completed, replayed.Runs[new RunId("RV")].Status);

        // The body lives in the log itself: this is what makes the ledger self-contained, and what
        // measured evidence E15 says is affordable at roughly one percent of the log budget.
        var events = await File.ReadAllTextAsync(Path.Combine(root.Path, taskId.Value, "events.jsonl"));
        Assert.Contains("artifact.recorded", events, StringComparison.Ordinal);
        Assert.Contains("Keep the indentation", events, StringComparison.Ordinal);
    }

    private static RecordArtifactCommand Record(
        ActorId actor,
        string correlation,
        string artifactId,
        GovernedArtifactKind kind,
        string content,
        WorkItemId? workItem = null,
        RunId? producerRun = null,
        string? supersedes = null) =>
        new(
            actor,
            null,
            correlation,
            new ArtifactId(artifactId),
            kind,
            "Governed document",
            content,
            workItem,
            producerRun,
            supersedes is null ? null : new ArtifactId(supersedes));

    private static FileGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }

    private static Task<CommandOutcome> Run(IGovernedTaskService service, TaskId taskId, LedgerCommand command) =>
        service.ExecuteAsync(taskId, command, CancellationToken.None);
}
