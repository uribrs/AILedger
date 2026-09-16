using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Cli;
using AILedger.Tests.Support;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Assurance;

// Every mutation goes through the production service or CLI. Stage waivers only arrange the
// stage; they neither synthesize outputs nor bypass command authorization/admission.
internal sealed class BundleFixture : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    public const string Candidate = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public string Root { get; }
    public string Repository { get; }
    public StringWriter Error { get; } = new();
    public StringWriter Output { get; } = new();
    public CliApplication App { get; }
    public RecordingAdapter Adapter { get; }
    public IGovernedTaskService Service => CliApplicationTestSupport.Service(Root);
    public string[] Common => ["--root", Root, "--task", "T1"];

    private BundleFixture()
    {
        var path = _directory.Path;
        if (OperatingSystem.IsMacOS() && (path.StartsWith("/var/", StringComparison.Ordinal) || path.StartsWith("/tmp/", StringComparison.Ordinal))) path = "/private" + path;
        Root = Directory.CreateDirectory(Path.Combine(path, "ledger")).FullName;
        Repository = Directory.CreateDirectory(Path.Combine(path, "repository")).FullName;
        Directory.CreateDirectory(Path.Combine(Repository, ".git"));
        Adapter = new RecordingAdapter(this);
        App = new CliApplication(Output, Error, CliApplicationTestSupport.Service, _ => Adapter, new ContextAssembler());
    }

    public static async Task<BundleFixture> CreateAsync(bool multipleScopes = false, bool dependencies = false, bool verifierOwnsA = false, string? fileScope = null, string workerBProvider = "codex")
    {
        var f = new BundleFixture();
        await f.Ok("task", "open", "--title", "TASK_SENTINEL", "--goal", "GOAL_SENTINEL");
        await ContextBrief.BuildAsync(f.Root, "T1");
        Assert.All(await RecordExecutionArtifactsAsync(f.App, f.Common), exit => Assert.Equal(0, exit));
        await CliStageFixture.AdvanceAsync(f.App, f.Root, "T1", TaskStage.Scope, TaskStage.Ready);
        foreach (var (actor, role) in new[] { ("worker", "worker"), ("other", "worker"), ("verifier", "verifier"), ("reviewer", "code-reviewer") })
            await f.Ok("actor", "attach", "--target", actor, "--role", role);
        if (multipleScopes)
            await f.Ok("alternative", "record", "--id", "MULTI", "--statement", "Split fixture scopes", "--rejected-because", "One fixture work item exercises all-scope access");
        foreach (var member in new[] { "A", "B", "C", "D" })
        {
            var scope = Directory.CreateDirectory(Path.Combine(f.Repository, member)).FullName;
            if (member == "A" && fileScope is not null)
            {
                scope = Path.Combine(fileScope == "root" ? f.Repository : scope, "owned.cs");
                await System.IO.File.WriteAllTextAsync(scope, "// fixture scope");
            }
            string[] more = [];
            if (multipleScopes && member == "A")
            {
                var second = Directory.CreateDirectory(Path.Combine(f.Repository, "A-second")).FullName;
                more = ["--scope", second, "--not-split-because", "MULTI"];
            }
            string[] dependency = [];
            if (dependencies)
            {
                await f.Ok("claim", "add", "--id", "C" + member, "--statement", member + "_CLAIM_SENTINEL");
                await f.Ok("evidence", "add", "--id", "E" + member, "--source-type", "fixture", "--citation", "fixture.cs:1", "--summary", member + "_EVIDENCE_SENTINEL", "--supports", "C" + member);
                await f.Ok("claim", "resolve", "--id", "C" + member, "--status", "validated", "--evidence", "E" + member);
                dependency = ["--depends-on", "C" + member];
            }
            await f.Ok(["work", "add", "--id", member, "--title", member + "_TITLE_SENTINEL", "--owner", member == "A" && verifierOwnsA ? "verifier" : member == "B" ? "other" : "worker", "--scope", scope, .. more, .. dependency]);
        }
        await CliStageFixture.ToExecutionAsync(f.App, f.Root);
        foreach (var member in new[] { "A", "B", "C", "D" }) await f.Work(member, "W" + member, member == "B" ? workerBProvider : "codex");
        await CliStageFixture.ToVerificationAsync(f.App, f.Root);
        f.Error.GetStringBuilder().Clear();
        f.Output.GetStringBuilder().Clear();
        return f;
    }

    public async Task<int> Run(params string[] args)
    {
        Error.GetStringBuilder().Clear();
        return await App.RunAsync([.. args.Take(2), .. Common, "--actor", "operator", .. args.Skip(2)], CancellationToken.None);
    }

    public async Task Ok(params string[] args) => Assert.True(await Run(args) == 0, Error.ToString());
    public async Task<GovernedTaskState> State() => (await Service.GetStateAsync(new TaskId("T1"), CancellationToken.None))!;
    public Task<IReadOnlyList<LedgerEvent>> History() => HistoryAsync(Root);
    public static string[] Scope(params string[] members) => members.Length == 0 ? [] : ["--work", members[0], .. members.Skip(1).SelectMany(m => new[] { "--also-work", m })];

    public async Task Work(string member, string run, string provider = "codex")
    {
        await Ok("run", "start", "--subject", member == "B" ? "other" : "worker", "--run", run, "--work", member, "--provider", provider);
        await Ok("run", "complete", "--run", run, "--status", "completed", "--session", "session-" + run);
    }

    public Task<int> Launch(string run, string[] members, string subject = "verifier", string? pair = null,
        string? candidate = Candidate, string provider = "claude", params string[] extra) =>
        Run(["provider", "launch", "--subject", subject, "--run", run, .. Scope(members),
            .. (candidate is null ? Array.Empty<string>() : new[] { "--candidate", candidate }),
            .. (pair is null ? Array.Empty<string>() : new[] { "--verifier-run", pair }),
            "--provider", provider, "--executable", "/usr/bin/true", "--cognitive-root", FindCognitiveRoot(), .. extra]);

    public async Task Verify(string run, params string[] members)
    {
        Assert.True(await Launch(run, members) == 0, Error.ToString());
        Assert.Equal(AgentRunStatus.Completed, (await State()).Runs[new RunId(run)].Status);
    }

    public async Task Review(string run, string pair, params string[] members)
    {
        await CliStageFixture.ToReviewAsync(App, Root);
        Assert.True(await Launch(run, members, "reviewer", pair) == 0, Error.ToString());
        Assert.Equal(AgentRunStatus.Completed, (await State()).Runs[new RunId(run)].Status);
    }

    public async Task<int> File(string actor, string run, string artifact, string kind, params string[] flags)
    {
        var state = await State();
        var binding = state.Runs[new RunId(run)].Assurance;
        var claims = binding is null ? [] : binding.WorkItemIds.SelectMany(id => state.WorkItems[id].DependsOnClaims).Distinct().ToArray();
        var body = kind == "verifier-output" ? ArtifactCommands.VerifierBody : "REVIEW_BODY_SENTINEL";
        if (claims.Length > 0)
            body = body.Replace("| --- | --- | --- | --- | --- |\n\n", "| --- | --- | --- | --- | --- |\n" + string.Join("\n", claims.Select(c => $"| {c.Value} | VALIDATED | fixture | fixture.cs:1 | verifier |")) + "\n\n");
        using var input = new StandardInput(body);
        return await Run(["artifact", "record", "--actor", actor, "--run", run, "--id", artifact, "--kind", kind, "--title", "OUTPUT_TITLE_SENTINEL", "--body-stdin", .. flags]);
    }

    public async Task<AssuranceBinding> Binding(params string[] members)
    {
        var state = await State();
        return new AssuranceBinding(1, members.Select(m => new WorkItemId(m)).OrderBy(m => m.Value, StringComparer.Ordinal).ToArray(),
            members.Order(StringComparer.Ordinal).Select(m => new AssuranceWorkVersion(new WorkItemId(m),
                state.Runs.Values.Where(r => r.WorkItemId == new WorkItemId(m) && r.SubjectRole == RoleKind.Worker && r.Status == AgentRunStatus.Completed).OrderByDescending(r => r.EndedAt).First().Id)).ToArray(), Candidate);
    }

    public void Dispose() { Error.Dispose(); Output.Dispose(); _directory.Dispose(); }

    internal sealed class RecordingAdapter(BundleFixture fixture) : IAgentAdapter
    {
        public string Provider => "claude";
        public int Probes { get; private set; }
        public List<AgentLaunchRequest> Requests { get; } = [];
        public List<GovernedTaskState> ActiveStates { get; } = [];
        public bool FileOutput { get; set; } = true;
        public Func<AgentLaunchRequest, Task>? BeforeResult { get; set; }
        public Func<AgentRunResult, AgentRunResult>? ChangeResult { get; set; }
        public Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken) { Probes++; return Task.FromResult("fixture-version"); }
        public async Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var state = await fixture.State();
            ActiveStates.Add(state);
            if (BeforeResult is not null) await BeforeResult(request);
            var role = state.Runs[request.RunId].SubjectRole;
            if (FileOutput && role is RoleKind.Verifier or RoleKind.CodeReviewer)
            {
                string[] scope = request.Assurance is null ? ["--work", request.WorkItemId!.Value.Value] : [];
                Assert.True(await fixture.File(request.ActorId.Value, request.RunId.Value, "OUT-" + request.RunId.Value,
                    role == RoleKind.Verifier ? "verifier-output" : "code-review-output", scope) == 0, fixture.Error.ToString());
            }
            var now = DateTimeOffset.UtcNow;
            var result = new AgentRunResult(request.RunId, request.Provider, "session-" + request.RunId.Value,
                AgentRunStatus.Completed, now, now.AddMilliseconds(1), 0, "done", [], "", "fixture-version", [], request.Mode == AgentLaunchMode.Resume, null);
            return ChangeResult?.Invoke(result) ?? result;
        }
    }
}
