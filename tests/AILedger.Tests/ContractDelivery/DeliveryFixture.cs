using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Tests.Findings;
using AILedger.Tests.Support;

namespace AILedger.Tests.ContractDelivery;

internal sealed class DeliveryFixture : IDisposable
{
    internal FindingsFixture Ledger { get; } = new();
    private readonly TemporaryDirectory _work = new();
    internal StringWriter Output { get; } = new();
    internal StringWriter Error { get; } = new();
    internal CliApplication App { get; }
    internal ScriptedAdapter Adapter { get; } = new();
    internal List<string> Order { get; } = [];
    internal CancellationToken RunCancellation { get; set; }

    internal DeliveryFixture()
    {
        App = new(Output, Error, _ => Ledger.Service(), _ => Adapter, new ContextAssembler());
        Adapter.Dispatched = request => Order.Add("dispatch:" + request.RunId.Value);
    }

    internal async Task InitializeAsync()
    {
        await Ledger.OpenAsync();
        foreach (var (actor, role) in new[] { ("planner", "planning-lead"), ("researcher", "researcher"),
                     ("worker", "worker"), ("verifier", "verifier"), ("reviewer", "code-reviewer") })
            await Ok("actor", "attach", "--target", actor, "--role", role);
        await Ledger.ExecuteAsync(new AddClaimCommand(Ledger.Actor, null, "claim", new("C"), "Internal premise", null));
        await Ledger.ExecuteAsync(new RecordArtifactCommand(Ledger.Actor, null, "request", new("U"),
            GovernedArtifactKind.UserRequest, "Request", "REQUEST_SECRET", null, null, null));
    }

    internal async Task<JsonElement> Context(string actor = "operator", string? output = null)
    {
        await Ok(["context", "build", "--actor", actor, "--cognitive-root", ContextBrief.CognitiveRoot(),
            .. output is null ? Array.Empty<string>() : ["--output", output]]);
        return output is null ? LastJson() : JsonDocument.Parse(await File.ReadAllTextAsync(output)).RootElement.Clone();
    }

    internal async Task ReconAsync(string provider = "codex", string domain = "internal")
    {
        await Stage(TaskStage.Research);
        Adapter.Act = async request =>
        {
            await Ledger.ExecuteAsync(new ConsultLessonsCommand(request.ActorId, null, request.RunId.Value,
                request.RunId, LessonConsultationPurpose.Recon, "Map current premise", ["contract-delivery"], []));
            await Ledger.ExecuteAsync(new RecordAlternativeCommand(request.ActorId, null, "alt", new("ALT"),
                "Skip recon", "Need evidence", null, null));
            var template = InternalReconDocuments.CreateTemplate(await Ledger.StateAsync());
            await FileAsync(request, "RECON", GovernedArtifactKind.InternalRecon, JsonSerializer.Serialize(template with
            {
                Assessments = template.Assessments.Select(a => a with { Domain = domain }).ToArray(), Report = "RECON_SECRET"
            }));
        };
        Assert.True(await Launch("RN", "planner", provider) == 0, Error.ToString());
    }

    internal async Task PlanAsync(string provider = "codex")
    {
        await Stage(TaskStage.Design);
        Adapter.Act = async request =>
        {
            await FileAsync(request, "PROMPT", GovernedArtifactKind.PromptContract, "PROMPT_SECRET");
            await FileAsync(request, "PLAN", GovernedArtifactKind.OrchestrationPlan, "No material attention items — disposable fixture.");
        };
        Assert.True(await Launch("RP", "planner", provider) == 0, Error.ToString());
    }

    internal async Task PrepareWorkerAsync()
    {
        await Stage(TaskStage.Scope);
        await Stage(TaskStage.Ready);
        await Ok("work", "add", "--id", "W", "--title", "Implement fixture", "--owner", "worker", "--scope", _work.Path,
            "--cognitive-root", ContextBrief.CognitiveRoot());
        await Stage(TaskStage.Execution);
    }

    internal Task<JsonElement> Stage(TaskStage stage, string? reason = null) => StageCore(stage, reason);
    private async Task<JsonElement> StageCore(TaskStage stage, string? reason)
    {
        await Ok(["stage", "transition", "--stage", stage.ToString(),
            .. reason is null ? Array.Empty<string>() : ["--reason", reason]]);
        return LastJson();
    }

    internal Task<int> Launch(string run, string subject, string provider = "codex", string? work = null, params string[] more) =>
        Run(["provider", "launch", "--run", run, "--subject", subject, "--provider", provider,
            "--executable", "/usr/bin/true", "--working-directory", _work.Path, "--cognitive-root", ContextBrief.CognitiveRoot(),
            .. work is null ? Array.Empty<string>() : ["--work", work], .. more]);

    internal Task FileAsync(AgentLaunchRequest request, string id, GovernedArtifactKind kind, string content) =>
        Ledger.ExecuteAsync(new RecordArtifactCommand(request.ActorId, null, request.RunId.Value, new(id), kind,
            "Output", content, request.Assurance is null ? request.WorkItemId : null, request.RunId, null));

    internal async Task<int> Run(params string[] args)
    {
        Output.GetStringBuilder().Clear(); Error.GetStringBuilder().Clear();
        var exit = await App.RunAsync([.. args.Take(2), "--root", Ledger.Root, "--task", Ledger.TaskId.Value,
            "--actor", "operator", .. args.Skip(2)], RunCancellation);
        Order.Add("response:" + string.Join(" ", args.Take(2)));
        return exit;
    }

    internal async Task Ok(params string[] args) => Assert.True(await Run(args) == 0, Error.ToString());
    internal JsonElement LastJson() => JsonDocument.Parse(Output.ToString()).RootElement.Clone();
    public void Dispose() { Ledger.Dispose(); _work.Dispose(); Output.Dispose(); Error.Dispose(); }

    internal sealed class ScriptedAdapter : IAgentAdapter
    {
        public string Provider => "stub";
        internal Action<AgentLaunchRequest>? Dispatched { get; set; }
        internal Func<Task>? OnProbe { get; set; }
        internal Func<AgentLaunchRequest, Task>? BeforeAct { get; set; }
        internal Func<AgentLaunchRequest, Task>? Act { get; set; }
        internal AgentRunStatus Status { get; set; } = AgentRunStatus.Completed;
        internal bool Throw { get; set; }
        internal List<AgentLaunchRequest> Requests { get; } = [];
        public async Task<string> ProbeVersionAsync(string executablePath, CancellationToken cancellationToken)
        {
            if (OnProbe is not null) await OnProbe();
            return "stub";
        }
        public async Task<AgentRunResult> RunAsync(AgentLaunchRequest request, CancellationToken cancellationToken)
        {
            Dispatched?.Invoke(request); Requests.Add(request);
            if (BeforeAct is not null) await BeforeAct(request);
            if (Act is not null) await Act(request);
            if (Throw) throw new IOException("Stub provider failed after partial findings");
            var now = DateTimeOffset.UtcNow;
            return new(request.RunId, request.Provider, "session-" + request.RunId.Value, Status, now, now, 0,
                "BLOCKED is deliberately unparsed free text", [], "", "stub", [], false, null);
        }
    }
}
