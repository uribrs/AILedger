using System.Text;
using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Storage;

public static class TaskRetentionApplier
{
    private static readonly TaskMutationLock MutationLock = new();

    public static async Task<TaskRetentionResult> ApplyAsync(
        string taskDirectory,
        GovernedTaskState state,
        TaskCloseoutEligibilityReport eligibility,
        TaskRetentionPlan plan,
        ActorId actor,
        DateTimeOffset now,
        CancellationToken cancellationToken = default,
        TaskWorkspaceLayout? layout = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskDirectory);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(eligibility);
        ArgumentNullException.ThrowIfNull(plan);
        layout ??= new TaskWorkspaceLayout();
        timeProvider ??= TimeProvider.System;

        if (!state.Roles.TryGetValue(actor, out var assignment))
        {
            throw new GovernanceException(
                $"Actor '{actor}' has no assigned role and cannot apply a task retention plan.");
        }

        if (assignment.Role is not
            (RoleKind.Operator or RoleKind.PlanningLead or RoleKind.ImplementationLead))
        {
            throw new GovernanceException(
                $"Actor '{actor}' with role '{assignment.Role}' cannot apply a task retention plan.");
        }

        var root = Path.GetFullPath(taskDirectory);
        var lockPath = Path.Combine(root, layout.LockFileName);
        await using var lease = await MutationLock.AcquireAsync(lockPath, cancellationToken)
            .ConfigureAwait(false);

        ValidatePlan(root, state, eligibility, plan, layout);
        var cleanup = Path.Combine(root, TaskRetentionScan.CleanupDirectoryName);
        var intentPath = CheckedCleanupPath(root, IntentPath(cleanup, plan.PlanId));
        var receiptsPath = CheckedCleanupPath(root, ReceiptsPath(cleanup, plan.PlanId));
        var resultPath = CheckedCleanupPath(root, ResultPath(cleanup, plan.PlanId));
        var resumed = File.Exists(intentPath);
        var intent = resumed
            ? await ReadIntentAsync(intentPath, cancellationToken).ConfigureAwait(false)
            : null;
        var receipts = await ReadReceiptsAsync(receiptsPath, plan, cancellationToken).ConfigureAwait(false);

        EnsureApplicable(root, state, eligibility, plan, intent, receipts, layout);
        Directory.CreateDirectory(cleanup);
        CheckedCleanupPath(root, cleanup);
        if (!resumed)
        {
            intent = RetentionIntent.From(plan);
            await WriteJsonAsync(intentPath, intent, overwrite: false, cancellationToken).ConfigureAwait(false);
        }

        var written = new List<TaskRetentionReceipt>();
        foreach (var entry in plan.Entries.Where(item => item.Decision == TaskRetentionDecision.Delete))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (receipts.TryGetValue(entry.Path, out var existing))
            {
                written.Add(existing with { Outcome = TaskRetentionOutcome.AlreadyRemoved });
                continue;
            }

            var receipt = Remove(root, entry, resumed, now);
            await AppendReceiptAsync(cleanup, plan.PlanId, receipt, cancellationToken).ConfigureAwait(false);
            written.Add(receipt);
        }

        var result = Summarise(plan, actor, written, now, timeProvider.GetUtcNow());
        await WriteJsonAsync(resultPath, result, overwrite: true, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    private static void ValidatePlan(
        string root,
        GovernedTaskState state,
        TaskCloseoutEligibilityReport eligibility,
        TaskRetentionPlan plan,
        TaskWorkspaceLayout layout)
    {
        if (plan.SchemaVersion != TaskRetentionPlan.CurrentSchemaVersion)
        {
            throw new GovernanceException(
                $"Retention plan schema version {plan.SchemaVersion} is not version " +
                $"{TaskRetentionPlan.CurrentSchemaVersion}.");
        }

        if (plan.Inputs is null || plan.Entries is null || plan.Totals is null)
        {
            throw new GovernanceException(
                "A retention plan must carry inputs, entries and totals.");
        }

        if (string.IsNullOrWhiteSpace(plan.PlanId) ||
            plan.PlanId is "." or ".." ||
            plan.PlanId.Contains('/') || plan.PlanId.Contains('\\') ||
            !string.Equals(Path.GetFileName(plan.PlanId), plan.PlanId, StringComparison.Ordinal) ||
            plan.PlanId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new GovernanceException("A retention plan id must be one safe file-name segment.");
        }

        EnsureEligible(state, eligibility);
        if (plan.Task != state.TaskId)
        {
            throw new GovernanceException(
                $"Retention plan was written for task '{plan.Task}' and cannot be applied to '{state.TaskId}'.");
        }

        if (plan.Inputs.TaskVersion != state.Version)
        {
            throw new GovernanceException(
                $"Retention plan was written at task version {plan.Inputs.TaskVersion} and the task " +
                $"is now at version {state.Version}. Rebuild it.");
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in plan.Entries)
        {
            if (entry is null)
            {
                throw new GovernanceException("Retention plan entries may not be null.");
            }

            if (string.IsNullOrWhiteSpace(entry.Path) || entry.Path.Contains('\\'))
            {
                throw new GovernanceException(
                    $"Retention plan path '{entry.Path}' must use canonical forward slashes.");
            }

            TaskRetentionScan.ResolveContained(root, entry.Path);
            if (!paths.Add(entry.Path))
            {
                throw new GovernanceException($"Retention plan names '{entry.Path}' more than once.");
            }

            if (entry.Length < 0 || !IsSha256(entry.Sha256) || string.IsNullOrWhiteSpace(entry.Reason))
            {
                throw new GovernanceException(
                    $"Retention plan entry '{entry.Path}' must carry a non-negative length, a SHA-256 " +
                    "digest and a reason.");
            }

            if (!Enum.IsDefined(entry.Decision))
            {
                throw new GovernanceException(
                    $"Retention plan entry '{entry.Path}' has an unknown decision '{entry.Decision}'.");
            }

            if (entry.Decision == TaskRetentionDecision.Delete &&
                (string.IsNullOrWhiteSpace(entry.Evidence) ||
                 string.IsNullOrWhiteSpace(entry.DiagnosticsLost)))
            {
                throw new GovernanceException(
                    $"Retention plan deletion '{entry.Path}' must carry evidence and diagnostics lost.");
            }

            EnsureDeletionPathAllowed(entry, layout);
        }

        var expectedTotals = Totals(plan.Entries);
        if (plan.Totals != expectedTotals)
        {
            throw new GovernanceException("Retention plan totals do not match its entries.");
        }

        var synthesis = Current(state, GovernedArtifactKind.CloseoutSynthesis);
        var retrospective = Current(state, GovernedArtifactKind.WorkflowRetrospective);
        if (synthesis is null ||
            synthesis.ArtifactId.Value != plan.Inputs.CloseoutSynthesisArtifactId ||
            TaskRetentionScan.Sha256OfText(synthesis.Content) != plan.Inputs.CloseoutSynthesisSha256)
        {
            throw new GovernanceException(
                "The current closeout synthesis does not match the one bound into the retention plan.");
        }

        EnsureSynthesisAuthority(synthesis.Content, plan.Entries);

        if (retrospective?.ArtifactId.Value != plan.Inputs.WorkflowRetrospectiveArtifactId)
        {
            throw new GovernanceException(
                "The current workflow retrospective does not match the one bound into the retention plan.");
        }
    }

    private static void EnsureSynthesisAuthority(
        string synthesisContent,
        IReadOnlyList<TaskRetentionEntry> entries)
    {
        var rows = TaskRetentionPlanner.ReadRetention(synthesisContent)
            .ToDictionary(row => row.Path.Replace('\\', '/'), StringComparer.Ordinal);
        foreach (var entry in entries.Where(item => item.Decision == TaskRetentionDecision.Delete))
        {
            if (!rows.TryGetValue(entry.Path, out var row) ||
                row.Decision != "delete" ||
                row.Reason != entry.Reason ||
                row.Evidence != entry.Evidence)
            {
                throw new GovernanceException(
                    $"Retention plan deletion '{entry.Path}' is not the deletion authorised by the " +
                    "current closeout synthesis.");
            }
        }
    }

    private static void EnsureDeletionPathAllowed(
        TaskRetentionEntry entry,
        TaskWorkspaceLayout layout)
    {
        if (entry.Decision != TaskRetentionDecision.Delete)
        {
            return;
        }

        var path = entry.Path.Replace('\\', '/');
        if (TaskRetentionScan.NeverDeletable(layout).Contains(path) ||
            path.Equals(TaskRetentionScan.CleanupDirectoryName, StringComparison.Ordinal) ||
            path.StartsWith(TaskRetentionScan.CleanupDirectoryName + "/", StringComparison.Ordinal))
        {
            throw new GovernanceException($"Retention plan may not delete protected path '{entry.Path}'.");
        }
    }

    private static void EnsureApplicable(
        string root,
        GovernedTaskState state,
        TaskCloseoutEligibilityReport eligibility,
        TaskRetentionPlan plan,
        RetentionIntent? intent,
        IReadOnlyDictionary<string, TaskRetentionReceipt> receipts,
        TaskWorkspaceLayout layout)
    {
        EnsureEligible(state, eligibility);
        EnsureUnchanged(
            Path.Combine(root, layout.EventsFileName),
            layout.EventsFileName,
            plan.Inputs.EventsSha256,
            plan.Inputs.EventsLength);
        // Refusal journal metadata is informational only. The journal is failure-path telemetry,
        // not canonical task state, so a change must never gate applying or resuming a plan.

        if (intent is not null && !IntentMatches(intent, RetentionIntent.From(plan)))
        {
            throw new GovernanceException(
                "The existing cleanup intent does not match this retention plan. It cannot explain " +
                "an absent file.");
        }

        var planned = plan.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var onDisk = TaskRetentionScan.Scan(root, layout)
            .ToDictionary(file => file.RelativePath, StringComparer.Ordinal);
        var unknown = onDisk.Keys.Where(path => !planned.ContainsKey(path)).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length != 0)
        {
            throw new GovernanceException(
                $"The task directory holds {unknown.Length} file(s) this plan does not account for, " +
                $"starting with '{unknown[0]}'. Rebuild the plan.");
        }

        foreach (var entry in plan.Entries)
        {
            if (onDisk.TryGetValue(entry.Path, out var file))
            {
                if (receipts.ContainsKey(entry.Path))
                {
                    throw new GovernanceException(
                        $"'{entry.Path}' exists even though this plan already carries a removal receipt.");
                }

                if (file.Length != entry.Length || file.Sha256 != entry.Sha256)
                {
                    throw ChangedFile(entry, file.Length, file.Sha256);
                }

                continue;
            }

            if (entry.Decision == TaskRetentionDecision.Retain)
            {
                throw new GovernanceException(
                    $"Retained file '{entry.Path}' is missing. The directory no longer matches the plan.");
            }

            if (!receipts.ContainsKey(entry.Path) && intent is null)
            {
                throw new GovernanceException(
                    $"'{entry.Path}' is named for deletion and is already absent, but this plan has " +
                    "no pre-existing intent or receipt. An unexplained absence is not a deletion.");
            }
        }
    }

    private static void EnsureEligible(
        GovernedTaskState state,
        TaskCloseoutEligibilityReport supplied)
    {
        if (supplied.Task != state.TaskId || supplied.Version != state.Version)
        {
            throw new GovernanceException(
                "The closeout eligibility report is for a different task snapshot. Recompute it.");
        }

        var current = TaskCloseoutEligibility.Evaluate(state);
        if (!current.Eligible)
        {
            throw new GovernanceException(TaskRetentionPlanner.IneligibleRefusal(current));
        }

        if (!supplied.Eligible)
        {
            throw new GovernanceException(TaskRetentionPlanner.IneligibleRefusal(supplied));
        }
    }

    private static TaskRetentionReceipt Remove(
        string root,
        TaskRetentionEntry entry,
        bool resumed,
        DateTimeOffset now)
    {
        var path = TaskRetentionScan.ResolveContained(root, entry.Path);
        if (!File.Exists(path))
        {
            if (!resumed)
            {
                throw new GovernanceException(
                    $"'{entry.Path}' became absent after preflight. An unexplained absence is not a deletion.");
            }

            return new TaskRetentionReceipt(
                entry.Path,
                entry.Sha256,
                entry.Length,
                TaskRetentionOutcome.RemovedWithoutReceipt,
                now);
        }

        var length = new FileInfo(path).Length;
        var sha256 = TaskRetentionScan.Sha256(path);
        if (length != entry.Length || sha256 != entry.Sha256)
        {
            throw ChangedFile(entry, length, sha256);
        }

        File.Delete(path);
        return new TaskRetentionReceipt(
            entry.Path, sha256, length, TaskRetentionOutcome.Removed, now);
    }

    private static GovernanceException ChangedFile(
        TaskRetentionEntry entry,
        long actualLength,
        string actualSha256) =>
        new($"'{entry.Path}' has changed since the plan was written: the plan recorded " +
            $"{entry.Length} bytes at {entry.Sha256}, and the file is now {actualLength} bytes at " +
            $"{actualSha256}. Rebuild the plan.");

    private static void EnsureUnchanged(
        string path,
        string name,
        string? sha256,
        long? length)
    {
        if (sha256 is null)
        {
            if (File.Exists(path))
            {
                throw new GovernanceException(
                    $"'{name}' did not exist when the plan was written and exists now. Rebuild the plan.");
            }

            return;
        }

        if (!File.Exists(path))
        {
            throw new GovernanceException($"'{name}' existed when the plan was written and is now missing.");
        }

        if (new FileInfo(path).Length != length || TaskRetentionScan.Sha256(path) != sha256)
        {
            throw new GovernanceException(
                $"'{name}' has changed since the plan was written. Rebuild the plan.");
        }
    }

    private static async Task<RetentionIntent> ReadIntentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return JsonSerializer.Deserialize<RetentionIntent>(
                       await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
                       LedgerJson.CreateOptions())
                   ?? throw new InvalidDataException($"Retention intent '{path}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Retention intent '{path}' is invalid.", exception);
        }
    }

    private static async Task<IReadOnlyDictionary<string, TaskRetentionReceipt>> ReadReceiptsAsync(
        string path,
        TaskRetentionPlan plan,
        CancellationToken cancellationToken)
    {
        var receipts = new Dictionary<string, TaskRetentionReceipt>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return receipts;
        }

        var deletions = plan.Entries
            .Where(entry => entry.Decision == TaskRetentionDecision.Delete)
            .ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var receipt = JsonSerializer.Deserialize<TaskRetentionReceipt>(line, LedgerJson.CreateOptions())
                ?? throw new InvalidDataException($"Retention receipt line in '{path}' is empty.");
            if (!deletions.TryGetValue(receipt.Path, out var entry) ||
                receipt.Sha256 != entry.Sha256 || receipt.Length != entry.Length ||
                receipt.Outcome is not (TaskRetentionOutcome.Removed or
                    TaskRetentionOutcome.RemovedWithoutReceipt) ||
                !receipts.TryAdd(receipt.Path, receipt))
            {
                throw new InvalidDataException(
                    $"Retention receipt for '{receipt.Path}' does not match exactly one deletion in the plan.");
            }
        }

        return receipts;
    }

    private static async Task AppendReceiptAsync(
        string cleanup,
        string planId,
        TaskRetentionReceipt receipt,
        CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(receipt, LedgerJson.CreateOptions()) + "\n";
        await using var stream = new FileStream(
            ReceiptsPath(cleanup, planId), FileMode.Append, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(line), cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(value, LedgerJson.CreateOptions(indented: true)));
        await using var stream = new FileStream(
            path, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static TaskRetentionResult Summarise(
        TaskRetentionPlan plan,
        ActorId actor,
        IReadOnlyList<TaskRetentionReceipt> receipts,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc) =>
        new(
            plan.PlanId,
            plan.Task,
            actor,
            startedAtUtc,
            completedAtUtc,
            receipts.Count(receipt => receipt.Outcome == TaskRetentionOutcome.Removed),
            receipts.Count(receipt => receipt.Outcome == TaskRetentionOutcome.RemovedWithoutReceipt),
            receipts.Count(receipt => receipt.Outcome == TaskRetentionOutcome.AlreadyRemoved),
            receipts.Where(receipt => receipt.Outcome != TaskRetentionOutcome.AlreadyRemoved)
                .Sum(receipt => receipt.Length),
            receipts);

    private static TaskRetentionTotals Totals(IReadOnlyList<TaskRetentionEntry> entries) =>
        new(
            entries.Count(entry => entry.Decision == TaskRetentionDecision.Retain),
            entries.Count(entry => entry.Decision == TaskRetentionDecision.Delete),
            entries.Where(entry => entry.Decision == TaskRetentionDecision.Retain).Sum(entry => entry.Length),
            entries.Where(entry => entry.Decision == TaskRetentionDecision.Delete).Sum(entry => entry.Length));

    private static GovernedArtifact? Current(GovernedTaskState state, GovernedArtifactKind kind) =>
        ArtifactApplicability.Current(state)
            .Where(artifact => artifact.Kind == kind)
            .OrderBy(artifact => artifact.ArtifactId.Value, StringComparer.Ordinal)
            .FirstOrDefault();

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IntentMatches(RetentionIntent actual, RetentionIntent expected) =>
        actual.PlanId == expected.PlanId &&
        actual.Task == expected.Task &&
        actual.TaskVersion == expected.TaskVersion &&
        actual.EventsSha256 == expected.EventsSha256 &&
        actual.CloseoutSynthesisSha256 == expected.CloseoutSynthesisSha256 &&
        actual.Deletions is not null && actual.Deletions.SequenceEqual(expected.Deletions);

    private static string CheckedCleanupPath(string root, string path)
    {
        var relative = TaskRetentionScan.Relative(root, path);
        return TaskRetentionScan.ResolveContained(root, relative);
    }

    internal static string IntentPath(string cleanup, string planId) =>
        Path.Combine(cleanup, $"{planId}.intent.json");

    internal static string ReceiptsPath(string cleanup, string planId) =>
        Path.Combine(cleanup, $"{planId}.receipts.jsonl");

    internal static string ResultPath(string cleanup, string planId) =>
        Path.Combine(cleanup, $"{planId}.result.json");

    private sealed record RetentionIntent(
        string PlanId,
        TaskId Task,
        long TaskVersion,
        string EventsSha256,
        string CloseoutSynthesisSha256,
        IReadOnlyList<RetentionIntentEntry> Deletions)
    {
        internal static RetentionIntent From(TaskRetentionPlan plan) =>
            new(
                plan.PlanId,
                plan.Task,
                plan.Inputs.TaskVersion,
                plan.Inputs.EventsSha256,
                plan.Inputs.CloseoutSynthesisSha256,
                plan.Entries.Where(entry => entry.Decision == TaskRetentionDecision.Delete)
                    .Select(entry => new RetentionIntentEntry(entry.Path, entry.Sha256, entry.Length))
                    .ToArray());
    }

    private sealed record RetentionIntentEntry(string Path, string Sha256, long Length);
}
