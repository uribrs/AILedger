using System.Text.Json;
using AILedger.Core.Authority;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
namespace AILedger.Storage;

public sealed record CoordinationSnapshot(int SchemaVersion, long Revision, long Epoch, string Owner,
    DateTimeOffset ExpiresAt, string? Payload)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public GovernedAssuranceAnchor? AssuranceAnchor { get; init; }
}

public sealed partial class FileGovernedTaskService
{
    private readonly TimeProvider _coordinationTimeProvider;
    private CoordinationLease? _coordination;
    private AgentSessionAuthority? _agentAuthority;
    private const string CoordinationFile = "coordination-v1.json";

    public FileGovernedTaskService BindCoordination(CoordinationLease lease)
    {
        var service = CopyService(_commandHandler);
        service._coordination = lease;
        return service;
    }

    private FileGovernedTaskService CopyService(ICommandHandler handler) =>
        new(_pathResolver.WorkspaceRoot, handler, _reducer, _projectionWriter,
            _layout, _maximumEventsPerTask, _maximumEventLogBytes, _lessonStore, _coordinationTimeProvider)
        { _coordination = _coordination, _agentAuthority = _agentAuthority, _completionAdmission = _completionAdmission };

    public async Task<CoordinationLease> AcquireCoordinationAsync(TaskId task, string owner,
        TimeSpan lifetime, CancellationToken token)
    {
        ValidateLifetime(lifetime);
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > 128) throw new ArgumentException("Invalid owner identity.");
        var directory = _pathResolver.Resolve(task);
        _pathResolver.EnsureTaskDirectory(directory);
        await using var gate = await _mutationLock.AcquireAsync(Path.Combine(directory, _layout.LockFileName), token).ConfigureAwait(false);
        if (!File.Exists(Path.Combine(directory, _layout.EventsFileName))) throw new InvalidDataException("Task is unavailable.");
        var old = await ReadCoordinationAsync(directory, token).ConfigureAwait(false);
        var now = _coordinationTimeProvider.GetUtcNow();
        if (old is not null && old.ExpiresAt > now) throw new GovernanceException("Task already has a routing owner.");
        var next = new CoordinationSnapshot(1, checked((old?.Revision ?? 0) + 1), checked((old?.Epoch ?? 0) + 1), owner, now + lifetime, old?.Payload) { AssuranceAnchor = old?.AssuranceAnchor };
        await WriteCoordinationAsync(directory, next, token).ConfigureAwait(false);
        return new(task, owner, next.Epoch);
    }

    public async Task<CoordinationSnapshot> ReadCoordinationAsync(CoordinationLease lease, CancellationToken token) =>
        await ChangeCoordinationAsync(lease, null, null, null, token).ConfigureAwait(false);

    // Diagnostic snapshot only. Does not acquire an owner, renew a lease or confer authority.
    public Task<CoordinationSnapshot?> InspectCoordinationAsync(TaskId task, CancellationToken token) =>
        ReadCoordinationAsync(_pathResolver.Resolve(task), token);

    public Task<CoordinationSnapshot> RenewCoordinationAsync(CoordinationLease lease, TimeSpan lifetime, CancellationToken token)
    {
        ValidateLifetime(lifetime);
        return ChangeCoordinationAsync(lease, lifetime, null, null, token);
    }

    public Task<CoordinationSnapshot> SaveCoordinationAsync(CoordinationLease lease, long revision, string payload, CancellationToken token)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(payload) > 4 * 1024 * 1024) throw new GovernanceException("Coordination journal capacity exceeded; work remains unresolved.");
        return ChangeCoordinationAsync(lease, null, revision, payload, token);
    }

    public async Task ReleaseCoordinationAsync(CoordinationLease lease, CancellationToken token) =>
        _ = await ChangeCoordinationAsync(lease, TimeSpan.Zero, null, null, token).ConfigureAwait(false);

    private async Task<CoordinationSnapshot> ChangeCoordinationAsync(CoordinationLease lease, TimeSpan? lifetime,
        long? revision, string? payload, CancellationToken token)
    {
        var directory = _pathResolver.Resolve(lease.TaskId);
        await using var gate = await _mutationLock.AcquireAsync(Path.Combine(directory, _layout.LockFileName), token).ConfigureAwait(false);
        var current = await RequireOwnerAsync(directory, lease, token).ConfigureAwait(false);
        if (revision is { } expected && expected != current.Revision) throw new GovernanceException("Coordination revision changed; reobserve before writing.");
        if (lifetime is null && payload is null) return current;
        var next = current with { Revision = checked(current.Revision + 1), Payload = payload ?? current.Payload,
            ExpiresAt = lifetime is { } duration ? _coordinationTimeProvider.GetUtcNow() + duration : current.ExpiresAt };
        await WriteCoordinationAsync(directory, next, token).ConfigureAwait(false);
        return next;
    }

    private async Task CheckCoordinationAsync(string directory, CancellationToken token)
    {
        if (_coordination is { } lease)
        {
            if (!string.Equals(directory, _pathResolver.Resolve(lease.TaskId), StringComparison.Ordinal))
                throw new GovernanceException("Routing owner is bound to a different task.");
            await RequireOwnerAsync(directory, lease, token).ConfigureAwait(false);
        }
    }

    private async Task<CoordinationSnapshot> RequireOwnerAsync(string directory, CoordinationLease lease, CancellationToken token)
    {
        var current = await ReadCoordinationAsync(directory, token).ConfigureAwait(false);
        if (current is null || current.Owner != lease.Owner || current.Epoch != lease.Epoch || current.ExpiresAt <= _coordinationTimeProvider.GetUtcNow())
            throw new GovernanceException("Routing ownership expired or was fenced by a later epoch.");
        return current;
    }

    private async Task<CoordinationSnapshot?> ReadCoordinationAsync(string directory, CancellationToken token)
    {
        var path = Path.Combine(directory, CoordinationFile);
        if (!File.Exists(path)) return null;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > 5 * 1024 * 1024)
            throw new InvalidDataException("Invalid coordination journal path or size.");
        var value = JsonSerializer.Deserialize<CoordinationSnapshot>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false), _eventJson);
        if (value is null || value.SchemaVersion != 1 || value.Epoch < 1 || value.Revision < 1 || string.IsNullOrWhiteSpace(value.Owner))
            throw new InvalidDataException("Unsupported or corrupt coordination journal; preserve it for recovery.");
        return value;
    }

    private async Task WriteCoordinationAsync(string directory, CoordinationSnapshot value, CancellationToken token)
    {
        var target = Path.Combine(directory, CoordinationFile);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporary, options))
            {
                await JsonSerializer.SerializeAsync(stream, value, _eventJson, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void ValidateLifetime(TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromHours(24)) throw new ArgumentOutOfRangeException(nameof(lifetime));
    }
}
