using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Storage;

public sealed class FileGovernedTaskService : IGovernedTaskService
{
    private const int RecalledArchivedTaskLimit = 3;
    private const int TaggedRecalledLessonLimit = 10;
    // Measured on this ledger an event costs 577 to 1061 bytes, averaging near a kilobyte now that
    // artifacts carry their bodies inline, so ten thousand events is about ten megabytes and the
    // byte bound stays the looser of the two rather than becoming the surprise limit. Replay is the
    // real cost, not disk: a 553-event task replays inside a 25ms invocation including process
    // start, which puts ten thousand events near 150ms per command. That is why this is a raised
    // bound and not a compaction scheme — rotation buys nothing at a cost this size.
    public const int DefaultMaximumEventsPerTask = 10_000;
    public const long DefaultMaximumEventLogBytes = 64 * 1024 * 1024;

    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly TaskWorkspacePathResolver _pathResolver;
    private readonly ICommandHandler _commandHandler;
    private readonly ITaskReducer _reducer;
    private readonly ITaskProjectionWriter _projectionWriter;
    private readonly TaskWorkspaceLayout _layout;
    private readonly TaskMutationLock _mutationLock = new();
    private readonly JsonSerializerOptions _eventJson = LedgerJson.CreateOptions();
    // The event log carries artifact bodies; state.json is a projection of it and carries their
    // metadata and size instead. Both the write and the currency comparison below go through these
    // options, so the file on disk and the expected text are projected the same way. An edited body
    // cannot hide behind the elision: an artifact is immutable, a revision is a new record, and
    // either way the version in this file moves.
    private readonly JsonSerializerOptions _stateJson = LedgerJson.CreateProjectionOptions(indented: true);
    private readonly int _maximumEventsPerTask;
    private readonly long _maximumEventLogBytes;
    private readonly ILessonStore? _lessonStore;

    public FileGovernedTaskService(
        string workspaceRoot,
        ICommandHandler commandHandler,
        ITaskReducer reducer,
        ITaskProjectionWriter? projectionWriter = null,
        TaskWorkspaceLayout? layout = null,
        int maximumEventsPerTask = DefaultMaximumEventsPerTask,
        long maximumEventLogBytes = DefaultMaximumEventLogBytes,
        // Without a store, recall reaches only this root's own archived tasks, which is what it did
        // before lessons crossed repositories. The per-user default belongs to the process that
        // composes the service, not to the library, so that a caller holding a temporary root never
        // writes into the operator's real store by omission.
        ILessonStore? lessonStore = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEventsPerTask);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEventLogBytes);
        _pathResolver = new TaskWorkspacePathResolver(workspaceRoot);
        _commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
        _reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));
        _layout = layout ?? new TaskWorkspaceLayout();
        _projectionWriter = projectionWriter ?? new MarkdownTaskProjectionWriter(_layout);
        _maximumEventsPerTask = maximumEventsPerTask;
        _maximumEventLogBytes = maximumEventLogBytes;
        _lessonStore = lessonStore;
    }

    public async Task<CommandOutcome> ExecuteAsync(
        TaskId taskId,
        LedgerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command is OpenTaskCommand open)
        {
            command = open with
            {
                RecalledLessons = await LoadArchivedLessonsAsync(taskId, open.Tags, cancellationToken)
                    .ConfigureAwait(false)
            };
        }

        var taskDirectory = _pathResolver.Resolve(taskId);
        _pathResolver.EnsureTaskDirectory(taskDirectory);

        await using var lease = await _mutationLock.AcquireAsync(
            Path.Combine(taskDirectory, _layout.LockFileName), cancellationToken).ConfigureAwait(false);

        var currentState = await ReplayAsync(taskId, taskDirectory, cancellationToken).ConfigureAwait(false);
        var outcome = _commandHandler.Handle(currentState, command, DateTimeOffset.UtcNow);
        ValidateOutcome(taskId, currentState, outcome);
        if (outcome.State.Version > _maximumEventsPerTask)
        {
            throw new GovernanceException(
                $"Task '{taskId}' reached the limit of {_maximumEventsPerTask} events. Archive it and open a " +
                "successor that depends on the claims this one validated; there is no compaction path, and a " +
                "task id is embedded in every one of its events so the log cannot be rewritten.");
        }

        await AppendEventsAsync(taskDirectory, outcome.Events, cancellationToken).ConfigureAwait(false);
        await TryPublishMintedLessonsAsync(outcome.Events).ConfigureAwait(false);
        await TryRepairDerivedStateAsync(taskDirectory, outcome.State).ConfigureAwait(false);
        return outcome;
    }

    // Archiving is the moment a lesson exists, and the store is how it reaches a task in another
    // repository. Publication happens after the commit, so it cannot fail the archive; a failure
    // here is repaired by the reconciliation the next opening in this root performs.
    private async Task TryPublishMintedLessonsAsync(IReadOnlyList<LedgerEvent> events)
    {
        if (_lessonStore is null)
        {
            return;
        }

        var minted = events.Select(item => item.Data).OfType<LessonMinted>()
            .Select(item => item.Lesson).ToArray();
        if (minted.Length == 0)
        {
            return;
        }

        try
        {
            await _lessonStore.PublishAsync(minted, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // The task's own history keeps the lesson. Reconciliation at the next opening in this
            // root republishes it.
        }
    }

    private async Task<IReadOnlyList<Lesson>> LoadArchivedLessonsAsync(
        TaskId openingTaskId,
        IReadOnlyList<string>? openingTags,
        CancellationToken cancellationToken)
    {
        var local = await LoadLessonsFromArchivedSiblingsAsync(openingTaskId, cancellationToken)
            .ConfigureAwait(false);
        if (_lessonStore is null)
        {
            return SelectRecalled(local, openingTags);
        }

        // Publishing this root's archived lessons before reading makes the two sources one set
        // rather than two competing recalls, and it is what repairs a publication that failed when
        // the source task archived.
        await _lessonStore.PublishAsync(local, cancellationToken).ConfigureAwait(false);
        var published = await _lessonStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        return SelectRecalled(published
            .Where(lesson => lesson.SourceTaskId != openingTaskId)
            .ToArray(), openingTags);
    }

    private async Task<IReadOnlyList<Lesson>> LoadLessonsFromArchivedSiblingsAsync(
        TaskId openingTaskId,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_pathResolver.WorkspaceRoot))
        {
            return [];
        }

        var archivedTasks = new List<GovernedTaskState>();
        foreach (var taskDirectory in Directory.EnumerateDirectories(_pathResolver.WorkspaceRoot)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(taskDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                // Real task workspaces reject reparse points. Recall must not create a second path
                // that follows a sibling link outside the authoritative root.
                continue;
            }

            var taskName = Path.GetFileName(taskDirectory);
            if (string.Equals(taskName, openingTaskId.Value, StringComparison.Ordinal))
            {
                continue;
            }

            GovernedTaskState? archived;
            try
            {
                var sourceTaskId = new TaskId(taskName);
                // Event-log replacement is atomic. Reading without taking a second task lock avoids
                // the A-opens-B/B-opens-A deadlock while still yielding either complete snapshot.
                archived = await ReplayAsync(
                    sourceTaskId, taskDirectory, cancellationToken, allowConcurrentReplacement: true)
                    .ConfigureAwait(false);
            }
            catch (ArgumentException)
            {
                // The root may contain administrative directories that are not task identifiers.
                continue;
            }

            if (archived?.Stage != TaskStage.Archive)
            {
                continue;
            }

            archivedTasks.Add(archived);
        }

        return archivedTasks
            // A task also holds the lessons it recalled from elsewhere. Only the ones it minted
            // itself are its own to publish.
            .SelectMany(task => task.Lessons.Values.Where(lesson => lesson.SourceTaskId == task.TaskId))
            .ToArray();
    }

    private static IReadOnlyList<Lesson> SelectRecalled(
        IReadOnlyList<Lesson> candidates,
        IReadOnlyList<string>? openingTags)
    {
        if (openingTags is null || openingTags.Count == 0)
        {
            return SelectLegacyRecalled(candidates);
        }

        var requestedTags = openingTags.ToHashSet(StringComparer.Ordinal);
        // Supersession is global, not tag-local. A replacement that no longer carries one of the
        // opening task's tags still withdraws the older lesson from recall.
        var superseded = candidates
            .Where(lesson => lesson.SupersedesLessonId is not null)
            .Select(lesson => lesson.SupersedesLessonId!.Value)
            .ToHashSet();

        return candidates
            .Where(lesson => !superseded.Contains(lesson.Id))
            .Where(lesson => lesson.Tags?.Any(requestedTags.Contains) == true)
            // Cross-repository task-id collisions produce the same lesson id. Keep the newest
            // copy before applying the cap so a collision cannot consume a recall slot.
            .GroupBy(lesson => lesson.Id)
            .Select(group => MostRecentLesson(group))
            .OrderByDescending(lesson => lesson.Provenance.RecordedAt)
            .ThenByDescending(lesson => lesson.SourceTaskId.Value, StringComparer.Ordinal)
            .ThenBy(lesson => lesson.Id.Value, StringComparer.Ordinal)
            .Take(TaggedRecalledLessonLimit)
            .OrderBy(lesson => lesson.Id.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<Lesson> SelectLegacyRecalled(IReadOnlyList<Lesson> candidates)
    {
        var lessons = candidates
            // A source task that minted nothing has no recency and nothing to recall, so it does
            // not occupy a slot in the cap.
            .GroupBy(lesson => lesson.SourceTaskId)
            // Recency is the provenance the kernel stamps on a lesson when its source task
            // archives. Nothing enforces a date prefix on a task id, so ordering by id made recall
            // depend on a naming convention and silently dropped newer lessons under undated ids.
            .OrderByDescending(source => source.Max(lesson => lesson.Provenance.RecordedAt))
            // Two tasks archived within the same tick still need one order on every replay.
            .ThenByDescending(source => source.Key.Value, StringComparer.Ordinal)
            .Take(RecalledArchivedTaskLimit)
            .SelectMany(source => source)
            .ToArray();
        var superseded = lessons
            .Where(lesson => lesson.SupersedesLessonId is not null)
            .Select(lesson => lesson.SupersedesLessonId!.Value)
            .ToHashSet();

        return lessons
            .Where(lesson => !superseded.Contains(lesson.Id))
            // A lesson identifier is derived from its source task's id, so two repositories that
            // named a task the same way mint colliding identifiers. The kernel refuses an opening
            // that carries a duplicate, and neither repository could fix the other's task id, so
            // recall keeps the more recent lesson rather than letting the coincidence block work.
            .GroupBy(lesson => lesson.Id)
            .Select(group => MostRecentLesson(group))
            .OrderBy(lesson => lesson.Id.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static Lesson MostRecentLesson(IEnumerable<Lesson> lessons) =>
        lessons
            .OrderByDescending(lesson => lesson.Provenance.RecordedAt)
            .ThenBy(lesson => lesson.Repo ?? string.Empty, StringComparer.Ordinal)
            .First();

    public async Task<GovernedTaskState?> GetStateAsync(TaskId taskId, CancellationToken cancellationToken)
    {
        var taskDirectory = _pathResolver.Resolve(taskId);
        if (!Directory.Exists(taskDirectory))
        {
            return null;
        }

        await using var lease = await _mutationLock.AcquireAsync(
            Path.Combine(taskDirectory, _layout.LockFileName), cancellationToken).ConfigureAwait(false);

        var state = await ReplayAsync(taskId, taskDirectory, cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            return null;
        }

        // Before any repair: a replay that lands behind the recorded version means history was
        // lost, and the repair below would heal the projection into the shorter log and erase the
        // only local evidence of it. This is a data condition, not a disposable-view failure, so
        // it must sit outside the fault-tolerant block.
        await EnsureEventLogHasNotLostHistoryAsync(taskDirectory, state, cancellationToken).ConfigureAwait(false);

        try
        {
            if (!await MaterializedStateIsCurrentAsync(taskDirectory, state, cancellationToken).ConfigureAwait(false))
            {
                await WriteMaterializedStateAsync(taskDirectory, state, cancellationToken).ConfigureAwait(false);
            }

            // Projections are disposable views. Rewriting them on every read also repairs a
            // missing or partially-written projection when state.json itself is current.
            await _projectionWriter.WriteAsync(taskDirectory, state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsFatal(exception) &&
                                          !(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // Authoritative replay succeeded. A presentation repair must not make that
            // state unavailable; a later read or explicit filesystem repair can retry it.
        }

        return state;
    }

    public async IAsyncEnumerable<LedgerEvent> GetHistoryAsync(
        TaskId taskId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var taskDirectory = _pathResolver.Resolve(taskId);
        if (!Directory.Exists(taskDirectory))
        {
            yield break;
        }

        List<LedgerEvent> snapshot = [];
        await using (await _mutationLock.AcquireAsync(
                         Path.Combine(taskDirectory, _layout.LockFileName), cancellationToken).ConfigureAwait(false))
        {
            var eventsPath = Path.Combine(taskDirectory, _layout.EventsFileName);
            if (File.Exists(eventsPath))
            {
                await foreach (var @event in ReadEventsAsync(eventsPath, cancellationToken).ConfigureAwait(false))
                {
                    ValidateEventEnvelope(taskId, @event, snapshot.Count);
                    snapshot.Add(@event);
                }
            }
        }

        foreach (var @event in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return @event;
        }
    }

    private async Task<GovernedTaskState?> ReplayAsync(
        TaskId taskId,
        string taskDirectory,
        CancellationToken cancellationToken,
        bool allowConcurrentReplacement = false)
    {
        var eventsPath = Path.Combine(taskDirectory, _layout.EventsFileName);
        if (!File.Exists(eventsPath))
        {
            return null;
        }

        GovernedTaskState? state = null;
        await foreach (var @event in ReadEventsAsync(
                           eventsPath, cancellationToken, allowConcurrentReplacement).ConfigureAwait(false))
        {
            ValidateEventEnvelope(taskId, @event, state?.Version ?? 0);
            try
            {
                state = _reducer.Apply(state, @event);
            }
            catch (GovernanceException exception)
            {
                throw new InvalidDataException(
                    $"Event '{@event.EventId}' violates domain transition rules: {exception.Message}",
                    exception);
            }
        }

        return state;
    }

    private async IAsyncEnumerable<LedgerEvent> ReadEventsAsync(
        string eventsPath,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        bool allowConcurrentReplacement = false)
    {
        EnsureEventLogSize(eventsPath);
        await using var stream = new FileStream(
            eventsPath,
            FileMode.Open,
            FileAccess.Read,
            allowConcurrentReplacement ? FileShare.ReadWrite | FileShare.Delete : FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Utf8WithoutBom, detectEncodingFromByteOrderMarks: true);

        var lineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (lineNumber > _maximumEventsPerTask)
            {
                throw new InvalidDataException(
                    $"Event log '{eventsPath}' exceeds the limit of {_maximumEventsPerTask} events. Archive it " +
                    "and open a successor that depends on the claims this one validated; there is no compaction " +
                    "path, and a task id is embedded in every one of its events so the log cannot be rewritten.");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                throw new InvalidDataException($"Blank event at line {lineNumber} in '{eventsPath}'.");
            }

            LedgerEvent? @event;
            try
            {
                @event = JsonSerializer.Deserialize<LedgerEvent>(line, _eventJson);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    $"Invalid event JSON at line {lineNumber} in '{eventsPath}'.", exception);
            }

            yield return @event ?? throw new InvalidDataException(
                $"Null event at line {lineNumber} in '{eventsPath}'.");
        }
    }

    private async Task AppendEventsAsync(
        string taskDirectory,
        IReadOnlyList<LedgerEvent> events,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        var payload = new StringBuilder();
        foreach (var @event in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            payload.Append(JsonSerializer.Serialize(@event, _eventJson)).Append('\n');
        }

        var eventsPath = Path.Combine(taskDirectory, _layout.EventsFileName);
        var bytes = Utf8WithoutBom.GetBytes(payload.ToString());
        var existingLength = File.Exists(eventsPath) ? new FileInfo(eventsPath).Length : 0;
        if (existingLength + bytes.LongLength > _maximumEventLogBytes)
        {
            throw new GovernanceException(
                $"Task event log would exceed the limit of {_maximumEventLogBytes} bytes. Archive it and open a " +
                "successor that depends on the claims this one validated; there is no compaction path, and a " +
                "task id is embedded in every one of its events so the log cannot be rewritten.");
        }

        var temporaryPath = $"{eventsPath}.{Guid.NewGuid():N}.append";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                if (File.Exists(eventsPath))
                {
                    await using var current = new FileStream(
                        eventsPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                        bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await current.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
                }

                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, eventsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task<bool> MaterializedStateIsCurrentAsync(
        string taskDirectory,
        GovernedTaskState replayedState,
        CancellationToken cancellationToken)
    {
        var statePath = Path.Combine(taskDirectory, _layout.StateFileName);
        if (!File.Exists(statePath))
        {
            return false;
        }

        try
        {
            var materializedJson = await File.ReadAllTextAsync(
                statePath, Utf8WithoutBom, cancellationToken).ConfigureAwait(false);
            var expectedJson = JsonSerializer.Serialize(replayedState, _stateJson) + "\n";
            return string.Equals(materializedJson, expectedJson, StringComparison.Ordinal);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    // A truncated event log still replays: a prefix of a valid history is a valid history. The
    // only local witness that history was lost is the materialised state, which records the
    // version reached by the last committed command. Healing silently into a shorter log would
    // erase the evidence, so a replay that lands behind it fails closed instead.
    private async Task EnsureEventLogHasNotLostHistoryAsync(
        string taskDirectory,
        GovernedTaskState replayedState,
        CancellationToken cancellationToken)
    {
        var statePath = Path.Combine(taskDirectory, _layout.StateFileName);
        if (!File.Exists(statePath))
        {
            return;
        }

        string materializedJson;
        try
        {
            materializedJson = await File.ReadAllTextAsync(statePath, Utf8WithoutBom, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
            return;
        }

        long materializedVersion;
        try
        {
            using var document = JsonDocument.Parse(materializedJson);
            if (!document.RootElement.TryGetProperty("version", out var version) ||
                !version.TryGetInt64(out materializedVersion))
            {
                return;
            }
        }
        catch (JsonException)
        {
            return;
        }

        if (replayedState.Version < materializedVersion)
        {
            throw new InvalidDataException(
                $"The event log for '{replayedState.TaskId}' replays to version {replayedState.Version}, " +
                $"behind the version {materializedVersion} recorded in '{statePath}'. History has been lost; " +
                "restore the event log rather than letting the projection heal into it.");
        }
    }

    private Task WriteMaterializedStateAsync(
        string taskDirectory,
        GovernedTaskState state,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(state, _stateJson) + "\n";
        return MarkdownTaskProjectionWriter.WriteAtomicAsync(
            Path.Combine(taskDirectory, _layout.StateFileName), json, cancellationToken);
    }

    private async Task TryRepairDerivedStateAsync(string taskDirectory, GovernedTaskState state)
    {
        try
        {
            // The event-log rename above is the commit point. Derived views must not
            // turn that committed command into an ambiguous failure for the caller.
            await WriteMaterializedStateAsync(taskDirectory, state, CancellationToken.None).ConfigureAwait(false);
            await _projectionWriter.WriteAsync(taskDirectory, state, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // GetStateAsync deterministically rebuilds every derived artifact.
        }
    }

    private static void ValidateOutcome(TaskId taskId, GovernedTaskState? currentState, CommandOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.State.TaskId != taskId)
        {
            throw new InvalidOperationException("The command outcome belongs to a different task.");
        }

        if (outcome.Events.Count == 0)
        {
            throw new InvalidOperationException("A successful command must emit at least one event.");
        }

        var priorEventCount = currentState?.Version ?? 0;
        foreach (var @event in outcome.Events)
        {
            ValidateEventEnvelope(taskId, @event, priorEventCount);
            priorEventCount++;
        }

        var expectedVersion = (currentState?.Version ?? 0) + outcome.Events.Count;
        if (outcome.State.Version != expectedVersion)
        {
            throw new InvalidOperationException(
                $"The command outcome version {outcome.State.Version} does not match expected version {expectedVersion}.");
        }
    }

    private static void EnsureTaskMatches(TaskId taskId, LedgerEvent @event)
    {
        if (@event.TaskId != taskId)
        {
            throw new InvalidDataException(
                $"Event '{@event.EventId}' belongs to task '{@event.TaskId}', not '{taskId}'.");
        }
    }

    private static void ValidateEventEnvelope(
        TaskId taskId,
        LedgerEvent @event,
        long zeroBasedIndex)
    {
        EnsureTaskMatches(taskId, @event);
        var expected = $"{taskId.Value}:{zeroBasedIndex + 1:D10}";
        if (!string.Equals(@event.EventId.Value, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Event sequence is invalid at position {zeroBasedIndex + 1}: expected '{expected}', found '{@event.EventId}'.");
        }

        if (string.IsNullOrWhiteSpace(@event.ActorId.Value) ||
            string.IsNullOrWhiteSpace(@event.CorrelationId) ||
            @event.RecordedAt == default)
        {
            throw new InvalidDataException($"Event '{@event.EventId}' has invalid provenance metadata.");
        }

        if (@event.CausationId is { } causationId && !IsPriorEventId(taskId, causationId, zeroBasedIndex))
        {
            throw new InvalidDataException(
                $"Event '{@event.EventId}' references unknown or non-prior cause '{causationId}'.");
        }
    }

    private static bool IsPriorEventId(TaskId taskId, EventId eventId, long priorEventCount)
    {
        var prefix = $"{taskId.Value}:";
        if (!eventId.Value.StartsWith(prefix, StringComparison.Ordinal) ||
            !long.TryParse(eventId.Value.AsSpan(prefix.Length), out var sequence) ||
            sequence < 1 || sequence > priorEventCount)
        {
            return false;
        }

        return string.Equals(eventId.Value, $"{taskId.Value}:{sequence:D10}", StringComparison.Ordinal);
    }

    private void EnsureEventLogSize(string eventsPath)
    {
        var length = new FileInfo(eventsPath).Length;
        if (length > _maximumEventLogBytes)
        {
            throw new InvalidDataException(
                $"Event log '{eventsPath}' exceeds the limit of {_maximumEventLogBytes} bytes. Archive it and " +
                "open a successor that depends on the claims this one validated; there is no compaction path, " +
                "and a task id is embedded in every one of its events so the log cannot be rewritten.");
        }
    }

    private static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException or StackOverflowException or AccessViolationException;
}
