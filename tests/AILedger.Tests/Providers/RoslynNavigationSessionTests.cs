using System.Text.Json.Nodes;
using AILedger.Providers.Navigation;
using AILedger.Tests.Support;

namespace AILedger.Tests.Providers;

public sealed class RoslynNavigationSessionTests
{
    [Theory]
    [InlineData(".sln")]
    [InlineData(".slnx")]
    public async Task LoadsAcrossGrantedRepositoriesAndSwitchesBackByExactPath(string extension)
    {
        using var fixture = new Fixture();
        var first = fixture.Solution("repo-a", "Same" + extension);
        var second = fixture.Solution("repo-b", "Same" + extension);
        var session = fixture.Session();

        Assert.False(IsError(await session.HandleAsync(Call("load_solution", "path", first), default)));
        Assert.False(IsError(await session.HandleAsync(Call("load_solution", "path", second), default)));
        Assert.False(IsError(await session.HandleAsync(Call("set_active_solution", "name", first), default)));
        Assert.False(IsError(await session.HandleAsync(Call("find_callers", "symbol", "Engine.Compute"), default)));
        Assert.Equal(4, fixture.Forwarded.Count);
        Assert.EndsWith("Same" + extension,
            fixture.Forwarded[2]["params"]!["arguments"]!["name"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedSelectionBlocksQueriesUntilAnotherSuccessfulSelection()
    {
        using var fixture = new Fixture();
        var first = fixture.Solution("repo-a");
        var outside = fixture.Solution("outside");
        var session = fixture.Session();
        await session.HandleAsync(Call("load_solution", "path", first), default);

        Assert.True(IsError(await session.HandleAsync(Call("load_solution", "path", outside), default)));
        Assert.True(IsError(await session.HandleAsync(Call("search_symbols"), default)));
        Assert.False(IsError(await session.HandleAsync(Call("list_solutions"), default)));
        Assert.Equal(2, fixture.Forwarded.Count);

        Assert.False(IsError(await session.HandleAsync(Call("set_active_solution", "name", first), default)));
        Assert.False(IsError(await session.HandleAsync(Call("search_symbols"), default)));
    }

    [Fact]
    public async Task BackendLoadFailureAlsoClearsSelectionAndRequiresReload()
    {
        using var fixture = new Fixture();
        var first = fixture.Solution("repo-a");
        var session = fixture.Session();
        await session.HandleAsync(Call("load_solution", "path", first), default);
        fixture.Fail = true;

        Assert.True(IsError(await session.HandleAsync(Call("load_solution", "path", first), default)));
        fixture.Fail = false;
        Assert.True(IsError(await session.HandleAsync(Call("search_symbols"), default)));
        Assert.True(IsError(await session.HandleAsync(Call("set_active_solution", "name", first), default)));
        Assert.Equal(2, fixture.Forwarded.Count);
        Assert.False(IsError(await session.HandleAsync(Call("load_solution", "path", first), default)));
    }

    [Fact]
    public async Task DoesNotAllowBackgroundLoadingPartialNamesOrUnloadedSwitches()
    {
        using var fixture = new Fixture();
        var solution = fixture.Solution("repo-a");
        var session = fixture.Session();
        Assert.True(IsError(await session.HandleAsync(Call("set_active_solution", "name", solution), default)));
        var load = Call("load_solution", "path", solution);
        load["params"]!["arguments"]!["background"] = true;
        await session.HandleAsync(load, default);
        Assert.False(fixture.Forwarded.Single()["params"]!["arguments"]!["background"]!.GetValue<bool>());
        Assert.True(IsError(await session.HandleAsync(Call("set_active_solution", "name", "App"), default)));
    }

    [Fact]
    public async Task DeniesDirectCallsToUnlistedToolsAndFiltersDiscovery()
    {
        using var fixture = new Fixture();
        var session = fixture.Session();
        Assert.True(IsError(await session.HandleAsync(Call("rename_symbol"), default)));
        Assert.True(IsError(await session.HandleAsync(Call("start_background_task"), default)));
        Assert.Empty(fixture.Forwarded);
        fixture.Tools = new JsonArray(
            new JsonObject { ["name"] = "load_solution", ["inputSchema"] = new JsonObject
            {
                ["properties"] = new JsonObject { ["path"] = new JsonObject(), ["background"] = new JsonObject() }
            } },
            new JsonObject { ["name"] = "rename_symbol" });
        var response = await session.HandleAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/list"
        }, default);
        var tools = response!["result"]!["tools"]!.AsArray();
        Assert.Single(tools);
        Assert.Equal("load_solution", tools[0]!["name"]!.GetValue<string>());
        Assert.Null(tools[0]!["inputSchema"]!["properties"]!["background"]);
    }

    [Fact]
    public async Task RejectsSiblingPrefixTraversalLedgerAndNonSolutionPaths()
    {
        using var fixture = new Fixture();
        var paths = fixture.Paths();
        var outside = fixture.Solution("repo-a-sibling");
        var ledgerSolution = fixture.Solution("repo-a/.ailedger");
        var project = fixture.Solution("repo-a", "App.csproj");
        Assert.Throws<ArgumentException>(() => paths.Resolve(outside));
        Assert.Throws<ArgumentException>(() => paths.Resolve(ledgerSolution));
        Assert.Throws<ArgumentException>(() => paths.Resolve(project));
        Assert.Throws<ArgumentException>(() => paths.Resolve("App.sln"));
        Assert.Throws<ArgumentException>(() => paths.Resolve(
            Path.Combine(fixture.Root.Path, "repo-a", "..", "repo-a-sibling", "App.sln")));
        var session = fixture.Session();
        Assert.True(IsError(await session.HandleAsync(Call("load_solution", "path", outside), default)));
        Assert.Empty(fixture.Forwarded);
    }

    [Fact]
    public void LedgerExclusionRecognizesCaseAliasesOnInsensitiveFilesystems()
    {
        using var fixture = new Fixture();
        fixture.Solution("repo-a/.ailedger");
        var alias = Path.Combine(fixture.Root.Path, "repo-a", ".AILEDGER", "App.sln");
        if (File.Exists(alias))
        {
            Assert.Throws<ArgumentException>(() => fixture.Paths().Resolve(alias));
        }
    }

    [Fact]
    public void SymlinksCannotSelectOutsideGrantOrIntoLedger()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unprivileged Windows runners cannot create symbolic links.
        }

        using var fixture = new Fixture();
        var outside = fixture.Solution("outside");
        var ledger = fixture.Solution("repo-a/.ailedger");
        var repository = Path.Combine(fixture.Root.Path, "repo-a");
        File.CreateSymbolicLink(Path.Combine(repository, "Link.sln"), outside);
        Directory.CreateSymbolicLink(Path.Combine(repository, "external"), Path.GetDirectoryName(outside)!);
        File.CreateSymbolicLink(Path.Combine(repository, "Ledger.sln"), ledger);
        var paths = fixture.Paths();
        Assert.Throws<ArgumentException>(() => paths.Resolve(Path.Combine(repository, "Link.sln")));
        Assert.Throws<ArgumentException>(() => paths.Resolve(Path.Combine(repository, "external", "App.sln")));
        Assert.Throws<ArgumentException>(() => paths.Resolve(Path.Combine(repository, "Ledger.sln")));
    }

    private static JsonObject Call(string tool, string? field = null, string? value = null) => new()
    {
        ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call",
        ["params"] = new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = field is not null ? new JsonObject { [field] = value }
                : tool == "search_symbols" ? new JsonObject { ["query"] = "Engine" } : new JsonObject()
        }
    };

    private static bool IsError(JsonObject? response) => response?["result"]?["isError"]?.GetValue<bool>() == true;

    private sealed class Fixture : IDisposable
    {
        internal TemporaryDirectory Root { get; } = new();
        internal List<JsonObject> Forwarded { get; } = [];
        internal bool Fail { get; set; }
        internal JsonArray? Tools { get; set; }

        internal Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Root.Path, "repo-a", ".ailedger"));
            Directory.CreateDirectory(Path.Combine(Root.Path, "repo-b"));
        }

        internal string Solution(string repository, string name = "App.sln")
        {
            var directory = Directory.CreateDirectory(Path.Combine(Root.Path, repository)).FullName;
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, "placeholder solution: backend is stubbed");
            return path;
        }

        internal RoslynSolutionPaths Paths() => new(new RoslynNavigationSettings("unused",
            [Path.Combine(Root.Path, "repo-a"), Path.Combine(Root.Path, "repo-b")],
            Path.Combine(Root.Path, "repo-a", ".ailedger")));

        internal RoslynNavigationSession Session() => new(Paths(), (request, _) =>
        {
            Forwarded.Add((JsonObject)request.DeepClone());
            return Task.FromResult(new JsonObject
            {
                ["jsonrpc"] = "2.0", ["id"] = request["id"]!.DeepClone(),
                ["result"] = Tools is not null
                    ? new JsonObject { ["tools"] = Tools.DeepClone() }
                    : new JsonObject { ["isError"] = Fail, ["content"] = new JsonArray() }
            });
        });

        public void Dispose() => Root.Dispose();
    }
}
