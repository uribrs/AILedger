using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.ClaimDispositions;
using AILedger.Storage;
using AILedger.Storage.Findings;
using AILedger.Tests.Support;

namespace AILedger.Tests.ClaimDispositions;

internal sealed class ClaimDispositionsFixture : IDisposable
{
    private readonly TemporaryDirectory _root = new();
    internal string Root => _root.Path;
    internal TaskId TaskId { get; } = new("dispositions");
    internal ActorId Actor { get; } = new("operator");
    internal string Directory => Path.Combine(Root, TaskId.Value);
    internal string EventsPath => Path.Combine(Directory, "events.jsonl");
    internal const int InitialVersion = 7;
    internal ClaimDispositionsBinding Binding => new(TaskId, Actor, null, "stable", AllowRecordClaimDispositions: true, AllowRunless: true);
    internal FileGovernedTaskService Service(int cap = 10000, long bytes = 64 * 1024 * 1024) =>
        new(Root, new CommandHandler(), new TaskReducer(), maximumEventsPerTask: cap, maximumEventLogBytes: bytes);
    internal FileGovernedTaskService Faulted(FindingsStorageFaults faults) => new(Root, new CommandHandler(), new TaskReducer(), faults);
    internal async Task OpenAsync()
    {
        await ExecuteAsync(new OpenTaskCommand(Actor, null, "open", TaskId, "Task", "Goal"));
        foreach (var id in new[] { "C1", "C2" })
            await ExecuteAsync(new AddClaimCommand(Actor, null, "claim", new(id), "Claim " + id, "Consequence"));
        await ExecuteAsync(new AddEvidenceCommand(Actor, null, "evidence", new("E1"), "test-run", "source", "Supports C1", [new("C1")], []));
        await ExecuteAsync(new AddEvidenceCommand(Actor, null, "evidence", new("E2"), "test-run", "source", "Refutes C2", [], [new("C2")]));
        await ExecuteAsync(new AddEvidenceCommand(Actor, null, "evidence", new("E3"), "test-run", "source", "Refutes C1", [], [new("C1")]));
    }
    internal Task<CommandOutcome> ExecuteAsync(LedgerCommand command) => Service().ExecuteAsync(TaskId, command, default);
    internal async Task<GovernedTaskState> StateAsync() => (await Service().GetStateAsync(TaskId, default))!;
    internal Task<ClaimDispositionsResult> RecordAsync(ClaimDispositionsRequest? request = null, ClaimDispositionsBinding? binding = null) =>
        Service().RecordClaimDispositionsAsync(binding ?? Binding, request ?? Request(), default);
    internal static ClaimDispositionsRequest Request(string id = "request-1") => new(1, id,
        [new("j1", new("C1"), ClaimStatus.Open, ClaimStatus.Validated, "  Rationale $HOME | `quoted` \"x\"\nשלום 😀  ", [new("E1")]),
         new("j2", new("C2"), ClaimStatus.Open, ClaimStatus.Rejected, "Refuting observation", [new("E2")])]);
    internal static ClaimDispositionsRequest Single(string id = "single-1") => Request(id) with { Dispositions = [Request().Dispositions[0]] };
    internal async Task<List<LedgerEvent>> HistoryAsync()
    {
        var history = new List<LedgerEvent>();
        await foreach (var e in Service().GetHistoryAsync(TaskId, default)) history.Add(e);
        return history;
    }
    public void Dispose() => _root.Dispose();
}
