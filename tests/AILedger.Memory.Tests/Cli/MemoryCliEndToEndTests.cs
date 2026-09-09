using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Memory.Tests.Support;
using AILedger.Storage;
using Microsoft.Data.Sqlite;

namespace AILedger.Memory.Tests.Cli;

public sealed class MemoryCliEndToEndTests
{
    [Fact]
    public async Task CommandsAreExplicitAndRebuildUpdateSearchStatsDropRoundTrip()
    {
        using var directory = new TemporaryDirectory();
        var taskRoot = Path.Combine(directory.Path, "tasks");
        var taskDirectory = Path.Combine(taskRoot, "T1");
        Directory.CreateDirectory(taskDirectory);
        var eventsPath = Path.Combine(taskDirectory, "events.jsonl");
        var opened = JsonSerializer.Serialize(
            new LedgerEvent(
                1,
                new EventId("EV1"),
                new TaskId("T1"),
                new ActorId("operator"),
                DateTimeOffset.UnixEpoch,
                null,
                "correlation-1",
                new TaskOpened("Fixture", "CLI explicit-only proof")),
            LedgerJson.CreateOptions());
        var artifact = JsonSerializer.Serialize(
            new LedgerEvent(
                1,
                new EventId("EV2"),
                new TaskId("T1"),
                new ActorId("operator"),
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                null,
                "correlation-2",
                new ArtifactRecorded(new GovernedArtifact(
                    new ArtifactId("A1"),
                    GovernedArtifactKind.UserRequest,
                    "CLI explicit-only proof",
                    "The standalone command path stays manually invoked.",
                    null,
                    null,
                    null,
                    new Provenance(
                        new ActorId("operator"),
                        DateTimeOffset.UnixEpoch.AddSeconds(1),
                        "artifact.record")))),
            LedgerJson.CreateOptions());
        await File.WriteAllTextAsync(eventsPath, $"{opened}\n{artifact}\n");
        var canonicalBefore = await File.ReadAllTextAsync(eventsPath);
        var database = Path.Combine(directory.Path, "memory.sqlite");
        var lessonRoot = Path.Combine(directory.Path, "empty-lessons");
        Directory.CreateDirectory(lessonRoot);

        var rebuild = await RunAsync([
            "rebuild", "--root", taskRoot, "--lesson-root", lessonRoot,
            "--database", database, "--embedding", "none"
        ]);
        Assert.Equal(0, rebuild.ExitCode);
        var sourcesRead = JsonDocument.Parse(rebuild.Output).RootElement
            .GetProperty("statistics").GetProperty("sourcesRead").GetInt64();
        Assert.True(sourcesRead >= 1);

        string documentId;
        await using (var connection = new SqliteConnection($"Data Source={database}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM documents WHERE kind = 'Artifact'";
            documentId = Assert.IsType<string>(await command.ExecuteScalarAsync());
        }

        var update = await RunAsync([
            "update", "--root", taskRoot, "--lesson-root", lessonRoot,
            "--database", database, "--embedding", "none"
        ]);
        Assert.Equal(0, update.ExitCode);
        Assert.Equal(0, JsonDocument.Parse(update.Output).RootElement
            .GetProperty("statistics").GetProperty("documentsWritten").GetInt64());

        var search = await RunAsync([
            "search", "not-present", "--root", taskRoot,
            "--database", database, "--embedding", "none"
        ]);
        Assert.Equal(0, search.ExitCode);
        Assert.Empty(JsonDocument.Parse(search.Output).RootElement.GetProperty("results").EnumerateArray());

        var stats = await RunAsync(["stats", "--root", taskRoot, "--database", database]);
        Assert.Equal(0, stats.ExitCode);
        var statsOutput = JsonDocument.Parse(stats.Output).RootElement;
        Assert.Equal(sourcesRead, statsOutput.GetProperty("sourceCount").GetInt64());
        Assert.Equal(Path.GetFullPath(database), statsOutput.GetProperty("database").GetString());

        var missingDatabase = Path.Combine(directory.Path, "missing", "memory.sqlite");
        var missingStats = await RunAsync([
            "stats", "--root", taskRoot, "--database", missingDatabase
        ]);
        Assert.Equal(1, missingStats.ExitCode);
        Assert.Contains(Path.GetFullPath(missingDatabase), missingStats.Error, StringComparison.Ordinal);
        Assert.Contains("statistics", missingStats.Error, StringComparison.OrdinalIgnoreCase);

        var inspect = await RunAsync([
            "inspect", documentId, "--root", taskRoot, "--database", database
        ]);
        Assert.Equal(0, inspect.ExitCode);
        Assert.Equal(documentId, JsonDocument.Parse(inspect.Output).RootElement.GetProperty("id").GetString());

        var casesPath = Path.Combine(directory.Path, "evaluation-cases.json");
        await File.WriteAllTextAsync(casesPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            corpusVersion = "cli-e2e-v1",
            cases = new[]
            {
                new
                {
                    id = "artifact",
                    query = "CLI explicit-only proof",
                    expectedDocumentIds = new[] { documentId },
                    kinds = new[] { "Artifact" },
                    expectNoSupportedAnswer = false
                }
            }
        }));
        var evaluate = await RunAsync([
            "evaluate", "--cases", casesPath, "--mode", "lexical", "--root", taskRoot,
            "--database", database
        ]);
        Assert.Equal(0, evaluate.ExitCode);
        Assert.True(JsonDocument.Parse(evaluate.Output).RootElement
            .GetProperty("metrics").GetProperty("recallAt1").GetDouble() > 0);

        var unsupported = await RunAsync(["watch", "--root", taskRoot]);
        Assert.Equal(2, unsupported.ExitCode);
        Assert.Contains("Unknown command", unsupported.Error, StringComparison.Ordinal);

        var unconfirmedOllama = await RunAsync([
            "search", "query", "--database", database, "--embedding", "ollama", "--model", "embed"
        ]);
        Assert.Equal(2, unconfirmedOllama.ExitCode);
        Assert.Contains("--confirm-local", unconfirmedOllama.Error, StringComparison.Ordinal);

        var nonLoopbackOllama = await RunAsync([
            "search", "query", "--database", database, "--embedding", "ollama", "--model", "embed",
            "--endpoint", "http://example.com/", "--confirm-local"
        ]);
        Assert.NotEqual(0, nonLoopbackOllama.ExitCode);
        Assert.Contains("loopback", nonLoopbackOllama.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Using loopback Ollama", nonLoopbackOllama.Error, StringComparison.Ordinal);

        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var cancelled = await RunAsync(["stats", "--database", database], cancellation.Token);
            Assert.Equal(130, cancelled.ExitCode);
            Assert.Contains("Cancelled", cancelled.Error, StringComparison.Ordinal);
        }

        var drop = await RunAsync(["drop", "--root", taskRoot, "--database", database]);
        Assert.Equal(0, drop.ExitCode);
        Assert.False(File.Exists(database));
        Assert.Equal(canonicalBefore, await File.ReadAllTextAsync(eventsPath));
        Assert.False(File.Exists(Path.Combine(taskDirectory, ".writer.lock")));
    }

    [Fact]
    public void DefaultDatabasePathUsesResolvedCanonicalRootAndExplicitPathRemainsExact()
    {
        using var directory = new TemporaryDirectory();
        var firstRoot = Path.Combine(directory.Path, "first", "tasks");
        var secondRoot = Path.Combine(directory.Path, "second", "tasks");
        var explicitDatabase = Path.Combine(directory.Path, "explicit.sqlite");

        var first = ResolveDatabasePath(["rebuild", "--root", firstRoot]);
        var firstWithTrailingSeparator = ResolveDatabasePath([
            "rebuild", "--root", firstRoot + Path.DirectorySeparatorChar
        ]);
        var second = ResolveDatabasePath(["rebuild", "--root", secondRoot]);
        var explicitPath = ResolveDatabasePath([
            "rebuild", "--root", secondRoot, "--database", explicitDatabase
        ]);

        Assert.Equal(first, firstWithTrailingSeparator);
        Assert.NotEqual(first, second);
        Assert.Equal(Path.GetFullPath(explicitDatabase), explicitPath);
    }

    [Fact]
    public void PhysicalRootAliasesConvergeAndEveryCommandHonorsRootAndExactDatabase()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        var canonicalTemporaryRoot = CanonicalizeDirectoryIdentity(directory.Path);
        var physicalParent = Path.Combine(canonicalTemporaryRoot, "physical");
        var physicalRoot = Path.Combine(physicalParent, "tasks");
        var aliasParent = Path.Combine(canonicalTemporaryRoot, "alias");
        var aliasRoot = Path.Combine(aliasParent, "tasks");
        var explicitDatabase = Path.Combine(canonicalTemporaryRoot, "exact", "memory.sqlite");
        Directory.CreateDirectory(physicalRoot);
        Directory.CreateSymbolicLink(aliasParent, physicalParent);
        Assert.Equal(
            CanonicalizeDirectoryIdentity(physicalRoot),
            CanonicalizeDirectoryIdentity(aliasRoot));
        var invocations = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["rebuild"] = ["rebuild"],
            ["update"] = ["update"],
            ["search"] = ["search", "query"],
            ["inspect"] = ["inspect", "document-id"],
            ["stats"] = ["stats"],
            ["evaluate"] = ["evaluate", "--cases", "cases.json"],
            ["drop"] = ["drop"]
        };

        foreach (var invocation in invocations.Values)
        {
            var physical = ResolveDatabasePath([.. invocation, "--root", physicalRoot]);
            var alias = ResolveDatabasePath([.. invocation, "--root", aliasRoot]);
            var explicitPath = ResolveDatabasePath([
                .. invocation, "--root", aliasRoot, "--database", explicitDatabase
            ]);

            Assert.Equal(physical, alias);
            Assert.Equal(Path.GetFullPath(explicitDatabase), explicitPath);
        }
    }

    [Fact]
    public void StoredSymlinkTargetWithAliasedAncestorConvergesWithPhysicalRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        var aliasedPhysicalParent = Path.Combine(directory.Path, "physical");
        var aliasedPhysicalRoot = Path.Combine(aliasedPhysicalParent, "tasks");
        Directory.CreateDirectory(aliasedPhysicalRoot);

        var physicalRoot = CanonicalizeDirectoryIdentity(aliasedPhysicalRoot);
        if (string.Equals(aliasedPhysicalRoot, physicalRoot, StringComparison.Ordinal))
        {
            return;
        }

        var aliasParent = Path.Combine(CanonicalizeDirectoryIdentity(directory.Path), "stored-alias");
        var aliasRoot = Path.Combine(aliasParent, "tasks");
        Directory.CreateSymbolicLink(aliasParent, aliasedPhysicalParent);

        Assert.Equal(aliasedPhysicalParent, new DirectoryInfo(aliasParent).LinkTarget);
        Assert.Equal(physicalRoot, CanonicalizeDirectoryIdentity(aliasRoot));
        Assert.Equal(
            ResolveDatabasePath(["stats", "--root", physicalRoot]),
            ResolveDatabasePath(["stats", "--root", aliasRoot]));
    }

    [Fact]
    public void CaseVariantsConvergeExactlyWhenTheActualVolumeIsCaseInsensitive()
    {
        using var directory = new TemporaryDirectory();
        var lowerParent = Path.Combine(directory.Path, "case-probe");
        var lowerRoot = Path.Combine(lowerParent, "tasks");
        var variantRoot = Path.Combine(directory.Path, "CASE-PROBE", "tasks");
        Directory.CreateDirectory(lowerRoot);

        var volumeIsCaseInsensitive = Directory.Exists(variantRoot);
        var pathsConverge = string.Equals(
            ResolveDatabasePath(["stats", "--root", lowerRoot]),
            ResolveDatabasePath(["stats", "--root", variantRoot]),
            StringComparison.Ordinal);

        Assert.Equal(volumeIsCaseInsensitive, pathsConverge);
    }

    [Fact]
    public async Task EveryCommandExecutesAgainstTheRootDerivedDatabaseWithoutDatabaseOption()
    {
        using var directory = new TemporaryDirectory();
        var taskRoot = Path.Combine(directory.Path, "tasks");
        var taskDirectory = Path.Combine(taskRoot, "T1");
        var localData = Path.Combine(directory.Path, "local-data");
        var lessonRoot = Path.Combine(directory.Path, "lessons");
        Directory.CreateDirectory(taskDirectory);
        Directory.CreateDirectory(localData);
        Directory.CreateDirectory(lessonRoot);
        await WriteCanonicalFixtureAsync(Path.Combine(taskDirectory, "events.jsonl"));

        var rebuild = await RunProcessAsync([
            "rebuild", "--root", taskRoot, "--lesson-root", lessonRoot, "--embedding", "none"
        ], localData);
        if (rebuild.ExitCode == 1 &&
            rebuild.Error.Contains("Access to the path", StringComparison.Ordinal) &&
            rebuild.Error.Contains("AILedger/memory", StringComparison.Ordinal))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "The test host denies writes to the platform application-data directory used by root-derived databases.");
        }

        Assert.True(
            rebuild.ExitCode == 0,
            $"Root-only rebuild failed with exit {rebuild.ExitCode}: {rebuild.Error}");

        var stats = await RunProcessAsync(["stats", "--root", taskRoot], localData);
        Assert.Equal(0, stats.ExitCode);
        var database = Assert.IsType<string>(
            JsonDocument.Parse(stats.Output).RootElement.GetProperty("database").GetString());
        Assert.True(File.Exists(database), $"Derived database does not exist: {database}");

        var update = await RunProcessAsync([
            "update", "--root", taskRoot, "--lesson-root", lessonRoot, "--embedding", "none"
        ], localData);
        Assert.Equal(0, update.ExitCode);

        var search = await RunProcessAsync([
            "search", "CLI explicit-only proof", "--root", taskRoot, "--embedding", "none"
        ], localData);
        Assert.Equal(0, search.ExitCode);
        Assert.NotEmpty(JsonDocument.Parse(search.Output).RootElement.GetProperty("results").EnumerateArray());

        string documentId;
        await using (var connection = new SqliteConnection($"Data Source={database}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM documents WHERE kind = 'Artifact'";
            documentId = Assert.IsType<string>(await command.ExecuteScalarAsync());
        }

        var inspect = await RunProcessAsync(["inspect", documentId, "--root", taskRoot], localData);
        Assert.Equal(0, inspect.ExitCode);
        Assert.Equal(documentId, JsonDocument.Parse(inspect.Output).RootElement.GetProperty("id").GetString());

        var casesPath = Path.Combine(directory.Path, "evaluation-cases.json");
        await File.WriteAllTextAsync(casesPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            corpusVersion = "root-only-cli-v1",
            cases = new[]
            {
                new
                {
                    id = "artifact",
                    query = "CLI explicit-only proof",
                    expectedDocumentIds = new[] { documentId },
                    kinds = new[] { "Artifact" },
                    expectNoSupportedAnswer = false
                }
            }
        }));
        var evaluate = await RunProcessAsync([
            "evaluate", "--cases", casesPath, "--mode", "lexical", "--root", taskRoot
        ], localData);
        Assert.Equal(0, evaluate.ExitCode);

        var drop = await RunProcessAsync(["drop", "--root", taskRoot], localData);
        Assert.Equal(0, drop.ExitCode);
        Assert.False(File.Exists(database));
    }

    private static async Task WriteCanonicalFixtureAsync(string eventsPath)
    {
        var actor = new ActorId("operator");
        var events = new[]
        {
            new LedgerEvent(
                1, new EventId("EV-root-1"), new TaskId("T1"), actor, DateTimeOffset.UnixEpoch,
                null, "correlation-root-1", new TaskOpened("Fixture", "CLI explicit-only proof")),
            new LedgerEvent(
                1, new EventId("EV-root-2"), new TaskId("T1"), actor,
                DateTimeOffset.UnixEpoch.AddSeconds(1), null, "correlation-root-2",
                new ArtifactRecorded(new GovernedArtifact(
                    new ArtifactId("A-root"), GovernedArtifactKind.UserRequest,
                    "CLI explicit-only proof", "The derived database must be used by every command.",
                    null, null, null,
                    new Provenance(actor, DateTimeOffset.UnixEpoch.AddSeconds(1), "artifact.record"))))
        };
        await File.WriteAllTextAsync(
            eventsPath,
            string.Join('\n', events.Select(item => JsonSerializer.Serialize(item, LedgerJson.CreateOptions()))) + "\n");
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
        string[] arguments,
        string localData)
    {
        var assemblyPath = Path.Combine(
            Path.GetDirectoryName(typeof(MemoryCliEndToEndTests).Assembly.Location)!,
            "ailedger-memory.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(assemblyPath);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["XDG_DATA_HOME"] = localData;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output, await error);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string[] arguments,
        CancellationToken cancellationToken = default)
    {
        var assembly = LoadCliAssembly();
        var applicationType = assembly.GetType("MemoryCliApplication", throwOnError: true)!;
        var output = new StringWriter();
        var error = new StringWriter();
        var constructor = applicationType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(TextWriter), typeof(TextWriter)],
            modifiers: null)!;
        var application = constructor.Invoke([output, error]);
        var method = applicationType.GetMethod("RunAsync", BindingFlags.Instance | BindingFlags.Public)!;
        var task = (Task<int>)method.Invoke(application, [arguments, cancellationToken])!;
        var exitCode = await task;
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string ResolveDatabasePath(string[] arguments)
    {
        var applicationType = LoadCliAssembly().GetType("MemoryCliApplication", throwOnError: true)!;
        var inputType = applicationType.GetNestedType("CommandInput", BindingFlags.NonPublic)!;
        var input = inputType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [arguments]);
        return (string)applicationType.GetMethod("DatabasePath", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [input])!;
    }

    private static string CanonicalizeDirectoryIdentity(string path) =>
        (string)LoadCliAssembly().GetType("MemoryCliApplication", throwOnError: true)!
            .GetMethod("CanonicalizeDirectoryIdentity", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [path])!;

    private static Assembly LoadCliAssembly() => Assembly.LoadFrom(Path.Combine(
        Path.GetDirectoryName(typeof(MemoryCliEndToEndTests).Assembly.Location)!,
        "ailedger-memory.dll"));
}
