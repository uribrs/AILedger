using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Cli.Routing;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Storage;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Tasks;

internal sealed class TaskInspectionCliCommands(
    CliCommandExecutor executor,
    JsonSerializerOptions json)
{
    private static readonly TimeSpan FollowPollInterval = TimeSpan.FromMilliseconds(250);

    public IEnumerable<CliCommandRegistration> Registrations()
    {
        yield return new CliCommandRegistration(
            ["who"], CliCommandOptions.Set("root", "task", "actor"), isReadOnly: true, WriteWhoAsync);
        yield return new CliCommandRegistration(
            ["status", "task status"], CliCommandOptions.Set("root", "task", "actor"),
            isReadOnly: true, WriteStateAsync);
        yield return new CliCommandRegistration(
            ["history", "task history"],
            CliCommandOptions.Set("root", "task", "actor", "follow", "since"),
            isReadOnly: true, WriteHistoryAsync);
    }

    private async Task WriteWhoAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var state = await CliCommandExecutor.RequireStateAsync(invocation, cancellationToken).ConfigureAwait(false);
        var actors = state.Roles.Values
            .OrderBy(assignment => assignment.Role)
            .ThenBy(assignment => assignment.ActorId.Value, StringComparer.Ordinal)
            .Select(assignment =>
            {
                var live = state.Runs.Values
                    .Where(run => run.ActorId == assignment.ActorId && run.Status == AgentRunStatus.Active)
                    .OrderBy(run => run.Id.Value, StringComparer.Ordinal)
                    .FirstOrDefault();
                return new
                {
                    Actor = assignment.ActorId.Value,
                    Role = assignment.Role.ToString(),
                    Status = live is null ? "idle" : "working",
                    Run = live?.Id.Value,
                    WorkItem = live?.WorkItemId?.Value,
                    CoveredWorkItemIds = live is null ? [] : WorkCoverage.Effective(live.WorkItemId, live.Assurance),
                    Provider = live?.Provider,
                    live?.Model,
                    live?.ProviderVersion,
                    Session = live?.ProviderSessionId,
                    Since = live?.StartedAt
                };
            })
            .ToArray();

        var occupied = state.WorkItems.Values
            .Where(item => item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Stale
                or WorkItemStatus.Abandoned))
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .Select(item => new
            {
                WorkItem = item.Id.Value,
                item.Status,
                Owner = item.Owner?.Value,
                Areas = item.ResourceScope
            })
            .ToArray();

        await executor.WriteJsonAsync(new
        {
            state.TaskId,
            Actors = actors,
            RoleCoverage = RoleCoverage(state),
            OccupiedAreas = occupied
        }).ConfigureAwait(false);
    }

    private async Task WriteStateAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var state = await CliCommandExecutor.RequireStateAsync(invocation, cancellationToken).ConfigureAwait(false);
        var projection = JsonSerializer.SerializeToNode(state, json)
            ?? throw new InvalidDataException("Task state could not be serialized.");
        if (projection["artifacts"] is JsonObject artifacts)
        {
            foreach (var artifact in artifacts.Select(item => item.Value).OfType<JsonObject>())
            {
                artifact.Remove("content");
                var id = artifact["artifactId"]?.GetValue<string>();
                if (id is not null && state.Artifacts.TryGetValue(new ArtifactId(id), out var recorded))
                {
                    artifact["coveredWorkItemIds"] = JsonSerializer.SerializeToNode(
                        WorkCoverage.Effective(recorded.WorkItemId, recorded.Assurance), json);
                    artifact["applicableWorkItemIds"] = JsonSerializer.SerializeToNode(
                        ArtifactApplicability.CurrentMembers(state, recorded), json);
                }
            }
        }

        if (projection["runs"] is JsonObject runs)
        {
            foreach (var entry in runs)
            {
                if (entry.Value is JsonObject run && state.Runs.TryGetValue(new RunId(entry.Key), out var recorded))
                {
                    run["coveredWorkItemIds"] = JsonSerializer.SerializeToNode(
                        WorkCoverage.Effective(recorded.WorkItemId, recorded.Assurance), json);
                }
            }
        }

        var debt = TaskDebt.Compute(state);
        if (!debt.IsClear)
        {
            projection["owed"] = JsonSerializer.SerializeToNode(debt, json);
        }

        var waivers = BriefWaiver.Compute(state);
        if (waivers.Count > 0)
        {
            projection["briefsWaived"] = JsonSerializer.SerializeToNode(waivers, json);
        }

        await executor.WriteJsonAsync(projection).ConfigureAwait(false);
    }

    private async Task WriteHistoryAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var taskId = Task(invocation.Input);
        var cursor = SinceVersion(invocation.Input.Optional("since"));
        var version = await WriteEventsAfterAsync(invocation, taskId, cursor, cancellationToken)
            .ConfigureAwait(false);
        if (version == 0)
        {
            throw new CliUsageException($"Task '{taskId}' was not found.");
        }

        if (!invocation.Input.Flag("follow"))
        {
            return;
        }

        cursor = Math.Max(cursor, version);
        while (true)
        {
            await System.Threading.Tasks.Task.Delay(FollowPollInterval, cancellationToken).ConfigureAwait(false);
            version = await WriteEventsAfterAsync(invocation, taskId, cursor, cancellationToken).ConfigureAwait(false);
            cursor = Math.Max(cursor, version);
        }
    }

    private async Task<long> WriteEventsAfterAsync(
        CliCommandInvocation invocation,
        TaskId taskId,
        long cursor,
        CancellationToken cancellationToken)
    {
        var options = LedgerJson.CreateOptions();
        var version = 0L;
        await foreach (var @event in invocation.Service.GetHistoryAsync(taskId, cancellationToken).ConfigureAwait(false))
        {
            version++;
            if (version > cursor)
            {
                await executor.WriteLineAsync(JsonSerializer.Serialize(@event, options)).ConfigureAwait(false);
            }
        }

        return version;
    }

    private static object[] RoleCoverage(GovernedTaskState state)
    {
        var assigned = state.Roles.Values
            .GroupBy(assignment => assignment.Role)
            .ToDictionary(group => group.Key, group => group
                .Select(assignment => assignment.ActorId.Value)
                .OrderBy(actor => actor, StringComparer.Ordinal)
                .ToArray());
        var engaged = state.Runs.Values
            .Where(run => run.Status == AgentRunStatus.Completed &&
                          !run.HasNoProviderSessionByDeclaration &&
                          run.SubjectRole is not null)
            .GroupBy(run => run.SubjectRole!.Value)
            .ToDictionary(group => group.Key, group => group
                .GroupBy(run => run.ActorId.Value)
                .OrderBy(actor => actor.Key, StringComparer.Ordinal)
                .Select(actor => new RoleEngagement(
                    actor.Key,
                    [.. actor.Select(run => run.Id.Value).OrderBy(id => id, StringComparer.Ordinal)]))
                .ToArray());

        return [.. assigned.Keys
            .Concat(engaged.Keys)
            .Distinct()
            .OrderBy(role => role)
            .Select(role => (object)new
            {
                Role = role.ToString(),
                Assigned = assigned.TryGetValue(role, out var holders) ? holders : Array.Empty<string>(),
                Engaged = engaged.ContainsKey(role),
                EngagedBy = engaged.TryGetValue(role, out var engagements)
                    ? engagements
                    : Array.Empty<RoleEngagement>()
            })];
    }

    private static long SinceVersion(string? value) =>
        value is null ? 0
            : long.TryParse(value, out var parsed) && parsed >= 0
                ? parsed
                : throw new CliUsageException("Since must be a task version of zero or more.");

    private sealed record RoleEngagement(string Actor, string[] Runs);
}
