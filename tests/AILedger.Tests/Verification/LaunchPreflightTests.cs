using AILedger.Cli;
using AILedger.Cli.Verification;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Tests.Support;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Verification;

public sealed class LaunchPreflightTests
{
    private const string Declared = """
        { "schemaVersion": 1, "profiles": { "redis-integration": {
            "command": "dotnet test", "requires": ["docker"], "launchPreflight": true } } }
        """;

    private const string Undeclared = """
        { "schemaVersion": 1, "profiles": { "redis-integration": {
            "command": "dotnet test", "requires": ["docker"], "launchPreflight": false } } }
        """;

    // R3 (launch-preflight-pre-cost). A profile that declares launchPreflight, with no socket to reach,
    // refuses the launch before the adapter is resolved, before the version probe and before
    // run.start. The adapter factory throws if it is ever invoked, because an exit code cannot tell a
    // refusal before cost from one after it. The refusal is journalled at the launch site.
    [Fact]
    public async Task R3_RefusesBeforeAdapterAndRunStart()
    {
        const string expected = "declares launchPreflight";
        using var launch = new LaunchFixture(adapter: null);
        launch.WriteProfile(Declared);
        await launch.OpenAsync();

        var exit = await launch.LaunchAsync();

        var state = await Service(launch.Root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Equal(0, launch.FactoryCalls);
        Assert.DoesNotContain(new RunId("R1"), state!.Runs.Keys);
        Assert.Contains(expected, launch.Error.ToString(), StringComparison.Ordinal);
        var journalled = Assert.Single(RefusalJournal(launch.Root));
        Assert.Equal("provider-launch", journalled.GetProperty("site").GetString());
        Assert.StartsWith(
            "Launch refused by the verification profile preflight:",
            journalled.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains(expected, journalled.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(launch.Engine.Requests);
    }

    // R3 (launch-preflight-declared-only), m3 (PC44, PALT16): a profile file that cannot be parsed
    // declares nothing that can be trusted, so it does not refuse the launch. v2 refused every launch in
    // such a repository, including the worker dispatched to repair the file. The socket is missing
    // here, so a check that still ran would refuse; the briefing reports the file instead (S10).
    [Theory]
    [InlineData("""{ "schemaVersion": 1, "profiles": { "redis-integration": { "command": "x", "image": "redis" } } }""")]
    [InlineData("""{ "schemaVersion": 1, "profiles": { "redis-integration": { "command": "x", "requires": ["docker"], "launchPreflight": true, "image": "redis" } } }""")]
    [InlineData("""not json""")]
    public async Task R3_UnparseableProfileDoesNotRefuseLaunch(string profile)
    {
        var capture = new CapturingAdapter();
        using var launch = new LaunchFixture(capture);
        launch.WriteProfile(profile);
        await launch.OpenAsync();

        var exit = await launch.LaunchAsync();

        var state = await Service(launch.Root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, exit);
        Assert.Equal(1, launch.FactoryCalls);
        Assert.Contains(new RunId("R1"), state!.Runs.Keys);
        Assert.Empty(RefusalJournal(launch.Root));
        Assert.Empty(launch.Engine.Requests);
        Assert.Single(capture.Requests);
    }

    // R3 controls, the other half of PALT8: only a declaration triggers the check. A docker profile
    // without launchPreflight, and a repository with no profile, launch with the socket missing and
    // never touch the engine. A declared profile with a reachable socket launches after one ping.
    // R4 (agent-env-and-briefing-parity), environment half: whatever the profile says, the request the
    // launcher hands the adapter carries no environment at all, so no Docker setting reaches the agent.
    [Theory]
    [InlineData("undeclared", false)]
    [InlineData("no-profile", false)]
    [InlineData("declared-reachable", true)]
    // m1 (PC42): a declaring profile above the repository root is not the repository's, so it is not
    // checked; the socket is missing, so v2's unbounded walk would have refused this launch.
    [InlineData("declared-above-root", false)]
    public async Task R3_OnlyADeclarationIsCheckedAndTheAgentEnvironmentStaysEmpty(string profile, bool pings)
    {
        var capture = new CapturingAdapter();
        using var launch = new LaunchFixture(capture, createSocketFile: profile == "declared-reachable");
        switch (profile)
        {
            case "undeclared":
                launch.WriteProfile(Undeclared);
                break;
            case "declared-reachable":
                launch.WriteProfile(Declared);
                break;
            case "declared-above-root":
                launch.WriteProfile(Declared, Path.GetDirectoryName(launch.Work));
                break;
        }

        await launch.OpenAsync();

        var exit = await launch.LaunchAsync();

        var state = await Service(launch.Root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, exit);
        Assert.Equal(1, launch.FactoryCalls);
        Assert.Contains(new RunId("R1"), state!.Runs.Keys);
        Assert.Empty(RefusalJournal(launch.Root));
        var request = Assert.Single(capture.Requests);
        Assert.Empty(request.Environment);
        Assert.Equal(
            pings ? new[] { (HttpMethod.Get, "/_ping") } : [],
            launch.Engine.Requests);
    }

    private sealed class LaunchFixture : IDisposable
    {
        private readonly TemporaryDirectory _root = new();
        private readonly TemporaryDirectory _provider = new();

        public LaunchFixture(IAgentAdapter? adapter, bool createSocketFile = false)
        {
            Work = Directory.CreateDirectory(Path.Combine(_provider.Path, "provider-work")).FullName;
            // S1: the working directory is a repository root; discovery stops at its .git entry.
            Directory.CreateDirectory(Path.Combine(Work, ".git"));
            var socket = Path.Combine(_provider.Path, "docker.sock");
            if (createSocketFile)
            {
                File.WriteAllText(socket, string.Empty);
            }

            Application = new CliApplication(
                TextWriter.Null, Error, Service,
                _ =>
                {
                    FactoryCalls++;
                    return adapter ?? throw new InvalidOperationException("A refused launch must resolve no adapter.");
                },
                new ContextAssembler())
            {
                VerificationHost = new VerificationHost
                {
                    GetEnvironmentVariable = name => name == "DOCKER_HOST"
                        ? $"unix://{socket}"
                        : Environment.GetEnvironmentVariable(name),
                    CreateEngineHandler = Engine.Create,
                    Spawner = ScriptedSpawner.NeverCalled()
                }
            };
        }

        public string Root => _root.Path;
        public string Work { get; }
        public int FactoryCalls { get; private set; }
        public StringWriter Error { get; } = new();
        public FakeDockerEngine Engine { get; } = new();
        public CliApplication Application { get; }

        public void WriteProfile(string json, string? directory = null) =>
            File.WriteAllText(Path.Combine(directory ?? Work, "ailedger.verification.json"), json);

        public async Task OpenAsync()
        {
            await Application.RunAsync(
                ["task", "open", "--root", Root, "--task", "T1", "--actor", "operator",
                 "--title", "Task", "--goal", "Goal"], CancellationToken.None);
            await ContextBrief.BuildAsync(Root, "T1");
        }

        public Task<int> LaunchAsync() => Application.RunAsync(
            ["provider", "launch", "--root", Root, "--task", "T1", "--actor", "operator",
             "--run", "R1", "--provider", "codex", "--executable", "/usr/bin/true",
             "--working-directory", Work, "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        public void Dispose()
        {
            _root.Dispose();
            _provider.Dispose();
        }
    }
}
