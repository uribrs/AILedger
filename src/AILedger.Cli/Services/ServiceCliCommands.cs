using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AILedger.Cli.Routing;
using AILedger.Cli.Verification;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Services;

internal sealed class ServiceCliCommands(CliCommandExecutor executor, Func<VerificationHost> host)
{
    public IEnumerable<CliCommandRegistration> Registrations()
    {
        yield return new(["service start"], CliCommandOptions.Set("root", "task", "actor", "id", "checkout", "profile", "confirm"), false,
            (invocation, token) => WithDiagnosticsAsync(() => StartAsync(invocation, token), token));
        yield return new(["service inspect"], CliCommandOptions.Set("root", "task", "actor", "id"), true,
            (invocation, token) => WithDiagnosticsAsync(() => InspectAsync(invocation, token), token));
        yield return new(["service stop"], CliCommandOptions.Set("root", "task", "actor", "id"), false,
            (invocation, token) => WithDiagnosticsAsync(() => StopAsync(invocation, token), token));
    }

    private async Task StartAsync(CliCommandInvocation invocation, CancellationToken token)
    {
        var state = await RequireOperatorAsync(invocation, token).ConfigureAwait(false);
        if (state.Stage == TaskStage.Archive)
            throw new GovernanceException("Cannot start a service for an archived task.");
        var input = invocation.Input;
        var id = SafeId(input.Required("id"));
        var directory = DirectoryFor(invocation, id);
        if (Directory.Exists(directory) || state.Evidence.ContainsKey(StartedEvidence(id)))
            throw new GovernanceException($"Service '{id}' already has a retained attempt. Inspect/stop it; use a new id for a new build.");
        var runtime = host();
        var dockerHost = runtime.DockerHost();
        using var engine = await ConnectAsync(runtime, dockerHost, token).ConfigureAwait(false);
        var plan = await PlanAsync(invocation, id, dockerHost, engine, token).ConfigureAwait(false);
        var confirmation = ServiceRecord.Confirmation(plan);
        if (input.Optional("confirm") is not { } confirmed)
        {
            await executor.WriteJsonAsync(new { Plan = plan, Confirmation = confirmation,
                Notice = "Read this plan, then repeat with --confirm. Image/source/profile/engine changes invalidate it. No container was created. Source observation does not establish image build provenance." }).ConfigureAwait(false);
            return;
        }
        if (!string.Equals(confirmed, confirmation, StringComparison.Ordinal))
            throw new GovernanceException("Service confirmation differs from the current image/source/profile/engine plan; preview again. Nothing was started.");

        Directory.CreateDirectory(directory);
        // Exclusive and never reused, including after a crash. No container exists before this marker.
        await using (var reservation = new FileStream(Path.Combine(directory, ".reserved"), FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        using var operation = Lock(directory);
        var record = new ServiceRecord(plan, "starting", runtime.Clock.GetUtcNow());
        await record.SaveAsync(directory, token).ConfigureAwait(false);
        try
        {
            var container = await engine.CreateAsync(plan, token).ConfigureAwait(false);
            record = record with { ContainerId = container };
            await record.SaveAsync(directory, token).ConfigureAwait(false);
            await engine.StartAsync(container, token).ConfigureAwait(false);
            using var inspection = await engine.InspectAsync(record, token).ConfigureAwait(false)
                ?? throw new GovernanceException("Service disappeared after creation.");
            var endpoint = ServiceEngine.Endpoint(inspection.RootElement, plan);
            await WaitReadyAsync(runtime, endpoint, plan.Profile, token).ConfigureAwait(false);
            // Recheck after readiness; a responding unrelated host listener is not sufficient.
            using var ready = await engine.InspectAsync(record, token).ConfigureAwait(false)
                ?? throw new GovernanceException("Service disappeared during readiness.");
            if (ServiceEngine.Endpoint(ready.RootElement, plan) != endpoint)
                throw new GovernanceException("Service endpoint changed during readiness.");
            record = record with { Status = "ready", Endpoint = endpoint, ObservedAt = runtime.Clock.GetUtcNow() };
            await record.SaveAsync(directory, token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Even an uncertain create response can be reconciled through the reserved name and owner label.
            var cleanup = await CleanupAsync(engine, record, directory).ConfigureAwait(false);
            record = record with { Status = "failed", Endpoint = null, ObservedAt = runtime.Clock.GetUtcNow(),
                Error = exception.Message + (cleanup is null ? "" : $"; cleanup unresolved: {cleanup}") };
            record = await WithLogReceiptAsync(record, directory).ConfigureAwait(false);
            await record.SaveAsync(directory, CancellationToken.None).ConfigureAwait(false);
            await RecordAsync(invocation, record, directory, "start", StartedEvidence(id)).ConfigureAwait(false);
            throw new GovernanceException($"Service start failed: {record.Error}. Retained at '{directory}'; use service stop to reconcile cleanup.");
        }
        await RecordAsync(invocation, record, directory, "start", StartedEvidence(id)).ConfigureAwait(false);
        await executor.WriteJsonAsync(record).ConfigureAwait(false);
    }

    private async Task InspectAsync(CliCommandInvocation invocation, CancellationToken token)
    {
        await RequireOperatorAsync(invocation, token).ConfigureAwait(false);
        var directory = DirectoryFor(invocation, SafeId(invocation.Input.Required("id")));
        var record = await ServiceRecord.ReadAsync(directory, token).ConfigureAwait(false);
        using var engine = await ConnectAsync(host(), record.Plan.DockerHost, token).ConfigureAwait(false);
        using var inspection = await engine.InspectAsync(record, token).ConfigureAwait(false);
        string? endpoint = null;
        string? problem = null;
        if (inspection is not null)
        {
            try { endpoint = ServiceEngine.Endpoint(inspection.RootElement, record.Plan); }
            catch (GovernanceException exception) { problem = exception.Message; }
        }
        await executor.WriteJsonAsync(new { Record = record, Present = inspection is not null, Endpoint = endpoint,
            Problem = problem, Notice = "Current container observation; HTTP readiness was checked at start, not continuously." }).ConfigureAwait(false);
    }

    private async Task StopAsync(CliCommandInvocation invocation, CancellationToken token)
    {
        var state = await RequireOperatorAsync(invocation, token).ConfigureAwait(false);
        var id = SafeId(invocation.Input.Required("id"));
        var directory = DirectoryFor(invocation, id);
        using var operation = Lock(directory);
        var record = await ServiceRecord.ReadAsync(directory, token).ConfigureAwait(false);
        var evidence = new EvidenceId($"service-{id}-stop");
        if (record.Status == "stopped" && state.Evidence.ContainsKey(evidence))
        {
            await RecordAsync(invocation, record, directory, "stop", evidence).ConfigureAwait(false);
            await executor.WriteJsonAsync(record).ConfigureAwait(false);
            return;
        }
        using var engine = await ConnectAsync(host(), record.Plan.DockerHost, token).ConfigureAwait(false);
        var failure = await CleanupAsync(engine, record, directory).ConfigureAwait(false);
        record = record with { Status = failure is null ? "stopped" : "cleanup-failed", Endpoint = null,
            ObservedAt = host().Clock.GetUtcNow(), Error = failure };
        record = await WithLogReceiptAsync(record, directory).ConfigureAwait(false);
        await record.SaveAsync(directory, CancellationToken.None).ConfigureAwait(false);
        if (failure is not null) throw new GovernanceException($"Service cleanup unresolved: {failure}. Retry service stop; receipt retained at '{directory}'.");
        await RecordAsync(invocation, record, directory, "stop", evidence).ConfigureAwait(false);
        await executor.WriteJsonAsync(record).ConfigureAwait(false);
    }

    private static async Task<ServicePlan> PlanAsync(CliCommandInvocation invocation, string id,
        string dockerHost, ServiceEngine engine, CancellationToken token)
    {
        var checkout = Encoding.UTF8.GetString(await VerificationCliCommands.GitAsync(
            Path.GetFullPath(invocation.Input.Required("checkout")), ["rev-parse", "--show-toplevel"], token).ConfigureAwait(false)).Trim();
        var profileName = invocation.Input.Required("profile");
        var (profile, profileHash) = await ServiceProfiles.ReadAsync(checkout, profileName, token).ConfigureAwait(false);
        var image = await engine.ImageAsync(profile.Image, token).ConfigureAwait(false);
        var head = Encoding.UTF8.GetString(await VerificationCliCommands.GitAsync(checkout, ["rev-parse", "HEAD"], token).ConfigureAwait(false)).Trim();
        var worktree = await WorktreeFingerprint.ComputeAsync(checkout, token).ConfigureAwait(false);
        var owner = ServiceRecord.Hash(Path.GetFullPath(DirectoryFor(invocation, id)));
        return new(Task(invocation.Input).Value, id, checkout, head, worktree, profileName, profileHash,
            profile, image, dockerHost, owner, $"ailedger-service-{owner[..32]}");
    }

    private static async Task<ServiceEngine> ConnectAsync(VerificationHost runtime, string dockerHost, CancellationToken token)
    {
        var failure = await DockerSocketPreflight.CheckAsync(dockerHost, runtime.CreateEngineHandler, token).ConfigureAwait(false);
        if (failure is not null) throw new GovernanceException($"service: {failure}");
        return new ServiceEngine(runtime.CreateEngineHandler(UnixSocketEngineHandler.SocketPath(dockerHost)!));
    }

    private static async Task WaitReadyAsync(VerificationHost runtime, string endpoint, ServiceProfile profile, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(profile.StartupSeconds));
        using var client = new HttpClient(runtime.CreateServiceProbeHandler()) { Timeout = TimeSpan.FromSeconds(2) };
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            try
            {
                using var response = await client.GetAsync(endpoint + profile.ReadinessPath,
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if ((int)response.StatusCode == profile.ReadinessStatus) return;
            }
            catch (Exception exception) when ((exception is HttpRequestException or TaskCanceledException) && !timeout.IsCancellationRequested) { }
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token).ConfigureAwait(false);
        }
    }

    private static async Task<string?> CleanupAsync(ServiceEngine engine, ServiceRecord record, string directory)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            using var container = await engine.InspectAsync(record, timeout.Token).ConfigureAwait(false);
            if (container is null) return null;
            var id = container.RootElement.GetProperty("Id").GetString()!;
            string? logFailure = null;
            try { await engine.RetainLogsAsync(id, Path.Combine(directory, "container.log"), timeout.Token).ConfigureAwait(false); }
            catch (Exception exception) { logFailure = exception.Message; }
            await engine.RemoveAsync(id, timeout.Token).ConfigureAwait(false);
            if (logFailure is not null)
                await File.WriteAllTextAsync(Path.Combine(directory, "log-retention-error.txt"), logFailure, CancellationToken.None).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception) { return exception.Message; }
    }

    private static async Task RecordAsync(CliCommandInvocation invocation, ServiceRecord record,
        string directory, string phase, EvidenceId evidence)
    {
        var path = Path.Combine(directory, $"{phase}.json");
        byte[] bytes;
        if (File.Exists(path)) bytes = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false);
        else
        {
            bytes = JsonSerializer.SerializeToUtf8Bytes(record, ServiceProfiles.Json);
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 8192, true);
            await file.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
        }
        var state = await invocation.Service.GetStateAsync(Task(invocation.Input), CancellationToken.None).ConfigureAwait(false);
        var citation = $"services/{record.Plan.Id}/{phase}.json sha256:{ServiceRecord.Hash(bytes)}";
        if (state!.Evidence.TryGetValue(evidence, out var existing))
        {
            if (existing.SourceType != "host-service" || existing.Citation != citation)
                throw new GovernanceException($"Service evidence id '{evidence}' belongs to another record; retained observation at '{path}'.");
            return;
        }
        record = JsonSerializer.Deserialize<ServiceRecord>(bytes, ServiceProfiles.Json)!;
        await invocation.Service.ExecuteAsync(Task(invocation.Input), new AddEvidenceCommand(
            Actor(invocation.Input), null, $"service-{record.Plan.Id}-{phase}", evidence, "host-service",
            citation,
            $"Service {record.Plan.Id}: {record.Status}; profile {record.Plan.ProfileName}; endpoint {record.Endpoint ?? "none"}; image {record.Plan.ImageId}; source observation {record.Plan.Worktree}. Source observation is not image build provenance.", [], []),
            CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task<ServiceRecord> WithLogReceiptAsync(ServiceRecord record, string directory)
    {
        var logs = Path.Combine(directory, "container.log");
        var error = Path.Combine(directory, "log-retention-error.txt");
        return record with
        {
            LogSha256 = File.Exists(logs) ? ServiceRecord.Hash(await File.ReadAllBytesAsync(logs, CancellationToken.None).ConfigureAwait(false)) : null,
            LogError = File.Exists(error) ? await File.ReadAllTextAsync(error, CancellationToken.None).ConfigureAwait(false) : null
        };
    }

    private static async Task WithDiagnosticsAsync(Func<Task> operation, CancellationToken token)
    {
        try { await operation().ConfigureAwait(false); }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException ||
                                          exception is TaskCanceledException && !token.IsCancellationRequested)
        {
            throw new GovernanceException($"Service host/receipt could not be read: {exception.Message}. Inspect the retained service before retrying.");
        }
    }

    private static async Task<GovernedTaskState> RequireOperatorAsync(CliCommandInvocation invocation, CancellationToken token)
    {
        var state = await CliCommandExecutor.RequireStateAsync(invocation, token).ConfigureAwait(false);
        if (!state.Roles.TryGetValue(Actor(invocation.Input), out var actor) || actor.Role != RoleKind.Operator)
            throw new GovernanceException("service lifecycle requires an operator on the host; workers may only use the supplied endpoint.");
        return state;
    }

    internal static EvidenceId StartedEvidence(string id) => new($"service-{id}-start");
    private static string DirectoryFor(CliCommandInvocation invocation, string id) =>
        Path.Combine(new TaskWorkspacePathResolver(invocation.LedgerRoot).Resolve(Task(invocation.Input)), "services", id);
    private static string SafeId(string value) => Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$")
        ? value : throw new CliUsageException("Service id must be 1..64 ASCII letters, digits, underscores or hyphens, starting with a letter or digit.");
    private static FileStream Lock(string directory) => new(Path.Combine(directory, ".operation"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
}
