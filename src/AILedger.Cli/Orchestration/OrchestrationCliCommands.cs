using System.Text.Json;
using AILedger.Cli.Dispatch;
using AILedger.Cli.Findings;
using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using AILedger.Storage;
using static AILedger.Cli.Routing.CliInput;
namespace AILedger.Cli.Orchestration;

internal sealed class OrchestrationCliCommands(CliCommandExecutor output, Func<string, IAgentAdapter> adapters, IContextAssembler context)
{
    internal IEnumerable<CliCommandRegistration> Registrations()
    {
        yield return new(["orchestrate coverage"], CliCommandOptions.Set("root"), true,
            (_, _) => output.WriteJsonAsync(new { version = OrchestrationCoverage.Version, fixtureOnly = true, deployment = "macos-confined-cognitive-coordinator-v1", acceptanceBridge = "bounded acceptance, repair-impact review reuse, required-check ordering and completion recovery; fixture validation only", stages = OrchestrationCoverage.Stages, cognitive = DispatchHostHandoffs.Support }));
        yield return new(["orchestrate reconcile-stopped"], CliCommandOptions.Set("root", "task", "actor", "run", "termination-evidence"), false, ReconcileStoppedAsync);
        yield return new(["orchestrate intake"], CliCommandOptions.Set("root", "task", "actor", "request-file"), false, IntakeAsync);
        yield return new(["orchestrate run"], CliCommandOptions.Set("root", "task", "actor", "work", "profiles", "working-directory",
            "cognitive-root", "executable", "assurance-authority", "assurance-store", "serial-justification", "timeout-seconds", "owner-lease-seconds", "acceptance-principal"), false, RunAsync);
    }
    private async Task RunAsync(CliCommandInvocation invocation, CancellationToken token)
    {
        if (invocation.Service is not FileGovernedTaskService host)
            throw new CliUsageException("Orchestration requires trusted file-host composition with restricted routine authority.");
        var input = invocation.Input;
        var profilePath = Path.GetFullPath(input.Required("profiles"));
        (IReadOnlyDictionary<CognitiveWorkKind, DriverAgentProfile> Agents, DriverPreparationPolicy? Preparation) profiles;
        try { profiles = await ReadProfilesAsync(profilePath, token).ConfigureAwait(false); }
        catch (JsonException error) { throw new CliUsageException("Invalid driver profiles: " + error.Message); }
        var timeoutSeconds = 1800;
        if (input.Optional("timeout-seconds") is { } timeout && (!int.TryParse(timeout, out timeoutSeconds) || timeoutSeconds <= 0))
            throw new CliUsageException("Timeout must be a positive integer.");
        var ownerSeconds = 120;
        if (input.Optional("owner-lease-seconds") is { } owner && (!int.TryParse(owner, out ownerSeconds) || ownerSeconds is < 1 or > 3600))
            throw new CliUsageException("Owner lease must be between 1 and 3600 seconds.");
        var options = new DriverHostOptions(Task(input), Actor(input), Path.GetFullPath(input.Required("working-directory")),
            new(invocation.LedgerRoot, invocation.LessonRoot)
            {
                CognitiveRoot = input.Optional("cognitive-root"), Executable = input.Optional("executable"),
                AssuranceAuthority = input.Optional("assurance-authority"), AssuranceStore = input.Optional("assurance-store"),
                ProtectedPaths = [profilePath]
            }, profiles.Agents)
        { TimeoutSeconds = timeoutSeconds, OwnerLifetime = TimeSpan.FromSeconds(ownerSeconds), Preparation = profiles.Preparation, AcceptancePrincipal = input.Optional("acceptance-principal") };
        var driver = await OrchestrationHost.CreateAsync(host, options, adapters, context, token).ConfigureAwait(false);
        var result = await driver.DriveAsync(new(input.Optional("work") is { } work ? new WorkItemId(work) : null,
            input.Optional("serial-justification") is { } serial ? new AlternativeId(serial) : null), token).ConfigureAwait(false);
        await output.WriteJsonAsync(result).ConfigureAwait(false);
        // A successful transport is not a completed task. Nonterminal outcomes have a distinct exit.
        if (result.Status != DriverStatus.Archived) throw new OrchestrationStoppedException(result.Status);
    }
    private static async Task<(IReadOnlyDictionary<CognitiveWorkKind, DriverAgentProfile> Agents, DriverPreparationPolicy? Preparation)> ReadProfilesAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var buffer = new byte[16385];
        var length = await stream.ReadAtLeastAsync(buffer, buffer.Length, false, token).ConfigureAwait(false);
        if (length == buffer.Length) throw new CliUsageException("Profiles exceed 16 KiB.");
        using var document = StrictJson.Parse(buffer.AsMemory(0, length));
        StrictJson.Validate(document.RootElement);
        StrictJson.Members(document.RootElement, ["schema_version", "agents"], ["preparation"]);
        if (!FindingsRequestParser.IsVersionOne(document.RootElement.GetProperty("schema_version")))
            throw new CliUsageException("Unsupported driver profile schema.");
        var profiles = new Dictionary<CognitiveWorkKind, DriverAgentProfile>();
        foreach (var row in FindingsRequestParser.Array(document.RootElement.GetProperty("agents"), 16))
        {
            StrictJson.Members(row, ["work", "subject", "provider"], ["model"]);
            var name = StrictJson.Text(row, "work");
            var matches = Enum.GetValues<CognitiveWorkKind>().Where(k => JsonNamingPolicy.SnakeCaseLower.ConvertName(k.ToString()) == name).ToArray();
            if (matches.Length != 1 || !profiles.TryAdd(matches[0], new(new(StrictJson.Text(row, "subject")),
                StrictJson.Text(row, "provider"), FindingsRequestParser.OptionalText(row, "model"))))
                throw new CliUsageException("Unknown or duplicate cognitive profile.");
        }
        var preparation = document.RootElement.TryGetProperty("preparation", out var policy) ? ReadPreparation(policy) : null;
        return (profiles, preparation);
    }
    private static DriverPreparationPolicy ReadPreparation(JsonElement policy)
    {
        StrictJson.Members(policy, ["roles", "work"]);
        var roles = FindingsRequestParser.Array(policy.GetProperty("roles"), 16).Select(row =>
        {
            StrictJson.Members(row, ["actor", "role", "capabilities"]);
            var role = Enum.Parse<RoleKind>(StrictJson.Text(row, "role"), ignoreCase: true);
            var capabilities = FindingsRequestParser.Array(row.GetProperty("capabilities"), 32)
                .Select(c => Enum.Parse<Capability>(c.GetString()!, ignoreCase: true)).ToArray();
            RoleDefaults.EnsureSafe(role, capabilities);
            return new AILedger.Core.Authority.PreparationRole(new(StrictJson.Text(row, "actor")), role, capabilities);
        }).ToArray();
        var work = FindingsRequestParser.Array(policy.GetProperty("work"), 16).Select(row =>
        {
            StrictJson.Members(row, ["profile", "work", "title", "owner", "scope"], ["base_ref"]);
            return new AILedger.Core.Authority.PreparationWork(StrictJson.Text(row, "profile"), new(StrictJson.Text(row, "work")),
                StrictJson.Text(row, "title"), new(StrictJson.Text(row, "owner")), [Path.GetFullPath(StrictJson.Text(row, "scope"))],
                FindingsRequestParser.OptionalText(row, "base_ref"));
        }).ToArray();
        return new(roles, work);
    }
    private async Task ReconcileStoppedAsync(CliCommandInvocation invocation, CancellationToken token)
    {
        if (invocation.Service is not FileGovernedTaskService host) throw new CliUsageException("Recovery requires trusted file-host composition.");
        var result = await TrustedExecutionRecovery.ConfirmStoppedAsync(host, Task(invocation.Input), Actor(invocation.Input),
            new(invocation.Input.Required("run")), new(invocation.Input.Required("termination-evidence")), token).ConfigureAwait(false);
        await output.WriteJsonAsync(result).ConfigureAwait(false);
    }

    private async Task IntakeAsync(CliCommandInvocation invocation, CancellationToken token)
    {
        if (invocation.Service is not FileGovernedTaskService host) throw new CliUsageException("Trusted intake requires file-host composition.");
        var path = Path.GetFullPath(invocation.Input.Required("request-file"));
        if (new FileInfo(path).Length > 128 * 1024) throw new CliUsageException("Intake exceeds 128 KiB.");
        using var document = StrictJson.Parse(await File.ReadAllBytesAsync(path, token).ConfigureAwait(false));
        var body = document.RootElement;
        StrictJson.Validate(body);
        StrictJson.Members(body, ["schema_version", "request_id", "title", "original_request", "constraints", "tags"]);
        if (!FindingsRequestParser.IsVersionOne(body.GetProperty("schema_version"))) throw new CliUsageException("Unsupported intake schema.");
        static string[] Strings(JsonElement value) => FindingsRequestParser.Array(value, 32)
            .Select(v => v.GetString() ?? throw new JsonException("Expected text.")).ToArray();
        await TrustedDriverIntake.CreateAsync(host, Task(invocation.Input), Actor(invocation.Input), StrictJson.Text(body, "request_id"),
            StrictJson.Text(body, "title"), StrictJson.Text(body, "original_request"), Strings(body.GetProperty("constraints")),
            Strings(body.GetProperty("tags")), token).ConfigureAwait(false);
        await output.WriteJsonAsync(new { status = "recorded", provenance = "trusted-host-intake" }).ConfigureAwait(false);
    }
}
internal sealed class OrchestrationStoppedException(DriverStatus status) : Exception
{
    internal int ExitCode => status == DriverStatus.Cancelled ? 130 : status == DriverStatus.UnknownOutcome ? 5 : 4;
}
