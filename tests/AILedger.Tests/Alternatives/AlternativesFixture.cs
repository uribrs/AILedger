using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Alternatives;
using AILedger.Storage;
using AILedger.Storage.Findings;
using AILedger.Tests.Support;

namespace AILedger.Tests.Alternatives;

internal sealed class AlternativesFixture : IDisposable
{
    private readonly TemporaryDirectory _root = new();
    internal string Root => _root.Path;
    internal TaskId TaskId { get; } = new("alternatives");
    internal ActorId Actor { get; } = new("operator");
    internal string Directory => Path.Combine(Root, TaskId.Value);
    internal string EventsPath => Path.Combine(Directory, "events.jsonl");
    internal AlternativesBinding Binding => new(TaskId, Actor, null, "stable", AllowRecordAlternatives: true, AllowRunless: true);
    internal FileGovernedTaskService Service(int cap = 10000, long bytes = 64 * 1024 * 1024,
        ITaskProjectionWriter? projection = null) => new(Root, new CommandHandler(), new TaskReducer(),
            projection, maximumEventsPerTask: cap, maximumEventLogBytes: bytes);
    internal FileGovernedTaskService Faulted(FindingsStorageFaults faults) =>
        new(Root, new CommandHandler(), new TaskReducer(), faults);
    internal Task OpenAsync() => ExecuteAsync(new OpenTaskCommand(Actor, null, "open", TaskId, "Task", "Goal"));
    internal Task<CommandOutcome> ExecuteAsync(LedgerCommand command) => Service().ExecuteAsync(TaskId, command, default);
    internal async Task<GovernedTaskState> StateAsync() => (await Service().GetStateAsync(TaskId, default))!;
    internal Task<AlternativesResult> RecordAsync(AlternativesRequest? request = null, AlternativesBinding? binding = null) =>
        Service().RecordAlternativesAsync(binding ?? Binding, request ?? Request(), default);
    internal static AlternativesRequest Request(string id = "request-1") => new(1, id,
        [new("a1", "  Approach $HOME | `quoted` \"x\"\nשלום 😀  ", " rationale\nsecond line "),
         new("a2", "Other approach", "Not suitable")]);
    internal static AlternativesRequest Single(string id = "single-1") => new(1, id,
        [new("a", "Approach", "Not suitable")]);
    public void Dispose() => _root.Dispose();
}
