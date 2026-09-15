using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;
using System.Text.Json;

namespace AILedger.Tests.ContextBriefing;

// The durable lifecycle of building a brief: first write, idempotence, replacement, work scoping,
// concurrent callers, and rollback when the manifest cannot be delivered.
public sealed class ContextBuildLifecycleTests
{
    // R2 (context-build-must-stay-a-read). A read that writes on every invocation is a read three
    // agents cannot perform at once without moving the task under each other. The suppression is in
    // the caller because a zero-event outcome cannot reach disk (IC2), so this drives the CLI.
    [Fact]
    public async Task BuildingContextTwiceIsIdempotentForTheSameSkillSet()
    {
        using var root = new TemporaryDirectory();
        await BuildAsync(root.Path, "T1", "operator");
        var afterFirst = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);

        await ContextBrief.BuildAsync(root.Path, "T1");
        var afterSecond = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);

        Assert.Equal(afterFirst!.Version, afterSecond!.Version);
        Assert.Equal(1, await CountContextBuiltAsync(root.Path, "T1"));
    }

    // VC2, and it is the same rule under the condition it was written for. Suppressing the repeat
    // in the caller — read the state, decide, then submit — leaves both of two simultaneous callers
    // deciding to append, and four agents briefing at the same instant is not hypothetical in this
    // repository. The decision is now taken inside the mutation lock, so only one append survives
    // and every caller still gets its brief.
    // No brief exists when the eight start, and that is the whole condition. Eight repeats of an
    // already recorded brief cannot show anything: every one of them reads a state that already
    // holds it and suppresses, so the test passes against the caller-side check as well. It is the
    // first brief that races.
    [Fact]
    // R4: the first-build race is the lifecycle boundary that must stay inside durable mutation.
    public async Task EightConcurrentFirstBriefsAppendOneEvent()
    {
        using var root = new TemporaryDirectory();
        await OpenAsync(root.Path, "T1", "operator");
        var afterOpening = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ContextBrief.BuildAsync(root.Path, "T1")));
        var afterConcurrent = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);

        // BuildAsync throws on a non-zero exit, so eight successful returns are eight briefs that
        // were served: the suppression must not be a refusal any of them reads as a failure.
        Assert.Equal(afterOpening!.Version + 1, afterConcurrent!.Version);
        Assert.Equal(1, await CountContextBuiltAsync(root.Path, "T1"));
    }

    // The other half of the same rule: a brief that is genuinely different is recorded. Without
    // this the test above would pass against a command that never writes at all.
    [Fact]
    public async Task ADifferentSkillSetForTheSameActorIsRecordedAsANewBrief()
    {
        using var root = new TemporaryDirectory();
        await BuildAsync(root.Path, "T1", "operator");
        var afterFirst = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);

        await Service(root.Path).ExecuteAsync(
            new TaskId("T1"),
            new RecordContextBuiltCommand(
                new ActorId("operator"), null, "second-brief", null,
                [new ContextSkill("workflow-coordinator", "a-different-hash")]),
            CancellationToken.None);
        var afterSecond = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);

        Assert.Equal(afterFirst!.Version + 1, afterSecond!.Version);
        Assert.Equal(2, await CountContextBuiltAsync(root.Path, "T1"));
        Assert.Equal(
            "a-different-hash",
            Assert.Single(afterSecond.ContextBuilds[new ActorId("operator")].Skills).ContentHash);
    }

    // SC1. The event is the gate's proof that this actor holds the brief, so recording it before
    // the manifest reaches the caller means a failed write leaves proof of a brief nobody received
    // — and the gate then admits 'work add' on evidence that is false. The write here fails because
    // its directory does not exist, which is the cheapest of the four failures in that finding;
    // they all reach the same place, after the event and before the caller has anything.
    [Fact]
    public async Task AManifestWriteThatFailsRecordsNoBrief()
    {
        using var root = new TemporaryDirectory();
        await OpenAsync(root.Path, "T1", "operator");

        var application = new CliApplication(
            TextWriter.Null, TextWriter.Null, Service,
            _ => throw new InvalidOperationException("No provider is launched here."),
            new ContextAssembler());
        var unwritable = Path.Combine(root.Path, "no-such-directory", "manifest.json");
        Assert.Equal(1, await application.RunAsync(
            ["context", "build", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--cognitive-root", ContextBrief.CognitiveRoot(), "--output", unwritable],
            CancellationToken.None));

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Empty(state!.ContextBuilds);
        Assert.Equal(0, await CountContextBuiltAsync(root.Path, "T1"));
    }

    // SC3 and MD2. The projection is keyed on the actor and the skill set, so a build naming a
    // different work item with the same skills appends nothing — which is why the projection cannot
    // carry a work item without naming whichever one happened to append first. The event still
    // records the invocation's work item; this pins that the second invocation writes no event, so
    // there is nothing for the projection to be right or wrong about.
    [Fact]
    public async Task ASecondBriefNamingADifferentWorkItemAppendsNoEvent()
    {
        using var root = new TemporaryDirectory();
        await BuildAsync(root.Path, "T1", "operator");

        var service = Service(root.Path);
        var served = await ContextBrief.RecordedAsync(service, "T1");
        await MoveToReadyAsync(service, new TaskId("T1"), new ActorId("operator"));
        foreach (var id in new[] { "W1", "W2" })
        {
            await service.ExecuteAsync(
                new TaskId("T1"),
                new AddWorkItemCommand(
                    new ActorId("operator"), null, $"add-{id}", new WorkItemId(id), id, null, [], [],
                    SkillsServedNow: served),
                CancellationToken.None);
        }

        var afterWork = await service.GetStateAsync(new TaskId("T1"), CancellationToken.None);
        await BuildForWorkAsync(root.Path, "T1", "operator", "W1");
        await BuildForWorkAsync(root.Path, "T1", "operator", "W2");
        var afterBriefs = await service.GetStateAsync(new TaskId("T1"), CancellationToken.None);

        Assert.Equal(afterWork!.Version, afterBriefs!.Version);
        Assert.Equal(1, await CountContextBuiltAsync(root.Path, "T1"));
    }

    private static async Task OpenAsync(string root, string taskId, string actorId)
    {
        var application = new CliApplication(
            TextWriter.Null, TextWriter.Null, Service,
            _ => throw new InvalidOperationException("No provider is launched here."),
            new ContextAssembler());
        Assert.Equal(0, await application.RunAsync(
            ["task", "open", "--root", root, "--task", taskId, "--actor", actorId,
             "--title", "Task", "--goal", "Goal"], CancellationToken.None));
    }

    private static async Task MoveToReadyAsync(
        IGovernedTaskService service,
        TaskId taskId,
        ActorId actor)
    {
        foreach (var stage in new[] { TaskStage.Research, TaskStage.Design, TaskStage.Scope, TaskStage.Ready })
        {
            await service.ExecuteAsync(taskId, new RequestStageTransitionCommand(
                actor, null, $"ready-{stage}", stage,
                WithoutPrerequisitesReason: "This test isolates context projection behavior"),
                CancellationToken.None);
        }
    }

    private static async Task<ContextManifest> BuildAsync(string root, string taskId, string actorId)
    {
        await OpenAsync(root, taskId, actorId);
        var application = new CliApplication(
            TextWriter.Null, TextWriter.Null, Service,
            _ => throw new InvalidOperationException("No provider is launched here."),
            new ContextAssembler());
        var manifestPath = Path.Combine(Path.GetTempPath(), $"manifest-{Guid.NewGuid():N}.json");
        Assert.Equal(0, await application.RunAsync(
            ["context", "build", "--root", root, "--task", taskId, "--actor", actorId,
             "--cognitive-root", ContextBrief.CognitiveRoot(), "--output", manifestPath],
            CancellationToken.None));
        var manifest = JsonSerializer.Deserialize<ContextManifest>(
            await File.ReadAllTextAsync(manifestPath), LedgerJson.CreateOptions())!;
        File.Delete(manifestPath);
        return manifest;
    }

    private static async Task BuildForWorkAsync(
        string root, string taskId, string actorId, string workItemId)
    {
        var application = new CliApplication(
            TextWriter.Null, TextWriter.Null, Service,
            _ => throw new InvalidOperationException("No provider is launched here."),
            new ContextAssembler());
        var manifestPath = Path.Combine(Path.GetTempPath(), $"manifest-{Guid.NewGuid():N}.json");
        Assert.Equal(0, await application.RunAsync(
            ["context", "build", "--root", root, "--task", taskId, "--actor", actorId,
             "--work", workItemId, "--cognitive-root", ContextBrief.CognitiveRoot(),
             "--output", manifestPath],
            CancellationToken.None));
        File.Delete(manifestPath);
    }

    private static async Task<int> CountContextBuiltAsync(string root, string taskId)
    {
        var count = 0;
        await foreach (var @event in Service(root).GetHistoryAsync(new TaskId(taskId), CancellationToken.None))
        {
            if (@event.Data is ContextBuilt)
            {
                count++;
            }
        }

        return count;
    }

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }
}
