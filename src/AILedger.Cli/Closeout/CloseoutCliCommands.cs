using System.Security.Cryptography;
using System.Text;
using AILedger.Cli.Routing;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Storage;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Closeout;

internal sealed class CloseoutCliCommands(CliCommandExecutor executor)
{
    public IEnumerable<CliCommandRegistration> Registrations()
    {
        yield return new CliCommandRegistration(
            ["closeout evidence"],
            CliCommandOptions.Set("root", "task", "actor"),
            isReadOnly: true,
            EvidenceAsync);
        yield return new CliCommandRegistration(
            ["closeout status"],
            CliCommandOptions.Set("root", "task", "actor", "index-database"),
            isReadOnly: true,
            StatusAsync);
    }

    private async Task EvidenceAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var taskId = Task(invocation.Input);
        _ = Actor(invocation.Input);
        var state = await CliCommandExecutor.RequireStateAsync(invocation, cancellationToken).ConfigureAwait(false);
        var history = new List<LedgerEvent>();
        await foreach (var @event in invocation.Service.GetHistoryAsync(taskId, cancellationToken).ConfigureAwait(false))
        {
            history.Add(@event);
        }

        var report = TaskCloseoutEvidence.Build(state, history);
        var taskDirectory = new TaskWorkspacePathResolver(invocation.LedgerRoot).Resolve(taskId);
        await executor.WriteJsonAsync(new
        {
            report.Task,
            report.Stage,
            report.Version,
            report.Completeness,
            report.AssuranceRevisions,
            report.WorkItems,
            report.Records,
            LooseReports = LooseReports(taskDirectory, report.AssuranceRevisions)
        }).ConfigureAwait(false);
    }

    private async Task StatusAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var taskId = Task(invocation.Input);
        _ = Actor(invocation.Input);
        var state = await CliCommandExecutor.RequireStateAsync(invocation, cancellationToken).ConfigureAwait(false);
        var eligibility = TaskCloseoutEligibility.Evaluate(state);
        var taskDirectory = new TaskWorkspacePathResolver(invocation.LedgerRoot).Resolve(taskId);

        await executor.WriteJsonAsync(new
        {
            eligibility.Task,
            eligibility.Stage,
            eligibility.Version,
            eligibility.Eligible,
            eligibility.Checks,
            eligibility.CloseoutSynthesisArtifactId,
            eligibility.WorkflowRetrospectiveArtifactId,
            LessonPublication = await LessonPublicationAsync(
                    invocation,
                    state,
                    eligibility.Checks.Single(check => check.Name == "lessonsMinted").Satisfied,
                    cancellationToken)
                .ConfigureAwait(false),
            CanonicalSources = CanonicalSources(taskDirectory),
            StandaloneIndex = StandaloneIndex(invocation.Input.Optional("index-database"))
        }).ConfigureAwait(false);
    }

    private static async Task<object> LessonPublicationAsync(
        CliCommandInvocation invocation,
        GovernedTaskState state,
        bool lessonMintingSatisfied,
        CancellationToken cancellationToken)
    {
        var minted = state.Lessons.Values
            .Where(lesson => lesson.SourceTaskId == state.TaskId)
            .Select(lesson => lesson.Id.Value)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var store = new FileLessonStore(invocation.LessonRoot);
        var published = (await store.ReadAsync(cancellationToken).ConfigureAwait(false))
            .Select(lesson => lesson.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        var unpublished = minted.Where(id => !published.Contains(id)).ToArray();
        return new
        {
            Store = store.LessonsPath,
            Minted = minted.Length,
            Published = minted.Length - unpublished.Length,
            Unpublished = unpublished,
            Satisfied = lessonMintingSatisfied && unpublished.Length == 0
        };
    }

    private static object CanonicalSources(string taskDirectory)
    {
        var layout = new TaskWorkspaceLayout();
        return new
        {
            Events = FileFact(Path.Combine(taskDirectory, layout.EventsFileName)),
            Refusals = FileFact(RefusalJournal.ResolvePath(taskDirectory))
        };
    }

    private static object FileFact(string path)
    {
        if (!File.Exists(path))
        {
            return new { Present = false, Length = (long?)null, Sha256 = (string?)null };
        }

        using var stream = File.OpenRead(path);
        return new
        {
            Present = true,
            Length = (long?)new FileInfo(path).Length,
            Sha256 = (string?)Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()
        };
    }

    private static object StandaloneIndex(string? database)
    {
        if (database is null)
        {
            return new
            {
                Configured = false,
                Present = (bool?)null,
                Path = (string?)null,
                Detail = "No standalone index database was configured; its state was not checked."
            };
        }

        var path = Path.GetFullPath(database);
        var present = File.Exists(path);
        return new
        {
            Configured = true,
            Present = (bool?)present,
            Path = (string?)path,
            Detail = present
                ? "The configured index database exists; this command does not judge whether it is current."
                : "The configured index database does not exist."
        };
    }

    private static IReadOnlyList<object> LooseReports(
        string taskDirectory,
        IReadOnlyList<CloseoutAssuranceRevision> revisions)
    {
        var reviewDirectory = Path.Combine(taskDirectory, "review");
        if (!Directory.Exists(reviewDirectory))
        {
            return [];
        }

        var filed = revisions
            .GroupBy(revision => revision.ContentSha256, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(revision => revision.ArtifactId).ToArray(),
                StringComparer.Ordinal);

        return Directory.EnumerateFiles(reviewDirectory)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path =>
            {
                var bytes = File.ReadAllBytes(path);
                var text = File.ReadAllText(path);
                var exact = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                var trimmed = Sha256OfText(text.Trim());
                string matchKind;
                string[] matches;
                if (filed.TryGetValue(exact, out var exactMatches))
                {
                    matchKind = "exact";
                    matches = exactMatches;
                }
                else if (filed.TryGetValue(trimmed, out var trimmedMatches))
                {
                    matchKind = "trimmed";
                    matches = trimmedMatches;
                }
                else
                {
                    matchKind = "none";
                    matches = [];
                }
                return (object)new
                {
                    Path = "review/" + Path.GetFileName(path),
                    Length = bytes.LongLength,
                    Sha256 = exact,
                    MatchesFiledArtifacts = matches,
                    Matched = matchKind == "exact",
                    MatchKind = matchKind
                };
            })
            .ToArray();
    }

    private static string Sha256OfText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
