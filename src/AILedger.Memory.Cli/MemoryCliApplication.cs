using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AILedger.Memory.Application;
using AILedger.Memory.Contracts;
using AILedger.Memory.Embeddings;
using AILedger.Memory.Evaluation;
using AILedger.Memory.Ingestion;
using AILedger.Memory.Storage;

internal sealed class MemoryCliApplication
{
    private const string EventsFileName = "events.jsonl";
    private const string RefusalsFileName = "refusals.jsonl";
    private const string LessonsFileName = "lessons.jsonl";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly IReadOnlySet<string> FlagOptions = Options("confirm-local", "include-superseded");
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedOptions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["rebuild"] = Options(
                "root", "database", "lesson-root", "embedding", "dimensions", "model", "endpoint",
                "keep-alive", "confirm-local"),
            ["update"] = Options(
                "root", "database", "lesson-root", "embedding", "dimensions", "model", "endpoint",
                "keep-alive", "confirm-local"),
            ["search"] = Options(
                "root", "database", "embedding", "dimensions", "model", "endpoint", "keep-alive",
                "confirm-local", "kind", "repo", "task", "limit", "include-superseded"),
            ["inspect"] = Options("root", "database"),
            ["stats"] = Options("root", "database"),
            ["evaluate"] = Options(
                "root", "database", "lesson-root", "cases", "mode", "embedding", "dimensions",
                "model", "endpoint", "keep-alive", "confirm-local"),
            ["drop"] = Options("root", "database")
        };

    private const string HelpText = """
        Usage:
          ailedger-memory rebuild [--root PATH] [--database PATH] [embedding options]
          ailedger-memory update  [--root PATH] [--database PATH] [embedding options]
          ailedger-memory search  QUERY [--root PATH] [--database PATH] [--kind KIND] [--repo REPO] [--task TASK] [--limit N]
          ailedger-memory inspect DOCUMENT-ID [--root PATH] [--database PATH]
          ailedger-memory stats [--root PATH] [--database PATH]
          ailedger-memory evaluate --cases PATH [--mode baseline|lexical|hybrid]
          ailedger-memory drop [--root PATH] [--database PATH]

        Index source options:
          --root PATH          Canonical task root. Defaults to the nearest .ailedger/tasks root.
          --lesson-root PATH   Published lesson-store directory. Defaults beside the ledger home.

        Embedding options (default: none):
          --embedding none|deterministic|ollama
          --dimensions N       Deterministic vector dimensions (default: 64).
          --model NAME         Required with ollama.
          --endpoint URI       Loopback Ollama endpoint (default: http://127.0.0.1:11434/).
          --keep-alive VALUE   Optional Ollama keep-alive value.
          --confirm-local      Confirms explicit local-model processing; required with ollama.

        Search options:
          --kind KIND          Repeatable document-kind filter.
          --include-superseded Include superseded or deleted records.

        All output is JSON. This executable is manual and standalone; it never changes canonical data.
        """;

    private readonly TextWriter _output;
    private readonly TextWriter _error;

    private MemoryCliApplication(TextWriter output, TextWriter error)
    {
        _output = output;
        _error = error;
    }

    public static MemoryCliApplication CreateDefault() => new(Console.Out, Console.Error);

    public async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            if (arguments.Count == 0 ||
                arguments.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
                arguments.Contains("-h", StringComparer.OrdinalIgnoreCase) ||
                arguments is ["help"])
            {
                await _output.WriteLineAsync(HelpText).ConfigureAwait(false);
                return 0;
            }

            var input = CommandInput.Parse(arguments);
            ValidateOptions(input);
            await DispatchAsync(input, cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _error.WriteLineAsync("Cancelled.").ConfigureAwait(false);
            return 130;
        }
        catch (MemoryCliUsageException exception)
        {
            await _error.WriteLineAsync($"error: {exception.Message}").ConfigureAwait(false);
            return 2;
        }
        catch (Exception exception)
        {
            await _error.WriteLineAsync($"error: {SafeMessage(exception)}").ConfigureAwait(false);
            return 1;
        }
    }

    private async Task DispatchAsync(CommandInput input, CancellationToken cancellationToken)
    {
        switch (input.Command)
        {
            case "rebuild":
                RequirePositionals(input, 0);
                await RunIndexAsync(input, rebuild: true, cancellationToken).ConfigureAwait(false);
                break;
            case "update":
                RequirePositionals(input, 0);
                await RunIndexAsync(input, rebuild: false, cancellationToken).ConfigureAwait(false);
                break;
            case "search":
                await SearchAsync(input, cancellationToken).ConfigureAwait(false);
                break;
            case "inspect":
                RequirePositionals(input, 1);
                await WithApplicationAsync(input, async (application, token) =>
                {
                    var document = await application.InspectAsync(input.Positionals[0], token).ConfigureAwait(false);
                    if (document is null)
                    {
                        throw new InvalidDataException($"Memory document '{input.Positionals[0]}' was not found.");
                    }

                    await WriteJsonAsync(document).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
                break;
            case "stats":
                RequirePositionals(input, 0);
                var statisticsDatabasePath = DatabasePath(input);
                await WithApplicationAsync(input, async (application, token) =>
                {
                    try
                    {
                        var statistics = await application.GetStatisticsAsync(token).ConfigureAwait(false);
                        var output = JsonSerializer.SerializeToNode(statistics, JsonOptions)?.AsObject()
                            ?? new JsonObject();
                        output["database"] = statisticsDatabasePath;
                        await WriteJsonAsync(output).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidDataException(
                            $"Could not read memory statistics from database '{statisticsDatabasePath}': {SafeMessage(exception)}",
                            exception);
                    }
                }, cancellationToken).ConfigureAwait(false);
                break;
            case "evaluate":
                await EvaluateAsync(input, cancellationToken).ConfigureAwait(false);
                break;
            case "drop":
                RequirePositionals(input, 0);
                await WithApplicationAsync(input, async (application, token) =>
                {
                    await application.DropAsync(token).ConfigureAwait(false);
                    await WriteJsonAsync(new { dropped = true, database = DatabasePath(input) })
                        .ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new MemoryCliUsageException($"Unknown command '{input.Command}'. Run with --help for usage.");
        }
    }

    private async Task RunIndexAsync(
        CommandInput input,
        bool rebuild,
        CancellationToken cancellationToken)
    {
        var sources = DiscoverSources(input);
        await WithApplicationAsync(input, async (application, token) =>
        {
            var request = new MemoryIndexRequest
            {
                Sources = sources,
                GenerateEmbeddings = !EmbeddingMode(input).Equals("none", StringComparison.OrdinalIgnoreCase)
            };
            var result = rebuild
                ? await application.RebuildAsync(request, token).ConfigureAwait(false)
                : await application.UpdateAsync(request, token).ConfigureAwait(false);
            await WriteJsonAsync(result).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task SearchAsync(CommandInput input, CancellationToken cancellationToken)
    {
        if (input.Positionals.Count == 0)
        {
            throw new MemoryCliUsageException("search requires a query.");
        }

        var queryText = string.Join(' ', input.Positionals);
        var kinds = input.Many("kind")
            .Select(value => ParseEnum<MemoryDocumentKind>(value, "kind"))
            .ToHashSet();
        var limit = PositiveInt(input.Optional("limit"), "limit", 10);
        await WithApplicationAsync(input, async (application, token) =>
        {
            var response = await application.SearchAsync(new MemoryQuery
            {
                Text = queryText,
                Limit = limit,
                Repository = input.Optional("repo"),
                TaskId = input.Optional("task"),
                Kinds = kinds.Count == 0 ? null : kinds,
                IncludeSuperseded = input.Flag("include-superseded"),
                UseEmbeddings = !EmbeddingMode(input).Equals("none", StringComparison.OrdinalIgnoreCase)
            }, token).ConfigureAwait(false);
            await WriteJsonAsync(response).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task EvaluateAsync(CommandInput input, CancellationToken cancellationToken)
    {
        RequirePositionals(input, 0);
        var casesPath = input.Required("cases");
        var mode = ParseEvaluationMode(input.Optional("mode") ?? "lexical");
        var embeddingMode = EmbeddingMode(input);
        if (mode == EvaluationMode.Hybrid && embeddingMode.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            throw new MemoryCliUsageException(
                "Hybrid evaluation requires --embedding deterministic or --embedding ollama.");
        }

        if (mode != EvaluationMode.Hybrid && !embeddingMode.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            throw new MemoryCliUsageException("Embedding configuration is only used by hybrid evaluation.");
        }

        var corpus = await EvaluationCorpusLoader.LoadAsync(casesPath, cancellationToken).ConfigureAwait(false);
        var baselineSources = mode == EvaluationMode.TagRecencyBaseline
            ? DiscoverSources(input)
            : [];
        await WithApplicationAsync(input, async (application, token) =>
        {
            var report = await application.EvaluateAsync(new MemoryEvaluationRequest
            {
                Corpus = corpus,
                Mode = mode,
                BaselineSources = baselineSources
            }, token).ConfigureAwait(false);
            await WriteJsonAsync(report).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task WithApplicationAsync(
        CommandInput input,
        Func<MemoryIndexApplication, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var generator = CreateEmbeddingGenerator(input);
        try
        {
            var application = new MemoryIndexApplication(
                new SqliteMemoryProjectionStoreFactory(DatabasePath(input)),
                embeddingGenerator: generator);
            await action(application, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            (generator as IDisposable)?.Dispose();
        }
    }

    private IEmbeddingGenerator? CreateEmbeddingGenerator(CommandInput input)
    {
        var mode = EmbeddingMode(input);
        switch (mode.ToLowerInvariant())
        {
            case "none":
                RejectEmbeddingDetailsWithoutMode(input);
                return null;
            case "deterministic":
                RejectOptions(input, "model", "endpoint", "keep-alive", "confirm-local");
                return new DeterministicIdentityResolvingGenerator(
                    PositiveInt(input.Optional("dimensions"), "dimensions", 64));
            case "ollama":
                RejectOptions(input, "dimensions");
                if (!input.Flag("confirm-local"))
                {
                    throw new MemoryCliUsageException(
                        "Ollama processing requires --confirm-local to acknowledge that command input or indexed canonical content will be sent to the configured local model.");
                }

                var endpointText = input.Optional("endpoint") ?? "http://127.0.0.1:11434/";
                if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint))
                {
                    throw new MemoryCliUsageException("--endpoint must be an absolute URI.");
                }

                var model = input.Required("model");
                var generator = new OllamaEmbeddingGenerator(new OllamaEmbeddingOptions
                {
                    Endpoint = endpoint,
                    Model = model,
                    KeepAlive = input.Optional("keep-alive")
                });
                _error.WriteLine($"Using loopback Ollama model '{model}' at '{endpoint}'.");
                return generator;
            default:
                throw new MemoryCliUsageException(
                    $"Unknown embedding mode '{mode}'; expected none, deterministic, or ollama.");
        }
    }

    private static IReadOnlyList<SourceDescriptor> DiscoverSources(CommandInput input)
    {
        var root = LedgerRoot(input);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Canonical task root '{root}' does not exist.");
        }

        var rootIdentity = NormalizeIdentity(root);
        var repository = RepositoryName(root);
        var sources = new List<SourceDescriptor>();
        foreach (var taskDirectory in Directory.EnumerateDirectories(root).OrderBy(path => path, PathComparer))
        {
            var taskId = Path.GetFileName(taskDirectory);
            var eventsPath = Path.Combine(taskDirectory, EventsFileName);
            if (File.Exists(eventsPath))
            {
                sources.Add(CanonicalSources.EventHistory(rootIdentity, taskId, eventsPath, repository));
            }

            var refusalsPath = Path.Combine(taskDirectory, RefusalsFileName);
            if (File.Exists(refusalsPath))
            {
                sources.Add(CanonicalSources.RefusalJournal(rootIdentity, taskId, refusalsPath, repository));
            }
        }

        var lessonRoot = input.Optional("lesson-root") is { } explicitLessonRoot
            ? Path.GetFullPath(explicitLessonRoot)
            : DefaultLessonRoot(root);
        var lessonsPath = Path.Combine(lessonRoot, LessonsFileName);
        if (File.Exists(lessonsPath))
        {
            sources.Add(CanonicalSources.PublishedLessons(NormalizeIdentity(lessonRoot), lessonsPath));
        }

        if (sources.Count == 0)
        {
            throw new InvalidDataException(
                $"No canonical event histories, refusal journals, or published lessons were found under '{root}'.");
        }

        return sources;
    }

    private static string LedgerRoot(CommandInput input)
    {
        if (input.Optional("root") is { } explicitRoot)
        {
            return Path.GetFullPath(explicitRoot);
        }

        if (DiscoverLedgerHome() is { } home)
        {
            return Path.Combine(home, ".ailedger", "tasks");
        }

        return Path.Combine(LocalDataRoot(), "AILedger", "tasks");
    }

    private static string DefaultLessonRoot(string taskRoot)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(taskRoot));
        if (directory.Parent is { Name: ".ailedger", Parent: { } ledgerHome })
        {
            return Path.Combine(ledgerHome.FullName, "lessons");
        }

        return Path.Combine(LocalDataRoot(), "AILedger", "lessons");
    }

    private static string DatabasePath(CommandInput input)
    {
        if (input.Optional("database") is { } explicitDatabase)
        {
            return Path.GetFullPath(explicitDatabase);
        }

        var rootIdentity = CanonicalizeDirectoryIdentity(LedgerRoot(input));
        if (UsesCaseInsensitiveNames(rootIdentity))
        {
            rootIdentity = rootIdentity.ToUpperInvariant();
        }

        var rootDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rootIdentity)))
            .ToLowerInvariant();
        return Path.Combine(LocalDataRoot(), "AILedger", "memory", rootDigest, "memory.sqlite");
    }

    private static string LocalDataRoot()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localData)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
            : localData;
    }

    private static string? DiscoverLedgerHome()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".ailedger")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static string? RepositoryName(string taskRoot)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(taskRoot));
        return directory.Parent is { Name: ".ailedger", Parent: { } ledgerHome }
            ? ledgerHome.Name
            : null;
    }

    private async Task WriteJsonAsync<T>(T value) =>
        await _output.WriteLineAsync(JsonSerializer.Serialize(value, JsonOptions)).ConfigureAwait(false);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static EvaluationMode ParseEvaluationMode(string value) => value.ToLowerInvariant() switch
    {
        "baseline" or "tag-recency" or "tag-recency-baseline" => EvaluationMode.TagRecencyBaseline,
        "lexical" => EvaluationMode.Lexical,
        "hybrid" => EvaluationMode.Hybrid,
        _ => throw new MemoryCliUsageException(
            $"Unknown evaluation mode '{value}'; expected baseline, lexical, or hybrid.")
    };

    private static T ParseEnum<T>(string value, string option) where T : struct, Enum
    {
        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<T>())
        {
            var candidateName = candidate.ToString().Replace("_", string.Empty, StringComparison.Ordinal);
            if (string.Equals(candidateName, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new MemoryCliUsageException($"Unknown --{option} value '{value}'.");
    }

    private static int PositiveInt(string? value, string option, int defaultValue)
    {
        if (value is null)
        {
            return defaultValue;
        }

        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new MemoryCliUsageException($"--{option} must be a positive integer.");
    }

    private static string EmbeddingMode(CommandInput input) => input.Optional("embedding") ?? "none";

    private static void ValidateOptions(CommandInput input)
    {
        if (!AllowedOptions.TryGetValue(input.Command, out var allowed))
        {
            return;
        }

        var unknown = input.OptionNames.FirstOrDefault(option => !allowed.Contains(option));
        if (unknown is not null)
        {
            throw new MemoryCliUsageException($"Option '--{unknown}' is not valid for {input.Command}.");
        }
    }

    private static void RejectEmbeddingDetailsWithoutMode(CommandInput input) =>
        RejectOptions(input, "dimensions", "model", "endpoint", "keep-alive", "confirm-local");

    private static void RejectOptions(CommandInput input, params string[] names)
    {
        var rejected = names.FirstOrDefault(input.HasOption);
        if (rejected is not null)
        {
            throw new MemoryCliUsageException(
                $"--{rejected} is not valid with --embedding {EmbeddingMode(input)}.");
        }
    }

    private static void RequirePositionals(CommandInput input, int count)
    {
        if (input.Positionals.Count != count)
        {
            throw new MemoryCliUsageException(
                count == 0
                    ? $"{input.Command} does not accept positional arguments."
                    : $"{input.Command} requires exactly {count} positional argument{(count == 1 ? string.Empty : "s")}.");
        }
    }

    private static IReadOnlySet<string> Options(params string[] values) =>
        new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);

    private static string NormalizeIdentity(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string CanonicalizeDirectoryIdentity(string path)
    {
        var directory = new DirectoryInfo(NormalizeIdentity(path));
        if (directory.Parent is null)
        {
            return directory.FullName;
        }

        var canonicalParent = CanonicalizeDirectoryIdentity(directory.Parent.FullName);
        var candidate = new DirectoryInfo(Path.Combine(canonicalParent, directory.Name));
        if (!candidate.Exists)
        {
            return NormalizeIdentity(candidate.FullName);
        }

        candidate = ResolveStoredDirectoryEntry(candidate);
        var target = candidate.ResolveLinkTarget(returnFinalTarget: true);
        return target is null
            ? NormalizeIdentity(candidate.FullName)
            : CanonicalizeDirectoryIdentity(target.FullName);
    }

    private static DirectoryInfo ResolveStoredDirectoryEntry(DirectoryInfo directory)
    {
        if (directory.Parent is null)
        {
            return directory;
        }

        var storedEntry = directory.Parent.EnumerateDirectories()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, directory.Name, StringComparison.Ordinal))
            ?? directory.Parent.EnumerateDirectories()
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, directory.Name, StringComparison.OrdinalIgnoreCase));
        return storedEntry ?? directory;
    }

    private static bool UsesCaseInsensitiveNames(string path)
    {
        for (var directory = NearestExistingDirectory(path);
             directory?.Parent is not null;
             directory = directory.Parent)
        {
            var alternateName = ToggleFirstLetterCase(directory.Name);
            if (alternateName == directory.Name)
            {
                continue;
            }

            var alternatePath = Path.Combine(directory.Parent.FullName, alternateName);
            if (!Directory.Exists(alternatePath))
            {
                return false;
            }

            var alternateIsStoredEntry = directory.Parent.EnumerateDirectories()
                .Any(candidate => string.Equals(candidate.Name, alternateName, StringComparison.Ordinal));
            return !alternateIsStoredEntry;
        }

        return false;
    }

    private static DirectoryInfo? NearestExistingDirectory(string path)
    {
        for (var directory = new DirectoryInfo(path);
             directory is not null;
             directory = directory.Parent)
        {
            if (directory.Exists)
            {
                return directory;
            }
        }

        return null;
    }

    private static string ToggleFirstLetterCase(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsLetter(value[index]))
            {
                continue;
            }

            var replacement = char.IsUpper(value[index])
                ? char.ToLowerInvariant(value[index])
                : char.ToUpperInvariant(value[index]);
            var characters = value.ToCharArray();
            characters[index] = replacement;
            return new string(characters);
        }

        return value;
    }

    private static string SafeMessage(Exception exception)
    {
        var message = exception.Message;
        return message.Length <= 2_000 ? message : message[..2_000];
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed class CommandInput
    {
        private readonly Dictionary<string, List<string?>> _options;

        private CommandInput(
            string command,
            IReadOnlyList<string> positionals,
            Dictionary<string, List<string?>> options)
        {
            Command = command;
            Positionals = positionals;
            _options = options;
        }

        public string Command { get; }
        public IReadOnlyList<string> Positionals { get; }
        public IEnumerable<string> OptionNames => _options.Keys;

        public bool HasOption(string name) => _options.ContainsKey(name);

        public string? Optional(string name)
        {
            if (!_options.TryGetValue(name, out var values))
            {
                return null;
            }

            if (values.Count != 1 || values[0] is null)
            {
                throw new MemoryCliUsageException($"--{name} requires exactly one value.");
            }

            return values[0];
        }

        public string Required(string name) => Optional(name) is { } value && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new MemoryCliUsageException($"--{name} is required.");

        public IReadOnlyList<string> Many(string name)
        {
            if (!_options.TryGetValue(name, out var values))
            {
                return [];
            }

            if (values.Any(value => value is null))
            {
                throw new MemoryCliUsageException($"--{name} requires a value.");
            }

            return values.Select(value => value!).ToArray();
        }

        public bool Flag(string name)
        {
            if (!_options.TryGetValue(name, out var values))
            {
                return false;
            }

            if (values.Count != 1 || values[0] is not null)
            {
                throw new MemoryCliUsageException($"--{name} is a flag and does not accept a value.");
            }

            return true;
        }

        public static CommandInput Parse(IReadOnlyList<string> arguments)
        {
            if (arguments.Count == 0 || arguments[0].StartsWith("-", StringComparison.Ordinal))
            {
                throw new MemoryCliUsageException("A command is required. Run with --help for usage.");
            }

            var command = arguments[0].ToLowerInvariant();
            var positionals = new List<string>();
            var options = new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);
            var optionsEnded = false;
            for (var index = 1; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                if (!optionsEnded && argument == "--")
                {
                    optionsEnded = true;
                    continue;
                }

                if (optionsEnded || !argument.StartsWith("--", StringComparison.Ordinal))
                {
                    positionals.Add(argument);
                    continue;
                }

                var option = argument[2..];
                string? value = null;
                var equals = option.IndexOf('=', StringComparison.Ordinal);
                if (equals >= 0)
                {
                    value = option[(equals + 1)..];
                    option = option[..equals];
                }
                else if (!FlagOptions.Contains(option) &&
                         index + 1 < arguments.Count &&
                         !arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = arguments[++index];
                }

                if (string.IsNullOrWhiteSpace(option))
                {
                    throw new MemoryCliUsageException("An option name cannot be empty.");
                }

                if (!options.TryGetValue(option, out var values))
                {
                    values = [];
                    options.Add(option, values);
                }

                values.Add(value);
            }

            return new CommandInput(command, positionals, options);
        }
    }

    private sealed class DeterministicIdentityResolvingGenerator :
        IEmbeddingGenerator,
        IEmbeddingIdentityResolver
    {
        private readonly DeterministicEmbeddingGenerator _inner;

        public DeterministicIdentityResolvingGenerator(int dimensions) =>
            _inner = new DeterministicEmbeddingGenerator(dimensions);

        public string Provider => _inner.Provider;
        public string Model => _inner.Model;

        public Task<EmbeddingBatch> GenerateAsync(
            EmbeddingRequest request,
            CancellationToken cancellationToken = default) =>
            _inner.GenerateAsync(request, cancellationToken);

        public Task<EmbeddingIdentity> ResolveIdentityAsync(
            int dimensions,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dimensions != _inner.Identity.Dimensions)
            {
                throw new InvalidOperationException(
                    "The deterministic embedding dimensions do not match the active index identity; rebuild before reusing vectors.");
            }

            return Task.FromResult(_inner.Identity);
        }
    }

    private sealed class MemoryCliUsageException(string message) : Exception(message);
}
