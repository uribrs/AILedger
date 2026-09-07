using System.Text;
using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Storage;

/// <summary>
/// A lesson is the one record that is worth more to the next task than to the one that earned it,
/// and the next task is often in another repository. Every other record in this kernel belongs to
/// exactly one task root, so a store that lessons cross repositories with has to live outside all
/// of them.
/// </summary>
public interface ILessonStore
{
    /// <summary>Every lesson published by any repository, in no particular order.</summary>
    Task<IReadOnlyList<Lesson>> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds lessons the store does not already hold. Publishing the same lesson twice is a no-op,
    /// so the caller may republish freely to repair a publication that failed.
    /// </summary>
    Task PublishAsync(IReadOnlyList<Lesson> lessons, CancellationToken cancellationToken);
}

/// <summary>
/// A newline-delimited JSON file under a root that belongs to no task. The task event log stays
/// authoritative for its own lessons; this file is the index that carries them across roots, and
/// it is rebuilt for the local root on every task opening.
/// </summary>
public sealed class FileLessonStore : ILessonStore
{
    public const string LessonsFileName = "lessons.jsonl";
    public const string LockFileName = ".lessons.lock";
    public const long DefaultMaximumStoreBytes = 16 * 1024 * 1024;

    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly string _root;
    private readonly long _maximumStoreBytes;
    private readonly TaskMutationLock _mutationLock = new();
    private readonly JsonSerializerOptions _json = LedgerJson.CreateOptions();

    public FileLessonStore(string lessonRoot, long maximumStoreBytes = DefaultMaximumStoreBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lessonRoot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStoreBytes);
        _root = Path.GetFullPath(lessonRoot);
        _maximumStoreBytes = maximumStoreBytes;
    }

    public string Root => _root;

    public string LessonsPath => Path.Combine(_root, LessonsFileName);

    /// <summary>
    /// The per-user location the store falls back to. It is a sibling of the default task root
    /// rather than a directory inside it, because a store inside one root is not a store that
    /// crosses roots.
    /// </summary>
    public static string DefaultRoot()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.Combine(localData, "AILedger", "lessons");
    }

    public async Task<IReadOnlyList<Lesson>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(LessonsPath))
        {
            return [];
        }

        await using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        return await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishAsync(IReadOnlyList<Lesson> lessons, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lessons);
        if (lessons.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(_root);
        await using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);

        var existing = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
        var held = existing.Select(KeyOf).ToHashSet();
        var payload = new StringBuilder();
        foreach (var lesson in lessons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(lesson);
            // The first publication of an identifier wins. A lesson is derived from an archived
            // task, and an archived history never changes, so a second row under the same key
            // carries no correction — it is the same lesson arriving by the repair path.
            if (!held.Add(KeyOf(lesson)))
            {
                continue;
            }

            payload.Append(JsonSerializer.Serialize(lesson, _json)).Append('\n');
        }

        if (payload.Length == 0)
        {
            return;
        }

        await AppendAsync(Utf8WithoutBom.GetBytes(payload.ToString()), cancellationToken).ConfigureAwait(false);
    }

    // A repository is part of the key, not just of the row. Two repositories can hold a task with
    // the same id, and a lesson id is derived from the task id, so keying on the identifier alone
    // would silently drop one repository's lesson at publication time.
    private static (string Repo, string Id) KeyOf(Lesson lesson) =>
        (lesson.Repo ?? string.Empty, lesson.Id.Value);

    private Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        return _mutationLock.AcquireAsync(Path.Combine(_root, LockFileName), cancellationToken);
    }

    private async Task<IReadOnlyList<Lesson>> ReadUnlockedAsync(CancellationToken cancellationToken)
    {
        var path = LessonsPath;
        if (!File.Exists(path))
        {
            return [];
        }

        EnsureStoreSize(path);
        var lessons = new List<Lesson>();
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Utf8WithoutBom, detectEncodingFromByteOrderMarks: true);

        var lineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                throw new InvalidDataException($"Blank lesson at line {lineNumber} in '{path}'.");
            }

            Lesson? lesson;
            try
            {
                lesson = JsonSerializer.Deserialize<Lesson>(line, _json);
            }
            catch (JsonException exception)
            {
                // A damaged row is not skipped. The store is the only path a lesson from another
                // repository travels, so dropping one is the exact silence this store exists to
                // remove; the repair is to restore the file, not to read past the damage.
                throw new InvalidDataException(
                    $"Invalid lesson JSON at line {lineNumber} in '{path}'.", exception);
            }

            lessons.Add(lesson ?? throw new InvalidDataException(
                $"Null lesson at line {lineNumber} in '{path}'."));
        }

        return lessons;
    }

    private async Task AppendAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        var path = LessonsPath;
        var existingLength = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (existingLength + bytes.LongLength > _maximumStoreBytes)
        {
            throw new GovernanceException(
                $"The lesson store would exceed the limit of {_maximumStoreBytes} bytes; prune or migrate it before continuing.");
        }

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.append";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             bufferSize: 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                if (File.Exists(path))
                {
                    await using var current = new FileStream(
                        path, FileMode.Open, FileAccess.Read, FileShare.Read,
                        bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await current.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
                }

                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void EnsureStoreSize(string path)
    {
        if (new FileInfo(path).Length > _maximumStoreBytes)
        {
            throw new InvalidDataException(
                $"The lesson store '{path}' exceeds the limit of {_maximumStoreBytes} bytes.");
        }
    }
}
