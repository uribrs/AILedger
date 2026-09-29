using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Findings;
using AILedger.Storage;

namespace AILedger.Tests.Findings;

public sealed class FindingsAuthorityTests
{
    [Fact]
    public async Task HostGrantAndExplicitRunlessPermissionAreRequiredEvenForRetry()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        await f.RecordAsync();
        foreach (var binding in new[] { f.Binding with { AllowRecordFindings = false },
                     f.Binding with { AllowRunless = false }, f.Binding with { CorrelationId = " " },
                     f.Binding with { RunId = new RunId("R"), CorrelationId = "R" },
                     f.Binding with { RunId = new RunId("R"), CorrelationId = "wrong" },
                     f.Binding with { TaskId = new TaskId("../escape") } })
        {
            var result = await f.RecordAsync(binding: binding);
            Assert.Equal("authorization_denied", result.Error?.Code);
            Assert.Equal("unknown", result.Error?.CommitState);
            Assert.Null(result.Receipt);
        }
    }

    [Fact]
    public async Task RevokedAuthorityBlocksReceiptAndConflictsWithoutDisclosingIt()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var actor = new ActorId("researcher");
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "assign", actor, RoleKind.Researcher,
            [Capability.AddClaim, Capability.AddEvidence]));
        var binding = f.Binding with { ActorId = actor };
        Assert.Null((await f.RecordAsync(binding: binding)).Error);
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "revoke", actor, RoleKind.Researcher,
            [Capability.AddClaim]));
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        foreach (var request in new[] { FindingsFixture.Request(), FindingsFixture.Claims("request-1") })
        {
            var result = await f.RecordAsync(request, binding);
            Assert.Equal("kernel_refused", result.Error?.Code);
            Assert.Equal("unknown", result.Error?.CommitState);
            Assert.Contains("AddEvidence", result.Error!.Message);
            Assert.Null(result.Receipt);
        }
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
    }

    [Fact]
    public async Task MissingEvidenceCapabilityCannotCommitEarlierCandidateClaims()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var actor = new ActorId("researcher");
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "assign", actor, RoleKind.Researcher, [Capability.AddClaim]));
        var result = await f.RecordAsync(binding: f.Binding with { ActorId = actor });
        Assert.Equal("kernel_refused", result.Error?.Code);
        Assert.Equal("evidence[0]", result.Error?.ItemPath);
        Assert.Equal("not_committed", result.Error?.CommitState);
        Assert.Empty((await f.StateAsync()).Claims);
    }

    [Fact]
    public async Task SameRequestIdIsScopedToActorAndLedgerLocation()
    {
        using var f = new FindingsFixture();
        using var other = new FindingsFixture();
        await f.OpenAsync();
        await other.OpenAsync();
        var actor = new ActorId("researcher");
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "assign", actor, RoleKind.Researcher,
            [Capability.AddClaim, Capability.AddEvidence]));
        var first = await f.RecordAsync();
        var second = await f.RecordAsync(binding: f.Binding with { ActorId = actor });
        var third = await other.RecordAsync();
        Assert.Null(second.Error);
        Assert.Null(third.Error);
        Assert.Equal(3, new[] { first, second, third }.Select(r => r.Receipt!.TransactionId).Distinct().Count());
    }

    [Fact]
    public async Task RunBindingPreservesAuthorshipCorrelationAndCostAfterCompletionAndRetry()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var actor = new ActorId("researcher");
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "assign", actor, RoleKind.Researcher,
            [Capability.AddClaim, Capability.AddEvidence]));
        await f.ExecuteAsync(new RequestStageTransitionCommand(f.Actor, null, "research", TaskStage.Research,
            WithoutPrerequisitesReason: "Isolated run attribution fixture"));
        var run = new RunId("R1");
        await f.ExecuteAsync(new StartRunCommand(f.Actor, null, "dispatch", run, null, "codex", "session",
            SubjectActorId: actor));
        var binding = f.Binding with { ActorId = actor, RunId = run, CorrelationId = run.Value, AllowRunless = false };
        var wrongActor = await f.RecordAsync(binding: binding with { ActorId = f.Actor });
        Assert.Equal("authorization_denied", wrongActor.Error?.Code);
        var first = await f.RecordAsync(binding: binding);
        Assert.Null(first.Error);
        await f.ExecuteAsync(new CompleteRunCommand(f.Actor, null, "close", run, AgentRunStatus.Completed,
            "session", OutputTokens: 19, TokensInUncached: 31));
        Assert.True((await f.RecordAsync(binding: binding)).Replayed);
        // Recording has no new active-run prerequisite, including a fresh request after completion.
        Assert.Null((await f.RecordAsync(FindingsFixture.Claims("after-completion"), binding)).Error);
        var state = await f.StateAsync();
        Assert.Equal(19, state.Runs[run].OutputTokens);
        Assert.Equal(31, state.Runs[run].TokensInUncached);
        Assert.Null(state.Runs[run].Turns);
        var events = (await File.ReadAllLinesAsync(f.EventsPath)).Select(l =>
            JsonSerializer.Deserialize<LedgerEvent>(l, LedgerJson.CreateOptions())!).ToArray();
        var written = events.Where(e => e.Data is ClaimAdded or EvidenceAdded).ToArray();
        Assert.Equal(3, written.Length);
        Assert.All(written, e => { Assert.Equal(actor, e.ActorId); Assert.Equal("R1", e.CorrelationId); });
        Assert.Single(events.Where(e => e.Data is RunCompleted));
        Assert.Equal(first.Receipt!.EventIds[0], written[0].EventId.Value);
    }

    [Fact]
    public async Task ArchivedTaskReturnsReceiptWithoutReapplyingMutationStageRules()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var first = await f.RecordAsync();
        // An explicit operator waiver exercises real stage commands; no relaxed reducer is used.
        await f.ExecuteAsync(new RecordAlternativeCommand(f.Actor, null, "alternative", new AlternativeId("A"),
            "Keep a sidecar receipt", "It cannot atomically commit with events", null));
        foreach (var stage in new[] { TaskStage.Research, TaskStage.Design, TaskStage.Scope, TaskStage.Ready,
                     TaskStage.Execution, TaskStage.Verification, TaskStage.Review, TaskStage.Learn })
            await f.ExecuteAsync(new RequestStageTransitionCommand(f.Actor, null, "stage", stage,
                WithoutPrerequisitesReason: "Isolated storage retry fixture"));
        await f.ExecuteAsync(new MarkLessonBearingCommand(f.Actor, null, "mark", LessonSourceKind.RejectedAlternative, "A",
            Class: LessonClass.Untested, Repo: "fixture", Tags: ["findings"], Verify: "dotnet test",
            DoNot: "split receipt commit", Actor: LessonActor.Researcher, VerifyExpects: VerifyExpectation.Present));
        await f.ExecuteAsync(new RequestStageTransitionCommand(f.Actor, null, "archive", TaskStage.Archive,
            WithoutPrerequisitesReason: "Isolated storage retry fixture"));
        Assert.Equal(TaskStage.Archive, (await f.StateAsync()).Stage);
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var retry = await f.RecordAsync();
        Assert.True(retry.Replayed);
        Assert.Equal(first.Receipt!.LedgerVersion, retry.Receipt!.LedgerVersion);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
        // The current kernel permits adding claims at Archive. Preserve that command policy too.
        Assert.Null((await f.RecordAsync(FindingsFixture.Claims())).Error);
    }

    [Fact]
    public async Task CitingAnUnrecalledLessonIsStillAKernelRefusal()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var request = FindingsFixture.Claims() with { Findings = [new("f", "Claim", FromLesson: "unknown")] };
        var result = await f.RecordAsync(request);
        Assert.Equal("kernel_refused", result.Error?.Code);
        Assert.Empty((await f.StateAsync()).Claims);
    }

    [Fact]
    public async Task RecalledLessonCanBeCitedWithoutChangingProvenance()
    {
        using var f = new FindingsFixture();
        var lesson = new Lesson(new("source:validatedclaim:C"), new("source"), LessonSourceKind.ValidatedClaim, "C", "Lesson", "validated",
            ["fixture"], new(f.Actor, DateTimeOffset.UtcNow, "stage.archive"));
        var reducer = new TaskReducer();
        var handler = new CommandHandler();
        var outcome = handler.Handle(null, new OpenTaskCommand(f.Actor, null, "open", f.TaskId, "Task", "Goal",
            RecalledLessons: [lesson]), DateTimeOffset.UtcNow);
        System.IO.Directory.CreateDirectory(f.Directory);
        await File.WriteAllTextAsync(f.EventsPath, string.Join("\n", outcome.Events.Select(e =>
            JsonSerializer.Serialize(e, LedgerJson.CreateOptions()))) + "\n");
        var request = FindingsFixture.Claims() with { Findings = [new("f", "Claim", FromLesson: "source:validatedclaim:C")] };
        var result = await f.RecordAsync(request);
        Assert.Null(result.Error);
        Assert.Equal(lesson.Id, (await f.StateAsync()).Claims[new(result.Receipt!.Findings[0].ClaimId)].FromLesson);
    }

    [Fact]
    public async Task MissingTaskDoesNotCreateATask()
    {
        using var f = new FindingsFixture();
        var result = await f.RecordAsync();
        Assert.Equal("task_not_found", result.Error?.Code);
        Assert.False(System.IO.Directory.Exists(f.Directory));
    }
}
