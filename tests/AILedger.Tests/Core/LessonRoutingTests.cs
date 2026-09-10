using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

// A lesson about how the kernel behaves is worth carrying to a later task in another repository, and
// a lesson about one role's mistake is worth nothing to the other six. These tests pin the three
// fields that say so — the kind, the audience, and which way the verify has to come out — and the
// one rule that reads the audience.
public sealed class LessonRoutingTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewMarkWithARunnableVerifyMustSayWhichWayItComesOut()
    {
        var task = MarkableTask();

        var error = Assert.Throws<GovernanceException>(() => task.Apply(Mark(task)));

        Assert.Contains("present or absent", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(task.State.LessonMarks);
    }

    // The one verify that has no direction to state. Demanding one here would close the escape hatch
    // that exists so a lesson does not have to invent a command, which is the worse failure.
    [Fact]
    public void AMarkThatRecordsThereIsNothingToRunTakesNoDirectionAndIsRefusedOne()
    {
        var refused = MarkableTask();
        var error = Assert.Throws<GovernanceException>(() => refused.Apply(Mark(
            refused,
            verify: "none - this is a scope judgement, not a fact about the code",
            expects: VerifyExpectation.Absent)));
        Assert.Contains("nothing can be run", error.Message, StringComparison.OrdinalIgnoreCase);

        var accepted = MarkableTask();
        accepted.Apply(Mark(
            accepted,
            verify: "none - this is a scope judgement, not a fact about the code",
            expects: null));

        Assert.Null(Assert.Single(accepted.State.LessonMarks).Value.VerifyExpects);
    }

    [Fact]
    public void AnAudienceIsCheckedAgainstRoleKindAndTheRolesLessonActorLacksAreAccepted()
    {
        var task = MarkableTask();

        // Worker and CodeReviewer have no LessonActor equivalent at all, which is the case that
        // proves the audience is not that vocabulary.
        task.Apply(Mark(
            task,
            expects: VerifyExpectation.Present,
            audience: [RoleKind.Worker, RoleKind.CodeReviewer]));

        Assert.Equal(
            [RoleKind.Worker, RoleKind.CodeReviewer],
            Assert.Single(task.State.LessonMarks).Value.Audience);
    }

    [Fact]
    public void ARepeatedAudienceRoleIsRefusedRatherThanCollapsed()
    {
        var task = MarkableTask();

        var error = Assert.Throws<GovernanceException>(() => task.Apply(Mark(
            task,
            expects: VerifyExpectation.Present,
            audience: [RoleKind.Verifier, RoleKind.Verifier])));

        Assert.Contains("audience", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(task.State.LessonMarks);
    }

    // Absent and empty both mean every role, so the record keeps one representation of it.
    [Fact]
    public void AnEmptyAudienceIsRecordedAsAbsent()
    {
        var task = MarkableTask();

        task.Apply(Mark(task, expects: VerifyExpectation.Present, audience: []));

        Assert.Null(Assert.Single(task.State.LessonMarks).Value.Audience);
    }

    [Fact]
    public void ALessonCarryingAnAudienceReachesOnlyTheRolesItNames()
    {
        var addressed = new LessonId("earlier-task:validatedclaim:C1");
        var everyone = new LessonId("earlier-task:validatedclaim:C2");
        var task = new TestTask();
        var worker = new ActorId("worker");
        var verifier = new ActorId("verifier");
        // Recall first: the replay rule accepts a recalled lesson only into a task holding nothing
        // but its opening role, so the two role assignments come after it.
        task.RecallLesson(Recalled(task.OperatorId, addressed, [RoleKind.Verifier]));
        task.RecallLesson(Recalled(task.OperatorId, everyone, null));
        task.Assign(worker, RoleKind.Worker, Capability.BuildContext);
        task.Assign(verifier, RoleKind.Verifier, Capability.BuildContext);
        var assembler = new ContextAssembler();

        var workerManifest = assembler.Build(task.State, worker, null, [], DateTimeOffset.UnixEpoch);
        var verifierManifest = assembler.Build(task.State, verifier, null, [], DateTimeOffset.UnixEpoch);

        Assert.DoesNotContain(workerManifest.Artifacts, artifact => artifact.Id == addressed.Value);
        Assert.Contains(verifierManifest.Artifacts, artifact => artifact.Id == addressed.Value);
        // The lesson with no audience is unaffected: that is what all 166 lessons already minted
        // carry, and narrowing them would change what every existing task inherits.
        Assert.Contains(workerManifest.Artifacts, artifact => artifact.Id == everyone.Value);
        Assert.Contains(verifierManifest.Artifacts, artifact => artifact.Id == everyone.Value);
    }

    // The audience is not carried in the artifact's RelatedIds, because those are what work-item
    // relevance is computed from: a role name there would widen which artifacts a work-scoped
    // manifest admits rather than narrowing one lesson (IC1).
    [Fact]
    public void AnAudienceDoesNotWidenWhatAWorkScopedManifestAdmits()
    {
        var addressed = new LessonId("earlier-task:validatedclaim:C1");
        var task = new TestTask();
        var verifier = new ActorId("verifier");
        task.RecallLesson(Recalled(task.OperatorId, addressed, [RoleKind.Verifier]));
        task.Assign(verifier, RoleKind.Verifier, Capability.BuildContext);
        task.Apply(new AddClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), new ClaimId("C-related"), "Depended on", null));
        task.Apply(new AddClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), new ClaimId("C-unrelated"), "Not depended on", null));
        task.Apply(new AddWorkItemCommand(
            task.OperatorId, null, task.NextCorrelation(), new WorkItemId("W1"), "Work", verifier,
            [new ClaimId("C-related")], []));

        var manifest = new ContextAssembler().Build(
            task.State, verifier, new WorkItemId("W1"), [], DateTimeOffset.UnixEpoch);

        Assert.Contains(manifest.Artifacts, artifact => artifact.Id == addressed.Value);
        Assert.Contains(manifest.Artifacts, artifact => artifact.Id == "C-related");
        Assert.DoesNotContain(manifest.Artifacts, artifact => artifact.Id == "C-unrelated");
    }

    // A field the manifest stores and never renders is a field no reader can act on, which is the
    // defect the stage-arms lesson about stored-but-unread tags records.
    [Fact]
    public void TheManifestRendersTheKindTheAudienceAndTheVerifyDirection()
    {
        var lessonId = new LessonId("earlier-task:validatedclaim:C1");
        var task = new TestTask();
        var verifier = new ActorId("verifier");
        task.RecallLesson(Recalled(task.OperatorId, lessonId, [RoleKind.Verifier]));
        task.Assign(verifier, RoleKind.Verifier, Capability.BuildContext);

        var manifest = new ContextAssembler().Build(task.State, verifier, null, [], DateTimeOffset.UnixEpoch);

        var rendered = Assert.Single(manifest.Artifacts, artifact => artifact.Id == lessonId.Value).Content;
        Assert.Contains("Kind: Workflow", rendered, StringComparison.Ordinal);
        Assert.Contains("Audience: Verifier", rendered, StringComparison.Ordinal);
        Assert.Contains("(expects Absent)", rendered, StringComparison.Ordinal);
    }

    // Nullable is a persistence fact and not a meaning. Every lesson minted before the kind existed
    // carries none, and a default that lives only in a comment leaves all of them unclassified to
    // the reader: nothing looking for Domain finds them. The absent case is resolved at the read,
    // which leaves the stored shape alone so replay keeps reading the histories on disk.
    [Fact]
    public void ALessonMintedBeforeTheKindExistedIsReadAsDomain()
    {
        var legacy = new LessonId("earlier-task:validatedclaim:C2");
        var task = new TestTask();
        var verifier = new ActorId("verifier");
        task.RecallLesson(Recalled(task.OperatorId, legacy, null));
        task.Assign(verifier, RoleKind.Verifier, Capability.BuildContext);

        var manifest = new ContextAssembler().Build(task.State, verifier, null, [], DateTimeOffset.UnixEpoch);

        Assert.Null(task.State.Lessons[legacy].Kind);
        Assert.Equal(LessonKind.Domain, task.State.Lessons[legacy].EffectiveKind());
        var rendered = Assert.Single(manifest.Artifacts, artifact => artifact.Id == legacy.Value).Content;
        Assert.Contains("Kind: Domain", rendered, StringComparison.Ordinal);
    }

    // Replay may never key on a field the histories already on disk do not carry, so a recalled
    // lesson has to read back with the three fields and without them. This is the reducer only.
    // The replay validator is exercised on the same two shapes through the real file store:
    // TaggedLessonRecallTests seeds store rows carrying none of the three, and
    // CliApplicationTests marks with all three and recalls the minted lesson into a second task.
    [Fact]
    public void ARecalledLessonReadsBackWhetherOrNotItCarriesTheRoutingFields()
    {
        var task = new TestTask();
        var withFields = new LessonId("earlier-task:validatedclaim:C1");
        var withoutFields = new LessonId("earlier-task:validatedclaim:C2");

        task.RecallLesson(Recalled(task.OperatorId, withFields, [RoleKind.Verifier]));
        task.RecallLesson(Recalled(task.OperatorId, withoutFields, null));

        Assert.Equal(LessonKind.Workflow, task.State.Lessons[withFields].Kind);
        Assert.Equal(VerifyExpectation.Absent, task.State.Lessons[withFields].VerifyExpects);
        Assert.Null(task.State.Lessons[withoutFields].Kind);
        Assert.Null(task.State.Lessons[withoutFields].Audience);
        Assert.Null(task.State.Lessons[withoutFields].VerifyExpects);
    }

    private static TestTask MarkableTask()
    {
        var task = new TestTask();
        task.Apply(new RecordAlternativeCommand(
            task.OperatorId, null, task.NextCorrelation(), new AlternativeId("ALT1"),
            "Grep for a symbol that exists either way", "It re-establishes nothing", null));
        return task;
    }

    private static MarkLessonBearingCommand Mark(
        TestTask task,
        string verify = "grep -n openingTags src/AILedger.Storage/FileGovernedTaskService.cs",
        VerifyExpectation? expects = null,
        IReadOnlyList<RoleKind>? audience = null,
        LessonKind? kind = LessonKind.Workflow) =>
        new(
            task.OperatorId, null, task.NextCorrelation(), LessonSourceKind.RejectedAlternative, "ALT1",
            Class: LessonClass.Refuted, Repo: "AILedger", Tags: ["lessons"],
            Verify: verify, DoNot: "Do not trust a check that cannot fail", Actor: LessonActor.Verifier,
            Kind: kind, Audience: audience, VerifyExpects: expects);

    // A lesson only reaches a task's state by being recalled into it or minted by it, so a manifest
    // test earns one the way recall does. A null audience also stands for the whole pre-routing
    // shape: no kind, no direction, no verify, which is what all 166 lessons already minted carry.
    private static Lesson Recalled(
        ActorId actor,
        LessonId lessonId,
        IReadOnlyList<RoleKind>? audience) =>
        new(
            lessonId,
            new TaskId("earlier-task"),
            LessonSourceKind.ValidatedClaim,
            lessonId.Value.Split(':')[^1],
            "Recall never reads the tags it stores",
            "Validated",
            ["src/AILedger.Storage/FileGovernedTaskService.cs:241-315"],
            new Provenance(actor, RecordedAt, "stage.archive"),
            null,
            LessonClass.Refuted,
            "AILedger",
            ["lessons", "recall"],
            audience is null ? null : "grep -n openingTags src/AILedger.Storage/FileGovernedTaskService.cs",
            audience is null ? null : "Do not assume a stored field is read",
            audience is null ? null : LessonActor.Verifier,
            audience is null ? null : LessonKind.Workflow,
            audience,
            audience is null ? null : VerifyExpectation.Absent);
}
