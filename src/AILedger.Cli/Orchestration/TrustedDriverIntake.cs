using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Storage;
namespace AILedger.Cli.Orchestration;

// Trusted OS-host API only. No provider or coordinator proposal endpoint exposes this composition.
public static class TrustedDriverIntake
{
    public static async Task CreateAsync(FileGovernedTaskService host, TaskId task, ActorId human,
        string requestId, string title, string originalRequest, IReadOnlyList<string> constraints,
        IReadOnlyList<string> tags, CancellationToken token)
    {
        var body = JsonSerializer.Serialize(new { title, originalRequest, constraints, tags });
        var binding = $"trusted-intake/{task.Value}/{human.Value}";
        await host.ExecuteAsync(task, new OpenTaskCommand(human, null, requestId, task, title, originalRequest, Tags: tags)
        { HostRequest = new(1, binding, requestId + "/open", body) }, token).ConfigureAwait(false);
        await host.ExecuteAsync(task, new RecordArtifactCommand(human, null, requestId, new("trusted-request"),
            GovernedArtifactKind.UserRequest, title, originalRequest, null, null, null)
        { HostRequest = new(1, binding, requestId + "/request", body) }, token).ConfigureAwait(false);
        for (var index = 0; index < constraints.Count; index++)
            await host.ExecuteAsync(task, new AddConstraintCommand(human, null, requestId, new("intake-" + index),
                constraints[index], "Trusted original user intake: " + requestId, [])
            { HostRequest = new(1, binding, requestId + "/constraint/" + index, body) }, token).ConfigureAwait(false);
    }
}
