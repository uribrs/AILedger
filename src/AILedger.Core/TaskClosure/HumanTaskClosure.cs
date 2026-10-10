using System.Text.Json.Serialization;
using AILedger.Core.Contracts;

namespace AILedger.Core.TaskClosure;

public sealed record HumanTaskClosure(
    ActorId ActorId, string Reason, TaskStage PreviousStage, long BasisVersion, long ReviewedVersion,
    DateTimeOffset ConfirmedAt, string AuthenticationMethod, string HumanIdentity,
    IReadOnlyList<RunId> InterruptedRuns, IReadOnlyList<WorkItemId> ExcludedWorkItems)
{
    public string Outcome => "succeeded_by_human_override";
}

public sealed record CloseTaskByHumanCommand(ActorId ActorId, EventId? CausationId,
    string CorrelationId, string Reason) : LedgerCommand(ActorId, CausationId, CorrelationId)
{
    // Host-only, transient authority. JSON, an operator role, and agent grants cannot mint it.
    [JsonIgnore]
    internal HumanClosureAdmission? Admission { get; init; }
}

public sealed record TaskClosedByHuman(HumanTaskClosure Closure) : LedgerEventData;

internal sealed record HumanClosureAdmission(TaskId TaskId, ActorId ActorId, long Version,
    string Reason, DateTimeOffset ConfirmedAt, string AuthenticationMethod, string HumanIdentity,
    Provenance OperatorAssignment);
