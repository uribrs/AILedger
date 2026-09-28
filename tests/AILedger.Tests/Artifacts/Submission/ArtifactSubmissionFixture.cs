using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Storage.Findings;
using AILedger.Tests.Support;

namespace AILedger.Tests.Artifacts.Submission;

internal sealed class ArtifactSubmissionFixture : IDisposable
{
    private readonly TemporaryDirectory _root = new();
    internal string Root => _root.Path;
    internal TaskId TaskId { get; } = new("submission");
    internal ActorId Actor { get; } = new("verifier");
    internal string Directory => Path.Combine(Root, TaskId.Value);
    internal string EventsPath => Path.Combine(Directory, "events.jsonl");
    internal ArtifactSubmissionBinding Binding => new(TaskId, Actor, new RunId("RV"), "RV", AllowSubmitArtifact: true);
    internal FileGovernedTaskService Service(int cap = 10000, long bytes = 64 * 1024 * 1024) =>
        new(Root, new CommandHandler(), new TaskReducer(), maximumEventsPerTask: cap, maximumEventLogBytes: bytes);
    internal FileGovernedTaskService Faulted(FindingsStorageFaults faults) => new(Root, new CommandHandler(), new TaskReducer(), faults);
    internal async Task OpenAsync(string provider = "verification-provider")
    {
        // Historical fixture events are produced by the established staging helper, then all
        // submissions, replays and refusals use the production handler and service.
        var task = new TestTask(TaskId.Value);
        ArtifactCommands.AddWork(task, new WorkItemId("W1"), "submission-fixture");
        task.RecordExecutionArtifacts();
        task.Assign(Actor, RoleKind.Verifier, Capability.BuildContext, Capability.RecordArtifact);
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), new RunId("RV"),
            new WorkItemId("W1"), provider, null, SubjectActorId: Actor));
        foreach (var stage in new[] { TaskStage.Research, TaskStage.Design, TaskStage.Scope, TaskStage.Ready, TaskStage.Execution, TaskStage.Verification })
            task.Apply(new RequestStageTransitionCommand(task.OperatorId, null, task.NextCorrelation(), stage,
                WithoutPrerequisitesReason: "Disposable submission fixture stage arrangement"));
        System.IO.Directory.CreateDirectory(Directory);
        await File.WriteAllTextAsync(EventsPath, string.Concat(task.Events.Select(e => JsonSerializer.Serialize(e, LedgerJson.CreateOptions()) + "\n")));
        _ = await StateAsync();
    }
    internal Task<CommandOutcome> ExecuteAsync(LedgerCommand command) => Service().ExecuteAsync(TaskId, command, default);
    internal async Task<GovernedTaskState> StateAsync() => (await Service().GetStateAsync(TaskId, default))!;
    internal Task<ArtifactSubmissionResult> SubmitAsync(ArtifactSubmissionRequest? request = null,
        ArtifactSubmissionBinding? binding = null) => Service().SubmitArtifactAsync(binding ?? Binding, request ?? Request(), default);
    internal static ArtifactSubmissionRequest Request(string id = "output-1") => new(1, id,
        GovernedArtifactKind.VerifierOutput, "  Verification output  ",
        ArtifactCommands.VerifierBody + "\n  Literal $HOME | `quoted` \"x\"\r\nשלום 😀  ");
    internal async Task<List<LedgerEvent>> HistoryAsync()
    {
        var events = new List<LedgerEvent>();
        await foreach (var e in Service().GetHistoryAsync(TaskId, default)) events.Add(e);
        return events;
    }
    public void Dispose() => _root.Dispose();
}
