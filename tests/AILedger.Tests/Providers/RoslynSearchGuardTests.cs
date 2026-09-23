using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Providers.Navigation;
using AILedger.Tests.Support;

namespace AILedger.Tests.Providers;

public sealed class RoslynSearchGuardTests
{
    [Theory]
    [InlineData("rg -n Engine .")]
    [InlineData("grep -l Engine Engine.cs")]
    [InlineData("git grep Engine")]
    [InlineData("rg --files-with-matches Engine .")]
    [InlineData("rg --files\nrg Engine Engine.cs")]
    [InlineData("rg -g '*.py' Engine .\nrg Engine .")]
    [InlineData("bash -lc 'rg Engine Engine.cs'")]
    [InlineData("sh -c 'rg Engine Engine.cs'")]
    public void DeniesShellContentSearchesInMixedRepositoryWithoutFailure(string command)
    {
        using var fixture = new Fixture();
        Assert.NotNull(fixture.Evaluate(fixture.Shell(command)));
    }

    [Theory]
    [InlineData("*.cs", null)]
    [InlineData(null, "cs")]
    [InlineData(null, null)]
    public void DeniesNativeGrepThatCanSearchCSharp(string? glob, string? type)
    {
        using var fixture = new Fixture();
        var hook = fixture.Grep(fixture.Repository);
        hook["tool_input"]!["glob"] = glob;
        hook["tool_input"]!["type"] = type;
        Assert.NotNull(fixture.Evaluate(hook));
    }

    [Theory]
    [InlineData("rg --files")]
    [InlineData("rg --files -g '*.cs'")]
    [InlineData("find . -name '*.cs'")]
    [InlineData("find . -name '*.slnx'")]
    [InlineData("rg -g '*.py' Engine .")]
    [InlineData("rg --glob='*.yaml' Engine .")]
    [InlineData("rg Pattern notes.txt > out.txt")]
    [InlineData("sed -n '1,80p' Engine.cs")]
    [InlineData("cat Engine.cs")]
    [InlineData("dotnet build App.slnx")]
    [InlineData("dotnet test App.slnx")]
    public void AllowsDiscoveryNonCSharpReadsAndBuildsWithoutSpendingFallback(string command)
    {
        using var fixture = new Fixture();
        fixture.RecordFailure();
        Assert.Null(fixture.Evaluate(fixture.Shell(command)));
        Assert.Null(fixture.Evaluate(fixture.Shell("rg Engine Engine.cs")));
        Assert.NotNull(fixture.Evaluate(fixture.Shell("rg Engine Engine.cs")));
    }

    [Theory]
    [InlineData("*.py")]
    [InlineData("*.yaml")]
    public void AllowsExplicitNativeGrepNonCSharpFilters(string glob)
    {
        using var fixture = new Fixture();
        var hook = fixture.Grep(fixture.Repository);
        hook["tool_input"]!["glob"] = glob;
        Assert.Null(fixture.Evaluate(hook));
    }

    [Fact]
    public async Task BackendFailurePermitsOneNativeSearchWithinSolutionDirectory()
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        fixture.Fail = true;
        var response = await fixture.Session.HandleAsync(Call("search_symbols"), default);
        Assert.True(IsError(response));
        Assert.Contains("Recorded Roslyn failure", response!.ToJsonString(), StringComparison.Ordinal);

        Assert.Null(fixture.Evaluate(fixture.Grep(Path.Combine(fixture.Repository, "Engine.cs"))));
        Assert.NotNull(fixture.Evaluate(fixture.Grep(Path.Combine(fixture.Repository, "Engine.cs"))));
    }

    [Fact]
    public async Task FailedAuthorizedLoadAlsoRecordsFallback()
    {
        using var fixture = new Fixture();
        fixture.Fail = true;
        Assert.True(IsError(await fixture.LoadAsync()));
        Assert.Null(fixture.Evaluate(fixture.Shell("rg Engine Engine.cs")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/does-not-exist/roslyn")]
    public async Task ObservedBackendStartupFailurePermitsOnlyScopedOneUseFallback(string executable)
    {
        using var fixture = new Fixture();
        Assert.NotNull(fixture.Evaluate(fixture.Shell("rg Engine Engine.cs")));
        var configurationPath = fixture.SaveStartupConfiguration(executable);

        Assert.Equal(1, await RoslynNavigationServer.RunAsync(configurationPath, CancellationToken.None));

        Assert.NotNull(fixture.Evaluate(fixture.Grep(fixture.OtherRepository)));
        Assert.NotNull(fixture.Evaluate(fixture.Grep(fixture.LedgerSource)));
        Assert.Null(fixture.Evaluate(fixture.Shell("rg Engine Engine.cs")));
        Assert.NotNull(fixture.Evaluate(fixture.Shell("rg Engine Engine.cs")));
    }

    [Fact]
    public void CrossRepositoryAndSiblingPrefixSearchesCannotSpendReceipt()
    {
        using var fixture = new Fixture();
        fixture.RecordFailure();
        Assert.NotNull(fixture.Evaluate(fixture.Shell("rg Engine ../repo-other/Engine.cs")));
        Assert.NotNull(fixture.Evaluate(fixture.Shell("rg Engine ../repo-sibling/Engine.cs")));
        Assert.NotNull(fixture.Evaluate(fixture.Shell("rg Engine Engine.cs ../repo-other/Engine.cs")));
        Assert.Null(fixture.Evaluate(fixture.Shell("rg Engine Engine.cs")));
    }

    [Fact]
    public void SymlinkEscapeCannotSpendReceipt()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture();
        File.CreateSymbolicLink(Path.Combine(fixture.Repository, "Linked.cs"),
            Path.Combine(fixture.OtherRepository, "Engine.cs"));
        fixture.RecordFailure();
        Assert.NotNull(fixture.Evaluate(fixture.Shell("rg Engine Linked.cs")));
        Assert.Null(fixture.Evaluate(fixture.Shell("rg Engine Engine.cs")));
    }

    [Theory]
    [InlineData("rg Engine Engine.cs && rg Engine Engine.cs")]
    [InlineData("rg Engine Engine.cs\nrg Engine Engine.cs")]
    [InlineData("rg --files\nrg Engine Engine.cs")]
    [InlineData("rg -g '*.py' Engine .\nrg Engine .")]
    public void CompoundSearchCannotSpendReceipt(string command)
    {
        using var fixture = new Fixture();
        fixture.RecordFailure();
        Assert.NotNull(fixture.Evaluate(fixture.Shell(command)));
        Assert.Null(fixture.Evaluate(fixture.Shell("rg Engine Engine.cs")));
    }

    [Fact]
    public void GitWorkingDirectoryOverrideCannotHideCSharpSearchBehindPythonCwd()
    {
        using var fixture = new Fixture();
        var pythonDirectory = Directory.CreateDirectory(Path.Combine(fixture.Repository, "python")).FullName;
        File.WriteAllText(Path.Combine(pythonDirectory, "script.py"), "Engine = 1");
        var ordinary = fixture.Shell("rg Engine .");
        ordinary["cwd"] = pythonDirectory;
        Assert.Null(fixture.Evaluate(ordinary));

        var redirected = fixture.Shell($"git -C '{fixture.OtherRepository}' grep Engine");
        redirected["cwd"] = pythonDirectory;
        Assert.NotNull(fixture.Evaluate(redirected));
    }

    [Fact]
    public async Task ParallelSearchesCannotSpendSameReceiptTwice()
    {
        using var fixture = new Fixture();
        fixture.RecordFailure();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            fixture.Evaluate(fixture.Shell("rg Engine Engine.cs")))));
        Assert.Single(results, result => result is null);
    }

    [Fact]
    public async Task SuccessfulRetryRevokesUnspentFailureReceipt()
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        fixture.Fail = true;
        await fixture.Session.HandleAsync(Call("search_symbols"), default);
        fixture.Fail = false;
        Assert.False(IsError(await fixture.Session.HandleAsync(Call("search_symbols"), default)));
        Assert.NotNull(fixture.Evaluate(fixture.Shell("rg Engine Engine.cs")));
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("missing-argument")]
    [InlineData("wrong-type")]
    public async Task LocalPathAndArgumentRefusalsDoNotRecordFallback(string kind)
    {
        using var fixture = new Fixture();
        var request = kind == "outside"
            ? Call("load_solution", "path", Path.Combine(fixture.OtherRepository, "App.slnx"))
            : Call("load_solution");
        if (kind == "wrong-type") request["params"]!["arguments"]!["path"] = 42;

        Assert.True(IsError(await fixture.Session.HandleAsync(request, default)));
        Assert.Equal(0, fixture.BackendCalls);
        Assert.NotNull(fixture.Evaluate(fixture.Shell("rg Engine Engine.cs")));
        Assert.NotNull(fixture.Evaluate(fixture.Grep(fixture.OtherRepository)));
    }

    [Fact]
    public async Task BackendInvalidArgumentsDoNotMintFailureReceipt()
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        fixture.InvalidArguments = true;
        var request = Call("search_symbols");
        var response = await fixture.Session.HandleAsync(request, default);
        Assert.Equal(-32602, response!["error"]!["code"]!.GetValue<int>());
        Assert.NotNull(fixture.Evaluate(fixture.Shell("rg Engine Engine.cs")));
    }

    [Theory]
    [InlineData("search_symbols", "limit")]
    [InlineData("find_tests_for_symbol", "maxDepth")]
    [InlineData("find_tests_for_symbol", "transitive")]
    [InlineData("find_references", "kinds")]
    [InlineData("load_solution", "include")]
    [InlineData("load_solution", "rootProjects")]
    public async Task MalformedOptionalArgumentsAreRejectedBeforeBackendCanReportInternalFailure(string tool, string field)
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        fixture.Fail = true;
        var request = tool == "load_solution"
            ? Call(tool, "path", Path.Combine(fixture.Repository, "App.slnx"))
            : tool == "search_symbols" ? Call(tool) : Call(tool, "symbol", "Engine");
        request["params"]!["arguments"]![field] = "invalid";

        Assert.True(IsError(await fixture.Session.HandleAsync(request, default)));
        Assert.Equal(1, fixture.BackendCalls);
        Assert.NotNull(fixture.Evaluate(fixture.Shell("rg Engine Engine.cs")));
    }

    [Theory]
    [InlineData("search_symbols", "query")]
    [InlineData("find_callers", "symbol")]
    public async Task MissingSemanticArgumentCannotCreateFallback(string tool, string field)
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        var request = Call(tool);
        request["params"]!["arguments"]!.AsObject().Remove(field);
        Assert.True(IsError(await fixture.Session.HandleAsync(request, default)));
        Assert.Equal(1, fixture.BackendCalls);
        Assert.NotNull(fixture.Evaluate(fixture.Shell("rg Engine Engine.cs")));
    }

    [Fact]
    public async Task SuccessfulEmptyResultDoesNotAuthorizeShellConfirmation()
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        Assert.False(IsError(await fixture.Session.HandleAsync(Call("search_symbols"), default)));
        Assert.NotNull(fixture.Evaluate(fixture.Shell("rg Engine Engine.cs")));
    }

    private static JsonObject Call(string name, string? field = null, string? value = null) => new()
    {
        ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call",
        ["params"] = new JsonObject
        {
            ["name"] = name,
            ["arguments"] = field is not null ? new JsonObject { [field] = value }
                : name == "search_symbols" ? new JsonObject { ["query"] = "Engine" } : new JsonObject()
        }
    };

    private static bool IsError(JsonObject? response) => response?["result"]?["isError"]?.GetValue<bool>() == true;

    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory root = new();
        private readonly RoslynNavigationSettings settings;
        private readonly RoslynFallbackStore store;
        internal string Repository { get; }
        internal string OtherRepository { get; }
        internal string LedgerSource => Path.Combine(settings.LedgerRoot, "Engine.cs");
        internal RoslynNavigationSession Session { get; }
        internal bool Fail { get; set; }
        internal bool InvalidArguments { get; set; }
        internal int BackendCalls { get; private set; }

        internal Fixture()
        {
            Repository = CreateRepository("repo");
            OtherRepository = CreateRepository("repo-other");
            CreateRepository("repo-sibling");
            var ledger = Directory.CreateDirectory(Path.Combine(root.Path, ".ailedger")).FullName;
            var guard = Path.Combine(root.Path, "guard");
            settings = new RoslynNavigationSettings("unused", [Repository], ledger, guard);
            store = new RoslynFallbackStore(guard);
            Session = new RoslynNavigationSession(new RoslynSolutionPaths(settings), RespondAsync, store);
        }

        internal Task<JsonObject?> LoadAsync() => Session.HandleAsync(
            Call("load_solution", "path", Path.Combine(Repository, "App.slnx")), default);

        internal void RecordFailure() => store.Record(Repository, "search_symbols", "backend unavailable");
        internal string? Evaluate(JsonObject hook) => RoslynSearchGuard.Evaluate(settings, hook);

        internal string SaveStartupConfiguration(string executable)
        {
            var path = Path.Combine(root.Path, "startup.json");
            File.WriteAllText(LedgerSource, "class Engine {}");
            File.WriteAllText(path, JsonSerializer.Serialize(settings with
            {
                Executable = executable,
                AllowedDirectories = [Repository, settings.LedgerRoot]
            }));
            return path;
        }

        internal JsonObject Shell(string command) => Hook("exec_command", new JsonObject { ["cmd"] = command });
        internal JsonObject Grep(string path) => Hook("Grep", new JsonObject { ["pattern"] = "Engine", ["path"] = path });

        private JsonObject Hook(string tool, JsonObject input) => new()
        {
            ["tool_name"] = tool, ["cwd"] = Repository, ["tool_input"] = input
        };

        private string CreateRepository(string name)
        {
            var repository = Directory.CreateDirectory(Path.Combine(root.Path, name)).FullName;
            File.WriteAllText(Path.Combine(repository, "App.slnx"), "<Solution />");
            File.WriteAllText(Path.Combine(repository, "Engine.cs"), "class Engine {}");
            File.WriteAllText(Path.Combine(repository, "script.py"), "Engine = 1");
            File.WriteAllText(Path.Combine(repository, "settings.yaml"), "name: Engine");
            File.WriteAllText(Path.Combine(repository, "notes.txt"), "Pattern");
            return repository;
        }

        private Task<JsonObject> RespondAsync(JsonObject request, CancellationToken cancellationToken)
        {
            BackendCalls++;
            var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = request["id"]!.DeepClone() };
            if (InvalidArguments)
            {
                response["error"] = new JsonObject { ["code"] = -32602, ["message"] = "Missing required query argument" };
            }
            else
            {
                response["result"] = new JsonObject { ["isError"] = Fail, ["content"] = new JsonArray() };
            }
            return Task.FromResult(response);
        }

        public void Dispose() => root.Dispose();
    }
}
