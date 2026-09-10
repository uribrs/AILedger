using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;
using System.Text.Json;

namespace AILedger.Tests.Core;

// A brief nothing records is unprovable. These pin the event that records one: that the operator is
// served a selection rather than everything, in the order the pipeline runs; that the record names
// each skill and the hash of what it said; and that building context twice changes nothing.
public sealed class ContextBuiltEventTests
{
    // C1's repair. The operator was the only role served no selection at all — eight skills,
    // 110,298 characters, unordered. Order is asserted, not just membership: the coordinator is the
    // entry point and the orchestrator is what it hands off to, and the manifest sorts by id, which
    // would put them the other way round (IC1).
    [Fact]
    public async Task TheOperatorIsServedTheCoordinatorFirstAndTheOrchestratorSecond()
    {
        using var root = new TemporaryDirectory();
        var manifest = await BuildAsync(root.Path, "T1", "operator");

        var skills = manifest.Artifacts
            .Where(artifact => artifact.Kind == ContextArtifactKind.Skill)
            .Select(artifact => artifact.Id)
            .ToArray();

        // The two skills, in the order the pipeline runs them, and nothing else but the reference
        // files those two skills carry. Before this change the same manifest held all eight.
        Assert.Equal("workflow-coordinator", skills[0]);
        Assert.Equal("task-orchestrator", skills[1]);
        Assert.All(skills, id => Assert.True(
            id is "workflow-coordinator" or "task-orchestrator" ||
            id.EndsWith(":workflow-coordinator", StringComparison.Ordinal) ||
            id.EndsWith(":task-orchestrator", StringComparison.Ordinal),
            $"'{id}' belongs to neither skill the operator is served."));
    }

    // R4 (hashes-must-detect-a-changed-skill). An id alone records that a brief carried a skill by
    // that name and nothing about what it said, so a skill edited afterwards would leave an event
    // that still looks satisfied.
    [Fact]
    public async Task TheEventRecordsOneRowPerSkillServedWithItsContentHash()
    {
        using var root = new TemporaryDirectory();
        var manifest = await BuildAsync(root.Path, "T1", "operator");
        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);

        var build = state!.ContextBuilds[new ActorId("operator")];
        Assert.Equal(RoleKind.Operator, build.Role);
        Assert.Equal(
            manifest.Artifacts.Count(artifact => artifact.Kind == ContextArtifactKind.Skill),
            build.Skills.Count);
        foreach (var skill in build.Skills)
        {
            var served = manifest.Artifacts.Single(artifact =>
                artifact.Kind == ContextArtifactKind.Skill && artifact.Id == skill.SkillId);
            Assert.Equal(ContextSkills.Hash(served.Content), skill.ContentHash);
        }
    }

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

    // R1 (replay-must-not-tighten). Every task in this ledger was created before context.built
    // existed, so replay has to keep accepting a history where work was added without one. This
    // reduces the events directly, which is the only way to produce that history now that the
    // command-time rule refuses to: the point is precisely that the two halves disagree.
    [Fact]
    public void ReplayAcceptsAHistoryThatAddsWorkWithNoContextBuiltEvent()
    {
        // Opened through the real commands, so the history is the shape a task actually has, and
        // then the work item is reduced straight onto it: the command handler now refuses to
        // produce this event without a brief, and the whole point is that replay must not.
        var task = new TestTask("legacy-task") { AutoBuildContext = false };
        Assert.Empty(task.State.ContextBuilds);

        var replayed = new TaskReducer().Apply(task.State, Event(
            task.TaskId, task.OperatorId, task.State.Version + 1, new WorkItemAdded(
                new WorkItem(
                    new WorkItemId("W1"), "Legacy work", task.OperatorId,
                    WorkItemStatus.Proposed, [], []))));

        Assert.Contains(new WorkItemId("W1"), replayed.WorkItems.Keys);
        Assert.Empty(replayed.ContextBuilds);
    }

    // The same history, offered to the command handler, is refused. Together with the test above
    // this is the twin-rule asymmetry stated as a test rather than as a comment: CommandHandler may
    // tighten, TaskTransitionValidator may not.
    [Fact]
    public void TheCommandHandlerRefusesWhatReplayAccepts()
    {
        var task = new TestTask();
        task.AutoBuildContext = false;

        var refusal = Assert.Throws<GovernanceException>(() => task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", null, [], [])));

        Assert.Contains("has not built its context", refusal.Message, StringComparison.Ordinal);
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

    private static LedgerEvent Event(TaskId taskId, ActorId actorId, long sequence, LedgerEventData data) =>
        new(
            GovernedTaskState.CurrentSchemaVersion,
            new EventId($"{taskId.Value}:{sequence:D10}"),
            taskId,
            actorId,
            new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero).AddMinutes(sequence),
            null,
            $"legacy-{sequence}",
            data);

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }
}
