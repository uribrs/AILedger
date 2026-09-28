using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Findings;
using AILedger.Storage;
using AILedger.Storage.Findings;
using AILedger.Tests.Support;

namespace AILedger.Tests.Findings;

internal sealed class FindingsFixture : IDisposable
{
    private readonly TemporaryDirectory _root = new();
    internal string Root => _root.Path;
    internal TaskId TaskId { get; } = new("findings");
    internal ActorId Actor { get; } = new("operator");
    internal string Directory => Path.Combine(Root, TaskId.Value);
    internal string EventsPath => Path.Combine(Directory, "events.jsonl");
    internal FindingsBinding Binding => new(TaskId, Actor, null, "stable", AllowRecordFindings: true, AllowRunless: true);
    internal FileGovernedTaskService Service(int cap = 10000, long bytes = 64 * 1024 * 1024,
        ITaskProjectionWriter? projection = null) => new(Root, new CommandHandler(), new TaskReducer(),
            projection, maximumEventsPerTask: cap, maximumEventLogBytes: bytes);
    internal FileGovernedTaskService Faulted(FindingsStorageFaults faults) =>
        new(Root, new CommandHandler(), new TaskReducer(), faults);
    internal Task OpenAsync() => ExecuteAsync(new OpenTaskCommand(Actor, null, "open", TaskId, "Task", "Goal"));
    internal Task<CommandOutcome> ExecuteAsync(LedgerCommand command) => Service().ExecuteAsync(TaskId, command, default);
    internal async Task<GovernedTaskState> StateAsync() => (await Service().GetStateAsync(TaskId, default))!;
    internal Task<FindingsResult> RecordAsync(FindingsRequest? request = null, FindingsBinding? binding = null) =>
        Service().RecordAsync(binding ?? Binding, request ?? Request(), default);
    internal static FindingsRequest Request(string id = "request-1") => new(1, id,
        [new("f1", "  Claim $HOME | `quoted` \"x\"\nשלום 😀  ", " consequence ")],
        [new("e1", " test ", " fixture://source ", " summary\nsecond line ", [new(Finding: "f1")], [])]);
    internal static FindingsRequest Claims(string id = "claim-1") => new(1, id, [new("f", "Statement")], []);
    public void Dispose() => _root.Dispose();
}
