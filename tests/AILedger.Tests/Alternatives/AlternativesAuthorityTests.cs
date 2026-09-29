using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Alternatives;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;

namespace AILedger.Tests.Alternatives;

[Collection(AlternativesRecordingCollection.Name)]
public sealed class AlternativesAuthorityTests
{
    [Theory]
    [InlineData(RoleKind.Researcher)]
    [InlineData(RoleKind.Worker)]
    [InlineData(RoleKind.Verifier)]
    [InlineData(RoleKind.CodeReviewer)]
    public async Task AuthorDefaultsPermitRecordingOnlyAfterExplicitAssignmentAndDoNotGrantApproval(RoleKind role)
    {
        using var f = new AlternativesFixture();
        await f.OpenAsync();
        var author = new ActorId("author");
        var defaults = RoleDefaults.For(role);
        Assert.Contains(Capability.RecordAlternative, defaults);
        var old = defaults.Where(c => c != Capability.RecordAlternative).ToArray();
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "old", author, role, old));
        var binding = f.Binding with { ActorId = author };
        var refused = await f.RecordAsync(binding: binding);
        Assert.Equal("kernel_refused", refused.Error?.Code);
        Assert.Contains("RecordAlternative", refused.Error!.Message);
        Assert.Equal(old.Order(), (await f.StateAsync()).Roles[author].Capabilities.Order());
        Assert.Empty((await f.StateAsync()).Alternatives);
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "explicit-upgrade", author, role, defaults));
        var recorded = await f.RecordAsync(binding: binding);
        Assert.Null(recorded.Error);
        var state = await f.StateAsync(); // full historical replay accepts both assignments
        Assert.All(state.Alternatives.Values, a => Assert.Equal(author, a.Provenance.ActorId));
        var policy = new AuthorizationPolicy();
        foreach (var command in new LedgerCommand[] {
            new ResolveClaimCommand(author, null, "no", new("C"), ClaimStatus.Validated, [], null),
            new ResolveDecisionCommand(author, null, "no", new("D"), DecisionStatus.Accepted),
            new AssignRoleCommand(author, null, "no", new("other"), RoleKind.Operator, []),
            new AddWorkItemCommand(author, null, "no", new("W"), "Scope", author, [], ["src"]) })
            Assert.Throws<GovernanceException>(() => policy.Authorize(state, command));
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "revoke", author, role, old));
        foreach (var request in new[] { AlternativesFixture.Request(), AlternativesFixture.Single("request-1") })
        {
            var denied = await f.RecordAsync(request, binding);
            Assert.Equal("kernel_refused", denied.Error?.Code);
            Assert.Equal("unknown", denied.Error?.CommitState);
            Assert.Null(denied.Receipt);
        }
        Assert.Equal(2, (await f.StateAsync()).Alternatives.Count);
    }

    [Theory]
    [InlineData(RoleKind.Operator)]
    [InlineData(RoleKind.PlanningLead)]
    [InlineData(RoleKind.ImplementationLead)]
    public async Task ExistingLeadAndOperatorSupportRemains(RoleKind role)
    {
        using var f = new AlternativesFixture();
        await f.OpenAsync();
        var actor = new ActorId("lead");
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "assign", actor, role, RoleDefaults.For(role)));
        Assert.Null((await f.RecordAsync(binding: f.Binding with { ActorId = actor })).Error);
    }

    [Fact]
    public async Task TrustedBindingAndActualRunOwnershipAreRequiredOnEveryCall()
    {
        using var f = new AlternativesFixture();
        await f.OpenAsync();
        var author = new ActorId("author");
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "assign", author, RoleKind.Researcher,
            [Capability.RecordAlternative]));
        await f.ExecuteAsync(new RequestStageTransitionCommand(f.Actor, null, "research", TaskStage.Research,
            WithoutPrerequisitesReason: "Disposable alternatives attribution fixture"));
        await f.ExecuteAsync(new StartRunCommand(f.Actor, null, "start", new("R1"), null, "codex", null,
            SubjectActorId: author));
        var binding = f.Binding with { ActorId = author, RunId = new RunId("R1"), CorrelationId = "R1", AllowRunless = false };
        var first = await f.RecordAsync(binding: binding);
        Assert.Null(first.Error);
        foreach (var bad in new[] { binding with { AllowRecordAlternatives = false }, binding with { ActorId = f.Actor },
                     binding with { RunId = null }, binding with { CorrelationId = "wrong" },
                     binding with { CausationId = new EventId("missing") }, binding with { TaskId = new TaskId("../escape") } })
        {
            var result = await f.RecordAsync(binding: bad);
            Assert.Equal("authorization_denied", result.Error?.Code);
            Assert.Null(result.Receipt);
        }
        await f.ExecuteAsync(new CompleteRunCommand(f.Actor, null, "complete", new("R1"), AgentRunStatus.Completed,
            "observed-session", OutputTokens: 19, TokensInUncached: 31));
        Assert.True((await f.RecordAsync(binding: binding)).Replayed);
        var state = await f.StateAsync();
        Assert.Equal(19, state.Runs[new("R1")].OutputTokens);
        Assert.Equal(31, state.Runs[new("R1")].TokensInUncached);
        // Same key for another actor is a different submission, never disclosure of this receipt.
        var other = await f.RecordAsync();
        Assert.NotEqual(first.Receipt!.TransactionId, other.Receipt!.TransactionId);
        Assert.Equal(f.Actor.Value, other.Receipt.ActorId);
    }

    [Fact]
    public async Task ArchivedTaskReturnsReceiptWithoutReapplyingMutationStageRules()
    {
        using var f = new AlternativesFixture();
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
            Class: LessonClass.Untested, Repo: "fixture", Tags: ["alternatives"], Verify: "dotnet test",
            DoNot: "split receipt commit", Actor: LessonActor.Researcher, VerifyExpects: VerifyExpectation.Present));
        await f.ExecuteAsync(new RequestStageTransitionCommand(f.Actor, null, "archive", TaskStage.Archive,
            WithoutPrerequisitesReason: "Isolated storage retry fixture"));
        Assert.Equal(TaskStage.Archive, (await f.StateAsync()).Stage);
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var retry = await f.RecordAsync();
        Assert.True(retry.Replayed);
        Assert.Equal(first.Receipt!.LedgerVersion, retry.Receipt!.LedgerVersion);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
        // The current kernel permits recording alternatives at Archive. Preserve that command policy too.
        Assert.Null((await f.RecordAsync(AlternativesFixture.Single())).Error);
    }

    [Fact]
    public async Task RecalledLessonReferenceAndOriginalHistoricalBytesArePreserved()
    {
        using var f = new AlternativesFixture();
        var lesson = new Lesson(new("source:validatedclaim:C"), new("source"), LessonSourceKind.ValidatedClaim,
            "C", "Lesson", "validated", ["fixture"], new(f.Actor, DateTimeOffset.UtcNow, "stage.archive"));
        // The live service discovers recalled lessons from its configured store. This disposable
        // historical fixture uses the real handler, as the findings compatibility fixture does.
        var opening = new CommandHandler().Handle(null, new OpenTaskCommand(f.Actor, null, "open", f.TaskId,
            "Task", "Goal", RecalledLessons: [lesson]), DateTimeOffset.UtcNow);
        Directory.CreateDirectory(f.Directory);
        await File.WriteAllTextAsync(f.EventsPath, string.Join("\n", opening.Events.Select(e =>
            JsonSerializer.Serialize(e, LedgerJson.CreateOptions()))) + "\n");
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var request = AlternativesFixture.Single() with { Alternatives = [new("a", "Considered", "Rejected", FromLesson: lesson.Id.Value)] };
        var result = await f.RecordAsync(request);
        Assert.Null(result.Error);
        Assert.Equal(lesson.Id, (await f.StateAsync()).Alternatives[new(result.Receipt!.Alternatives[0].AlternativeId)].FromLesson);
        Assert.Equal(before, (await File.ReadAllBytesAsync(f.EventsPath))[..before.Length]);
    }
}
