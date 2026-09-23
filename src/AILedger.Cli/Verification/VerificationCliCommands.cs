using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AILedger.Cli.Routing;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Providers.Verification;
using AILedger.Storage;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Verification;

/// <summary>
/// <c>ailedger verification run</c> (contract S4-S8, S11, S12). It runs the command the profile at
/// the checkout's repository root declares, host-side, in that root, and records what happened as one
/// ordinary evidence entry citing a retained result file by digest.
/// </summary>
/// <remarks>
/// <para>
/// <c>--actor</c> is asserted, not authenticated (PC22), so the operator check alone does not keep a
/// governed child from running this command. The confirmation digest covers only the command text,
/// as <see cref="LessonRecheck"/> does: not the checkout the command executes, not the profile's
/// requirements or timeout. It stops a command being run unread; it does not vouch for what the
/// command runs. For a docker profile, the socket preflight is the real bound: it passes only when
/// this process itself can reach the socket, which a sandboxed child cannot (PC11). A profile that
/// does not require docker has no further bound, and runs with whatever sandbox the invoker has.
/// </para>
/// <para>
/// The source type <see cref="SourceType"/> is free text any actor may record, so it proves nothing
/// alone. What the evidence carries is the digest of the retained result file it cites, and the
/// actor and time the ledger records with it. There is no free-form command option.
/// </para>
/// </remarks>
internal sealed class VerificationCliCommands(
    CliCommandExecutor executor,
    JsonSerializerOptions json,
    Func<VerificationHost> host)
{
    public const string SourceType = "container-verification";
    public const string RunVariable = "AILEDGER_VERIFICATION_RUN";
    public const string OutputVariable = "AILEDGER_VERIFICATION_OUTPUT";
    private const string DockerHostVariable = "DOCKER_HOST";
    private const string RyukVariable = "TESTCONTAINERS_RYUK_DISABLED";
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(120);

    public CliCommandRegistration Registration() => new(
        ["verification run"],
        CliCommandOptions.Set(
            "root", "task", "actor", "id", "checkout", "candidate", "profile", "confirm",
            "timeout-seconds", "supports", "refutes", "cause", "correlation"),
        isReadOnly: false,
        RunAsync);

    private async Task RunAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var input = invocation.Input;
        var taskId = Task(input);
        var actor = Actor(input);
        var evidenceId = SafeEvidenceId(input.Required("id"));
        var checkout = Path.GetFullPath(input.Required("checkout"));
        var candidate = input.Required("candidate");
        if (!Sha256Hex.IsMatch(candidate))
        {
            throw new CliUsageException("--candidate must be 64 lowercase hexadecimal characters.");
        }

        var timeoutOverride = TimeoutSeconds(input.Optional("timeout-seconds"));
        var supports = input.Many("supports").Select(value => new ClaimId(value)).ToArray();
        var refutes = input.Many("refutes").Select(value => new ClaimId(value)).ToArray();
        var confirmation = input.Optional("confirm");
        var verificationHost = host();
        var taskPath = new TaskWorkspacePathResolver(invocation.LedgerRoot).Resolve(taskId);
        var verificationRun = $"{taskId.Value}.{evidenceId.Value}";
        var directory = Path.Combine(taskPath, "verification", evidenceId.Value);
        var output = Path.Combine(directory, "results");

        if (confirmation is null)
        {
            // The listing. Nothing is started, nothing is created, and no authority is needed to read
            // what a confirmation would run.
            if (!Directory.Exists(checkout))
            {
                throw new CliUsageException($"Checkout '{checkout}' does not exist.");
            }

            var planPath = VerificationProfileFile.Discover(checkout)
                ?? throw new GovernanceException(
                    $"verification run: no {VerificationProfileFile.FileName} was found at the repository root " +
                    $"of '{checkout}'.");
            var (planFile, planProfile) = ResolveProfile(planPath, input.Optional("profile"));
            // Written without HTML escaping: the operator reads this command before confirming it,
            // and '>' printed as > is text nobody can check by eye.
            await executor.WriteLineAsync(JsonSerializer.Serialize(new
            {
                ProfilePath = planFile.Path,
                ProfileSha256 = planFile.Sha256,
                ProfileName = planProfile.Name,
                planProfile.Command,
                Environment = ProfileEnvironment(planProfile, verificationHost, verificationRun, output),
                Confirmation = Confirmation(planProfile.Command)
            }, ResultJson(json))).ConfigureAwait(false);
            return;
        }

        // S4 refusal order. Every one of these happens before any profile process exists.
        var state = await CliCommandExecutor.RequireStateAsync(invocation, cancellationToken).ConfigureAwait(false);
        // 1. The same expression as RoleAssignmentRules.IsOperator, which is internal to Core.
        if (!state.Roles.TryGetValue(actor, out var assignment) || assignment.Role != RoleKind.Operator)
        {
            throw new GovernanceException(
                $"verification run: actor '{actor}' is not an operator on task '{taskId}'. Only an operator " +
                "may run a verification profile, because it executes outside any agent sandbox.");
        }

        // 2. The root comes from git, with links resolved (/tmp is /private/tmp); it is never compared
        // with the path as given.
        var repositoryRoot = Directory.Exists(checkout)
            ? await RepositoryRootAsync(checkout, cancellationToken).ConfigureAwait(false)
            : null;
        if (repositoryRoot is null)
        {
            throw new GovernanceException($"verification run: checkout '{checkout}' is not a git work tree.");
        }

        // 3. Only the file at the repository root is used; one in any other directory never is (S1).
        var rootProfile = Path.Combine(repositoryRoot, VerificationProfileFile.FileName);
        if (!File.Exists(rootProfile))
        {
            throw new GovernanceException(
                $"verification run: no {VerificationProfileFile.FileName} at the repository root '{repositoryRoot}'. " +
                "A profile file in any other directory is not used.");
        }

        var (file, profile) = ResolveProfile(rootProfile, input.Optional("profile"));

        // 4.
        var expected = Confirmation(profile.Command);
        if (!string.Equals(confirmation.Trim(), expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new GovernanceException(
                $"verification run: the confirmation does not match the command of profile '{profile.Name}' " +
                $"in '{file.Path}', so nothing was run. Run without --confirm, read the command, and pass back " +
                "the confirmation printed beside it.");
        }

        // 5. The id names the evidence entry and the result directory; both must be new. Claims are
        // checked here too, because a claim refused after the run would leave a result with no entry.
        if (state.Evidence.ContainsKey(evidenceId))
        {
            throw new GovernanceException($"verification run: evidence '{evidenceId}' already exists on task '{taskId}'.");
        }

        if (Directory.Exists(directory))
        {
            throw new GovernanceException(
                $"verification run: result directory '{directory}' already exists; choose a new evidence id.");
        }

        foreach (var claim in supports.Concat(refutes))
        {
            if (!state.Claims.ContainsKey(claim))
            {
                throw new GovernanceException($"verification run: claim '{claim}' does not exist on task '{taskId}'.");
            }
        }

        // 6.
        var dockerHost = verificationHost.DockerHost();
        if (profile.RequiresDocker &&
            await DockerSocketPreflight.CheckAsync(
                dockerHost, verificationHost.CreateEngineHandler, cancellationToken).ConfigureAwait(false) is { } unreachable)
        {
            throw new GovernanceException($"verification run: {unreachable}.");
        }

        // What ran is the checkout as it stood when the command started: its HEAD and the content of
        // every file git lists (S12). A failure here refuses the run before anything is created.
        string gitHead;
        string worktreeBefore;
        try
        {
            gitHead = Encoding.UTF8.GetString(
                await GitAsync(repositoryRoot, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false)).Trim();
            worktreeBefore = await WorktreeFingerprint.ComputeAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new GovernanceException(
                $"verification run: the checkout '{repositoryRoot}' could not be measured, so nothing was run: {exception.Message}");
        }

        // Every value that can throw is computed above; from here the id is reserved (S11), and any
        // failure before the spawner releases it again.
        var environment = ProfileEnvironment(profile, verificationHost, verificationRun, output);
        var stdoutPath = Path.Combine(directory, "stdout.log");
        var stderrPath = Path.Combine(directory, "stderr.log");
        var timeout = TimeSpan.FromSeconds(timeoutOverride ?? profile.TimeoutSeconds);
        using var reservation = VerificationReservation.Take(directory);
        Directory.CreateDirectory(output);
        var errors = new List<string>();
        int? exitCode;
        string status;
        DateTimeOffset startedAt;
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            deadline.CancelAfter(timeout);
            string? startFailure = null;
            startedAt = verificationHost.Clock.GetUtcNow();
            reservation.Commit();
            try
            {
                exitCode = await verificationHost.Spawner.RunAsync(
                    new VerificationProcessStart(profile.Command, repositoryRoot, environment, stdoutPath, stderrPath),
                    deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                exitCode = null;
            }
            catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or
                                                  InvalidOperationException or UnauthorizedAccessException)
            {
                // Keep the failure even if its error log is itself unwritable.
                exitCode = null;
                startFailure = exception.Message;
                errors.Add($"verification run: {startFailure}");
            }

            status = exitCode switch
            {
                0 => "passed",
                not null => "failed",
                null when startFailure is not null => "failed",
                null when cancellationToken.IsCancellationRequested => "cancelled",
                null => "timed-out"
            };
        }

        var endedAt = verificationHost.Clock.GetUtcNow();

        // A change the run made to the checkout shows as a different digest; it does not change the
        // status. The measurement runs on its own token, like cleanup, and a failure leaves it null.
        string? worktreeAfter;
        try
        {
            worktreeAfter = await WorktreeFingerprint.ComputeAsync(repositoryRoot, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            worktreeAfter = null;
            errors.Add($"verification run: the checkout could not be measured after the run: {exception.Message}");
        }

        // Cleanup and the record run on their own token: the one that ended the process has fired,
        // and a cancelled run is exactly the one whose containers must still be removed (R5).
        ContainerCleanupRecord? cleanup = null;
        if (profile.RequiresDocker)
        {
            using var cleanupDeadline = new CancellationTokenSource(CleanupTimeout);
            var socketPath = UnixSocketEngineHandler.SocketPath(dockerHost)!;
            cleanup = await ContainerCleanup.RunAsync(
                verificationHost.CreateEngineHandler(socketPath), verificationRun, cleanupDeadline.Token)
                .ConfigureAwait(false);
        }

        var errorsBeforeCollection = errors.Count;
        var resultFiles = await ResultFilesAsync(taskPath, output, profile.ResultFiles, errors).ConfigureAwait(false);
        if (errors.Count > errorsBeforeCollection && status == "passed")
        {
            status = "failed";
        }

        await AppendDiagnosticsAsync(stderrPath, errors).ConfigureAwait(false);
        var result = new VerificationResult(
            2, taskId.Value, evidenceId.Value, verificationRun, candidate, checkout, repositoryRoot,
            gitHead, worktreeBefore, worktreeAfter,
            file.Path, file.Sha256, profile.Name, profile.Command, environment,
            startedAt, endedAt, exitCode, status,
            Relative(taskPath, stdoutPath), Relative(taskPath, stderrPath),
            resultFiles,
            cleanup,
            RunningKernelIdentity.Current,
            errors.Count == 0 ? null : errors.ToArray());
        string? resultPath = Path.Combine(directory, "result.json");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, ResultJson(json));
        string? resultSha256 = null;
        try
        {
            await File.WriteAllBytesAsync(resultPath, bytes, CancellationToken.None).ConfigureAwait(false);
            resultSha256 = Sha256(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Could not retain result.json: {exception.Message}");
            resultPath = null;
            if (status == "passed") status = "failed";
        }

        var summary =
            $"{status}; exit {(exitCode is { } code ? code.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none")}; " +
            $"profile {profile.Name}; asserted candidate {candidate}; git {gitHead}; " +
            $"worktree {worktreeBefore[..12]} {(worktreeAfter is null ? "after unmeasured" : worktreeAfter == worktreeBefore ? "unchanged" : "changed")}; " +
            $"{result.ResultFiles.Count} result files; removed {cleanup?.Removed.Count ?? 0} containers" +
            (errors.Count == 0 ? string.Empty : $"; errors: {string.Join("; ", errors)}");
        CommandOutcome outcome;
        try
        {
            outcome = await invocation.Service.ExecuteAsync(
                taskId,
                new AddEvidenceCommand(
                    actor, Cause(input), Correlation(input), evidenceId, SourceType,
                    resultSha256 is null
                        ? $"verification/{evidenceId.Value}: result.json unavailable; see evidence summary"
                        : $"verification/{evidenceId.Value}/result.json sha256:{resultSha256}",
                    summary, supports, refutes),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (GovernanceException refusal)
        {
            throw new GovernanceException(
                "verification run: " +
                (resultSha256 is null ? $"result.json could not be retained ({string.Join("; ", errors)})"
                    : $"the result was retained at '{resultPath}' (sha256 {resultSha256})") +
                $", but the evidence entry was refused: {refusal.Message}");
        }

        await executor.WriteJsonAsync(new
        {
            outcome.State.TaskId,
            outcome.State.Version,
            Events = outcome.Events.Select(@event => @event.EventId),
            EvidenceId = evidenceId,
            Status = status,
            ExitCode = exitCode,
            ResultPath = resultPath,
            ResultSha256 = resultSha256
        }).ConfigureAwait(false);

        if (status == "cancelled")
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (status != "passed")
        {
            throw new VerificationFailedException(
                $"Verification '{evidenceId}' ended with status '{status}'; evidence was recorded.");
        }
    }

    /// <summary>Contract S4: lowercase hex SHA-256 of the UTF-8 command text.</summary>
    internal static string Confirmation(string command) => Sha256(Encoding.UTF8.GetBytes(command));

    /// <summary>Contract S2: the four variables the kernel adds or replaces, and no others.</summary>
    internal static Dictionary<string, string> ProfileEnvironment(
        VerificationProfile profile,
        VerificationHost host,
        string verificationRun,
        string output)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        if (profile.RequiresDocker)
        {
            environment[DockerHostVariable] = host.DockerHost();
            environment[RyukVariable] = "true";
        }

        environment[RunVariable] = verificationRun;
        environment[OutputVariable] = output;
        return environment;
    }

    private static (VerificationProfileFile File, VerificationProfile Profile) ResolveProfile(
        string path,
        string? name)
    {
        VerificationProfileFile file;
        try
        {
            file = VerificationProfileFile.Read(path);
        }
        catch (VerificationProfileException invalid)
        {
            throw new GovernanceException($"verification run: {invalid.Message}");
        }

        if (name is not null)
        {
            return (file, file.Profiles.FirstOrDefault(profile => profile.Name == name)
                ?? throw new GovernanceException(
                    $"verification run: profile '{name}' is not declared in '{file.Path}'. Declared: " +
                    $"{string.Join(", ", file.Profiles.Select(profile => profile.Name))}."));
        }

        return file.Profiles.Count == 1
            ? (file, file.Profiles[0])
            : throw new GovernanceException(
                $"verification run: '{file.Path}' declares {file.Profiles.Count} profiles; name one with --profile " +
                $"({string.Join(", ", file.Profiles.Select(profile => profile.Name))}).");
    }

    private static async Task AppendDiagnosticsAsync(string stderrPath, List<string> errors)
    {
        if (errors.Count == 0) return;
        try
        {
            await File.AppendAllTextAsync(stderrPath, string.Join('\n', errors) + "\n", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Could not append stderr.log: {exception.Message}");
        }
    }

    private static async Task<IReadOnlyList<VerificationResultFile>> ResultFilesAsync(
        string taskPath,
        string output,
        IReadOnlyList<string> globs,
        List<string> errors)
    {
        if (globs.Count == 0)
        {
            return [];
        }

        var patterns = globs.Select(GlobToRegex).ToArray();
        var files = new List<VerificationResultFile>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(output, path).Replace('\\', '/');
                if (!patterns.Any(pattern => pattern.IsMatch(relative))) continue;
                try
                {
                    await using var stream = File.OpenRead(path);
                    var digest = await SHA256.HashDataAsync(stream, CancellationToken.None).ConfigureAwait(false);
                    files.Add(new VerificationResultFile(Relative(taskPath, path),
                        Convert.ToHexString(digest).ToLowerInvariant(), stream.Length));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"Could not collect result '{relative}': {exception.Message}");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Could not enumerate results: {exception.Message}");
        }

        return files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }

    // '**/' is any number of directories, '*' is any run within one segment, '?' is one character.
    internal static Regex GlobToRegex(string glob)
    {
        var pattern = new StringBuilder("^");
        var normalized = glob.Replace('\\', '/');
        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            if (character == '*' && index + 1 < normalized.Length && normalized[index + 1] == '*')
            {
                var slash = index + 2 < normalized.Length && normalized[index + 2] == '/';
                pattern.Append(slash ? "(?:.*/)?" : ".*");
                index += slash ? 2 : 1;
            }
            else
            {
                pattern.Append(character switch
                {
                    '*' => "[^/]*",
                    '?' => "[^/]",
                    _ => Regex.Escape(character.ToString())
                });
            }
        }

        return new Regex(pattern.Append('$').ToString(), RegexOptions.CultureInvariant);
    }

    // The work tree's top level, or null when the checkout is not inside a work tree.
    private static async Task<string?> RepositoryRootAsync(string checkout, CancellationToken cancellationToken)
    {
        try
        {
            var output = await GitAsync(checkout, ["rev-parse", "--show-toplevel"], cancellationToken)
                .ConfigureAwait(false);
            var root = Encoding.UTF8.GetString(output).Trim();
            return root.Length == 0 ? null : Path.GetFullPath(root);
        }
        catch (IOException)
        {
            return null;
        }
    }

    // Every git call passes --no-optional-locks: a pilot worktree's index lives in the primary
    // checkout's git directory, which a read must not refresh or lock (K5).
    internal static async Task<byte[]> GitAsync(
        string checkout,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--no-optional-locks");
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(checkout);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new IOException($"git could not be started: {exception.Message}", exception);
        }

        using (process)
        {
            if (process is null)
            {
                throw new IOException("git could not be started.");
            }

            process.StandardInput.Close();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(GitTimeout);
            using var buffer = new MemoryStream();
            var copy = process.StandardOutput.BaseStream.CopyToAsync(buffer, deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                await copy.ConfigureAwait(false);
                await error.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                cancellationToken.ThrowIfCancellationRequested();
                throw new IOException($"git {string.Join(' ', arguments)} did not finish within {GitTimeout.TotalSeconds:0} seconds.");
            }

            if (process.ExitCode != 0)
            {
                throw new IOException(
                    $"git {string.Join(' ', arguments)} in '{checkout}' exited {process.ExitCode}: {(await error.ConfigureAwait(false)).Trim()}");
            }

            return buffer.ToArray();
        }
    }

    private static JsonSerializerOptions ResultJson(JsonSerializerOptions ledger) =>
        // The ledger options drop nulls; S5 records a killed process's exit code and a docker-free
        // profile's cleanup as explicit nulls. The command text is kept readable, not HTML-escaped.
        new(ledger)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

    private static string Relative(string taskPath, string path) =>
        Path.GetRelativePath(taskPath, path).Replace('\\', '/');

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static EvidenceId SafeEvidenceId(string value) =>
        value is not ("." or "..") && !value.StartsWith('-') &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            ? new EvidenceId(value)
            : throw new CliUsageException(
                $"Evidence id '{value}' is not allowed here: it names a result directory, so it may contain only " +
                "letters, digits, '-', '_' and '.', may not begin with '-', and may not be '.' or '..'.");

    // Bounded here, as the profile's timeoutSeconds is, so CancelAfter can never throw after the
    // result directory exists (PC41).
    private static int? TimeoutSeconds(string? value) =>
        value is null ? null
        : int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
          parsed is >= 1 and <= VerificationProfileFile.MaxTimeoutSeconds
            ? parsed
            : throw new CliUsageException(
                $"--timeout-seconds must be an integer from 1 to {VerificationProfileFile.MaxTimeoutSeconds}.");

    private static readonly Regex Sha256Hex = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
}

/// <summary>
/// Contract S5 schema version 2, in field order. The candidate id is the coordinator's assertion,
/// checked for format only; what ran is identified by <see cref="GitHead"/> and
/// <see cref="WorktreeSha256Before"/>.
/// </summary>
internal sealed record VerificationResult(
    int SchemaVersion,
    string TaskId,
    string EvidenceId,
    string VerificationRun,
    string AssertedCandidateId,
    string Checkout,
    string RepositoryRoot,
    string GitHead,
    string WorktreeSha256Before,
    string? WorktreeSha256After,
    string ProfilePath,
    string ProfileSha256,
    string ProfileName,
    string Command,
    IReadOnlyDictionary<string, string> Environment,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int? ExitCode,
    string Status,
    string StdoutPath,
    string StderrPath,
    IReadOnlyList<VerificationResultFile> ResultFiles,
    ContainerCleanupRecord? Cleanup,
    KernelBuildIdentity KernelVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Errors = null);

internal sealed record VerificationResultFile(string Path, string Sha256, long Bytes);

/// <summary>A verification that ran and did not pass. Its evidence is already recorded.</summary>
internal sealed class VerificationFailedException(string message) : Exception(message);
