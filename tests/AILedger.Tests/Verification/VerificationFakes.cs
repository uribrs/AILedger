using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using AILedger.Cli;
using AILedger.Cli.Verification;
using AILedger.Core.Application;
using AILedger.Tests.Support;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Verification;

/// <summary>
/// An in-memory Docker engine behind the injected transport. No test binds a unix socket (PALT10):
/// every handler the command creates writes to this one recorder, so a test sees each call the
/// preflight and the cleanup made, in order, across every handler instance.
/// </summary>
internal sealed class FakeDockerEngine
{
    private readonly object _gate = new();
    private readonly List<(HttpMethod Method, string PathAndQuery)> _requests = [];
    private readonly List<string> _socketPaths = [];

    /// <summary>What <c>GET /containers/json</c> returns. Only these ids may be deleted.</summary>
    public List<string> Owned { get; } = [];

    /// <summary>Ids whose delete answers 500, to exercise a non-fatal cleanup failure.</summary>
    public HashSet<string> FailingDeletes { get; } = [];

    /// <summary>Replaces the whole engine when a test needs a specific failure.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage>? Override { get; set; }

    public IReadOnlyList<(HttpMethod Method, string PathAndQuery)> Requests
    {
        get { lock (_gate) return [.. _requests]; }
    }

    public IReadOnlyList<string> SocketPaths
    {
        get { lock (_gate) return [.. _socketPaths]; }
    }

    public HttpMessageHandler Create(string socketPath)
    {
        lock (_gate) _socketPaths.Add(socketPath);
        return new Handler(this);
    }

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        lock (_gate) _requests.Add((request.Method, request.RequestUri!.PathAndQuery));
        if (Override is { } custom)
        {
            return custom(request);
        }

        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Get && path == "/_ping")
        {
            return Text(HttpStatusCode.OK, "OK");
        }

        if (request.Method == HttpMethod.Get && path == "/containers/json")
        {
            var body = "[" + string.Join(",", Owned.Select(id => $"{{\"Id\":\"{id}\"}}")) + "]";
            return Text(HttpStatusCode.OK, body);
        }

        if (request.Method == HttpMethod.Delete && path.StartsWith("/containers/", StringComparison.Ordinal))
        {
            var id = Uri.UnescapeDataString(path["/containers/".Length..]);
            if (!Owned.Contains(id))
            {
                // R2: a real engine would delete it. Answer success so a leak is visible only in the
                // request log, which every cleanup test asserts on.
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return FailingDeletes.Contains(id)
                ? Text(HttpStatusCode.InternalServerError, "removal failed")
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        return Text(HttpStatusCode.NotFound, "unexpected call");
    }

    internal static HttpResponseMessage Text(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8) };

    private sealed class Handler(FakeDockerEngine engine) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(engine.Respond(request));
    }
}

/// <summary>A spawner a test scripts. It records every start it was handed.</summary>
internal sealed class ScriptedSpawner(Func<VerificationProcessStart, CancellationToken, Task<int?>> run)
    : IVerificationProcessSpawner
{
    private readonly List<VerificationProcessStart> _starts = [];

    public IReadOnlyList<VerificationProcessStart> Starts
    {
        get { lock (_starts) return [.. _starts]; }
    }

    public Task<int?> RunAsync(VerificationProcessStart start, CancellationToken cancellationToken)
    {
        lock (_starts) _starts.Add(start);
        return run(start, cancellationToken);
    }

    /// <summary>R1: any call is the failure under test, so the count is the assertion.</summary>
    public static ScriptedSpawner NeverCalled() =>
        new((_, _) => throw new Xunit.Sdk.XunitException("A refused verification must start no process."));

    public static ScriptedSpawner Exits(int code) => new((_, _) => Task.FromResult<int?>(code));

    /// <summary>Stands in for a process that never ends: it returns null once the token fires.</summary>
    public static ScriptedSpawner Hangs() => new(async (_, token) =>
    {
        try
        {
            await Task.Delay(Timeout.Infinite, token);
        }
        catch (OperationCanceledException)
        {
        }

        return null;
    });
}

internal sealed class SteppingClock(DateTimeOffset start) : TimeProvider
{
    private int _calls;

    public override DateTimeOffset GetUtcNow() => start.AddSeconds(Interlocked.Increment(ref _calls) - 1);
}

/// <summary>A real git checkout in a temporary directory; the command's git probe is not injectable.</summary>
internal static class GitCheckout
{
    public static void Init(string path)
    {
        Git(path, "init", "-q");
        Git(path, "-c", "user.name=test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false",
            "commit", "-q", "--allow-empty", "-m", "base");
    }

    public static void CommitFile(string path, string relativePath, string content)
    {
        File.WriteAllText(System.IO.Path.Combine(path, relativePath), content);
        Git(path, "add", "--", relativePath);
        Git(path, "-c", "user.name=test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false",
            "commit", "-q", "-m", relativePath);
    }

    /// <summary>The root as git reports it, links resolved; the command runs there (S4).</summary>
    public static string TopLevel(string path) =>
        Encoding.UTF8.GetString(Git(path, "rev-parse", "--show-toplevel")).Trim();

    /// <summary>
    /// S12 computed independently of the production type, from the contract text: one record per
    /// listed path in UTF-8 ordinal order, <c>path\0kind\0value\n</c>, hashed as a whole. Only the
    /// kinds these fixtures create are supported; any other entry fails the test rather than guess.
    /// </summary>
    public static string ExpectedWorktreeDigest(string root)
    {
        var paths = Encoding.UTF8.GetString(Git(root, "ls-files", "-z", "--cached", "--others", "--exclude-standard"))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .Select(Encoding.UTF8.GetBytes)
            .ToList();
        paths.Sort((left, right) => left.AsSpan().SequenceCompareTo(right));
        using var records = new MemoryStream();
        foreach (var path in paths)
        {
            var full = System.IO.Path.Combine(root, Encoding.UTF8.GetString(path));
            var info = new FileInfo(full);
            Assert.True(info.Exists && info.LinkTarget is null, $"Fixture path '{full}' is not a regular file.");
            var kind = !OperatingSystem.IsWindows() && (File.GetUnixFileMode(full) & UnixFileMode.UserExecute) != 0
                ? "executable"
                : "file";
            records.Write(path);
            records.Write(Encoding.UTF8.GetBytes($"\0{kind}\0{VerificationFixture.Sha256(File.ReadAllBytes(full))}\n"));
        }

        return VerificationFixture.Sha256(records.ToArray());
    }

    public static byte[] Git(string path, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(path);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        using var buffer = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(buffer);
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
        return buffer.ToArray();
    }
}

/// <summary>One ledger, one checkout, one fake engine and a CLI wired to them.</summary>
internal sealed class VerificationFixture : IDisposable
{
    public const string RedisCommand = "dotnet test tests/Redis --results-directory \"$AILEDGER_VERIFICATION_OUTPUT\"";

    private readonly TemporaryDirectory _root = new();
    private readonly TemporaryDirectory _work = new();

    public VerificationFixture(IVerificationProcessSpawner spawner, bool createSocketFile = true, bool git = true)
    {
        Checkout = Directory.CreateDirectory(Path.Combine(_work.Path, "checkout")).FullName;
        if (git)
        {
            GitCheckout.Init(Checkout);
        }

        SocketPath = Path.Combine(_work.Path, "docker.sock");
        if (createSocketFile)
        {
            // S7 checks the path exists before it calls the transport. A plain file stands in for
            // the socket; nothing is bound (PALT10).
            File.WriteAllText(SocketPath, string.Empty);
        }

        Spawner = spawner;
        Host = new VerificationHost
        {
            GetEnvironmentVariable = name => name == "DOCKER_HOST"
                ? $"unix://{SocketPath}"
                : Environment.GetEnvironmentVariable(name),
            CreateEngineHandler = Engine.Create,
            Spawner = spawner,
            Clock = new SteppingClock(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero))
        };
        Application = new CliApplication(
            Output, Error, Service,
            _ => throw new InvalidOperationException("verification run resolves no provider adapter."),
            new ContextAssembler())
        {
            VerificationHost = Host
        };
    }

    public string Root => _root.Path;
    public string Checkout { get; }
    public string RepositoryRoot => GitCheckout.TopLevel(Checkout);
    public string SocketPath { get; }
    public FakeDockerEngine Engine { get; } = new();
    public IVerificationProcessSpawner Spawner { get; }
    public VerificationHost Host { get; }
    public StringWriter Output { get; } = new();
    public StringWriter Error { get; } = new();
    public CliApplication Application { get; }
    public string TaskPath => Path.Combine(Root, "T1");

    public string[] Common(string actor = "operator") => ["--root", Root, "--task", "T1", "--actor", actor];

    public async Task OpenAsync()
    {
        Assert.Equal(0, await Application.RunAsync(
            ["task", "open", .. Common(), "--title", "Task", "--goal", "Goal"], CancellationToken.None));
    }

    public void WriteProfile(string json, string? directory = null) =>
        File.WriteAllText(Path.Combine(directory ?? Checkout, "ailedger.verification.json"), json);

    public void WriteRedisProfile(bool docker = true, string command = RedisCommand, string? directory = null) => WriteProfile($$"""
        {
          "schemaVersion": 1,
          "profiles": {
            "redis-integration": {
              "command": {{System.Text.Json.JsonSerializer.Serialize(command)}},
              {{(docker ? "\"requires\": [\"docker\"]," : string.Empty)}}
              "resultFiles": ["*.trx"],
              "timeoutSeconds": 1200
            }
          }
        }
        """, directory);

    public Task<int> RunAsync(
        string evidenceId,
        string? confirm,
        string actor = "operator",
        CancellationToken cancellationToken = default,
        params string[] extra)
    {
        string[] confirmation = confirm is null ? [] : ["--confirm", confirm];
        return Application.RunAsync(
            ["verification", "run", .. Common(actor), "--id", evidenceId, "--checkout", Checkout,
             "--candidate", Candidate, .. confirmation, .. extra],
            cancellationToken);
    }

    public static readonly string Candidate = new('c', 64);

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string Sha256(string text) => Sha256(Encoding.UTF8.GetBytes(text));

    public void Dispose()
    {
        _root.Dispose();
        _work.Dispose();
    }
}
