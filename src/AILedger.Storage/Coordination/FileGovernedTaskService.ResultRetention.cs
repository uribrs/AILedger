using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
namespace AILedger.Storage;

public sealed partial class FileGovernedTaskService
{
    public async Task<AgentRunResult?> InspectProviderResultAsync(TaskId task, RunId run, CancellationToken token)
    {
        var name = run.Value;
        if (name is "." or ".." || name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            throw new ArgumentException("Unsafe result identity.");
        var folder = Path.Combine(_pathResolver.Resolve(task), "runs");
        var path = Path.Combine(folder, name + ".json");
        if (!File.Exists(path)) return null;
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > 64 * 1024 * 1024)
            throw new InvalidDataException("Invalid retained result path or size.");
        var result = JsonSerializer.Deserialize<AgentRunResult>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false), _eventJson);
        if (result is null || result.RunId != run) throw new InvalidDataException("Retained result identity differs from its run.");
        if (result.Events is null || result.Events.Any(e => e is null || e.RawJson is null))
            throw new InvalidDataException("Retained provider events are unavailable.");
        return result;
    }

    public async Task<string> RetainProviderResultAsync(TaskId task, AgentRunResult result, CancellationToken token)
    {
        var directory = _pathResolver.Resolve(task);
        await using var gate = await _mutationLock.AcquireAsync(Path.Combine(directory, _layout.LockFileName), token).ConfigureAwait(false);
        await CheckCoordinationAsync(directory, token).ConfigureAwait(false);
        var state = await ReplayAsync(task, directory, token).ConfigureAwait(false);
        if (state is null || !state.Runs.TryGetValue(result.RunId, out var run) || run.Provider != result.Provider)
            throw new GovernanceException("Retained result must match its actual admitted run.");
        var name = result.RunId.Value;
        if (name is "." or ".." || name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            throw new ArgumentException("Unsafe result identity.");
        var folder = Path.Combine(directory, "runs");
        Directory.CreateDirectory(folder);
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Result directory is a link.");
        var path = Path.Combine(folder, name + ".json");
        var body = JsonSerializer.Serialize(result, _eventJson);
        if (File.Exists(path))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Result path is a link.");
            var prior = JsonSerializer.Deserialize<AgentRunResult>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false), _eventJson);
            if (JsonSerializer.Serialize(prior, _eventJson) != body) throw new GovernanceException("Retained result already has different content; preserve it for investigation.");
            return path;
        }
        await MarkdownTaskProjectionWriter.WriteAtomicAsync(path, body, token).ConfigureAwait(false);
        return path;
    }
}
