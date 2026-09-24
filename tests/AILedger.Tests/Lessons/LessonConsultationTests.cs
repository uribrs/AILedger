using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Artifacts.Recon;
using AILedger.Tests.Support;

namespace AILedger.Tests.Lessons;

// Mid-task lesson consultation (PPC2) through the production handler: what one consultation records,
// the three episode-bound arms that require one, and what replay accepts and refuses. Every task here
// is admitted by real commands; nothing places a stage or waives a prerequisite.
public sealed class LessonConsultationTests
{
    private const string A1Refusal =
        "InternalRecon: requires a recon lesson consultation by producer run 'recon-run' against the current claim set.";
    private const string A3Refusal =
        "Scope after replanning requires a reconsideration lesson consultation by a completed lead run " +
        "with real cognition, recorded after the latest return from a later stage into Design or Research.";

    // S1: one lesson.consulted event; the new lesson joins the task, reaches the next manifest of a
    // role it addresses, can be cited with --from-lesson, and the whole state replays byte-for-byte.
    [Fact]
    public void S1_ConsultationImportsTheLessonAndMakesItCitableAndBriefed()
    {
        var f = new InternalReconFixture();
        f.Start();
        var lesson = Lesson("earlier-task", "C1", null);
        var hashBefore = InternalReconDocuments.ComputeClaimSetHash(f.Task.State);

        var outcome = f.Task.ConsultLessons(f.Lead, f.Run, LessonConsultationPurpose.Recon,
            candidates: [lesson], tags: "recon");

        var consulted = Assert.IsType<LessonsConsulted>(Assert.Single(outcome.Events).Data);
        Assert.Equal([lesson.Id], consulted.ServedLessonIds);
        Assert.Equal([lesson.Id], consulted.NewLessons.Select(item => item.Id));
        Assert.Equal(hashBefore, consulted.ClaimSetHash);
        Assert.Equal(["recon"], consulted.Tags);
        Assert.Contains(lesson.Id, f.Task.State.Lessons.Keys);
        var consultation = Assert.Single(f.Task.State.LessonConsultations);
        Assert.Equal(f.Task.State.Version, consultation.Version);
        Assert.Equal(f.Run, consultation.RunId);
        Assert.Equal(f.Lead, consultation.ActorId);
        Assert.Equal(outcome.Events[0].EventId, consultation.EventId);

        var manifest = new ContextAssembler().Build(f.Task.State, f.Lead, null, [], DateTimeOffset.UnixEpoch);
        Assert.Contains(manifest.Artifacts, artifact =>
            artifact.Kind == ContextArtifactKind.Lesson && artifact.Id == lesson.Id.Value);

        f.Task.Apply(new AddClaimCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(),
            new ClaimId("C-from-lesson"), "The earlier finding holds here", null, FromLesson: lesson.Id));
        Assert.Contains(new ClaimId("C-from-lesson"), f.Task.State.Claims.Keys);

        var replayed = Replay(f.Task.Events);
        var options = LedgerJson.CreateProjectionOptions();
        Assert.Equal(JsonSerializer.Serialize(f.Task.State, options), JsonSerializer.Serialize(replayed, options));
    }

    // A candidate the store served that has never reached the task cannot be cited: consultation is
    // the only way in, and the citation rule is unchanged (PC2).
    [Fact]
    public void ALessonThatWasNeverConsultedCannotBeCited()
    {
        var f = new InternalReconFixture();
        Assert.Throws<GovernanceException>(() => f.Task.Apply(new AddClaimCommand(
            f.Task.OperatorId, null, f.Task.NextCorrelation(), new ClaimId("C-uncited"), "Unfounded", null,
            FromLesson: new LessonId("earlier-task:validatedclaim:C1"))));
    }

    // R3 (empty-consultation-satisfies-arms), S2: a young corpus serves nothing, and each arm still
    // passes on the consultation rather than on what it served.
    [Fact]
    public void R3_ZeroLessonConsultationSatisfiesEachArm()
    {
        var f = new InternalReconFixture();
        f.Resolve();
        ResearchPass(f, "RR1", [new ClaimId("C-topic")]);
        f.Eligible("external");                                  // A1 on an empty consultation
        f.Task.Transition(TaskStage.Design);                     // A2 on an empty consultation
        f.Task.RecordPromptContract();
        f.Task.Transition(TaskStage.Scope);
        f.Task.Transition(TaskStage.Design);
        f.Reconsider("reconsider-1");
        f.Task.Transition(TaskStage.Scope);                      // A3 on an empty consultation

        Assert.Equal(TaskStage.Scope, f.Task.State.Stage);
        Assert.Equal(
            [LessonConsultationPurpose.Research, LessonConsultationPurpose.Recon, LessonConsultationPurpose.Reconsideration],
            f.Task.State.LessonConsultations.Select(item => item.Purpose));
        Assert.All(f.Task.State.LessonConsultations, item => Assert.Empty(item.ServedLessonIds));
        Assert.Empty(f.Task.State.Lessons);
    }

    // S4, A1: refused without a consultation, admitted with one, with the refusal text PPC2 fixes.
    [Fact]
    public void A1_ReconIsRefusedWithoutAConsultationByItsProducerRun()
    {
        var f = new InternalReconFixture();
        f.Start();
        var version = f.Task.State.Version;

        var error = Assert.Throws<GovernanceException>(() => f.File(consult: false));

        Assert.Equal(A1Refusal, error.Message);
        Assert.Equal(version, f.Task.State.Version);
        f.Consult();
        f.File(consult: false);
        Assert.Single(f.Task.State.Artifacts);
    }

    // R4 (stale-episode-consultation-refused), recon: a claim change after consulting leaves the
    // consultation bound to a claim set the document no longer describes.
    [Fact]
    public void R4_ReconConsultationBeforeAClaimChangeIsRefused()
    {
        var f = new InternalReconFixture();
        f.Start();
        f.Consult();
        f.Task.Apply(new AddClaimCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(),
            new ClaimId("C-finding"), "Recon found a second question", null));

        var error = Assert.Throws<GovernanceException>(() => f.File(f.Body(), consult: false));

        Assert.Equal(A1Refusal, error.Message);
        f.Consult();
        f.File(f.Body(), consult: false);
        Assert.Single(f.Task.State.Artifacts);
    }

    // R4, recon: another run's consultation is not the producer's.
    [Fact]
    public void R4_ReconConsultationByAnotherRunIsRefused()
    {
        var f = new InternalReconFixture();
        f.Start("other-run");
        f.Consult();
        f.Complete();
        f.Start();

        var error = Assert.Throws<GovernanceException>(() => f.File(consult: false));

        Assert.Equal(A1Refusal, error.Message);
    }

    // R4, research: a consultation from before the latest entry into Research belongs to an earlier
    // episode, even though it names the claim and its run completed.
    [Fact]
    public void R4_ResearchConsultationBeforeTheLatestResearchEntryIsRefused()
    {
        var f = new InternalReconFixture();
        f.Resolve();
        ResearchPass(f, "RR1", [new ClaimId("C-topic")]);
        f.Eligible("external");
        f.Task.Transition(TaskStage.Design);
        f.Task.Transition(TaskStage.Research);

        var error = Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Design));

        Assert.Contains("research lesson consultation naming external claim", error.Message, StringComparison.Ordinal);
        Assert.Contains("C-topic", error.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStage.Research, f.Task.State.Stage);
        ResearchPass(f, "RR2", [new ClaimId("C-topic")]);
        f.Task.Transition(TaskStage.Design);
        Assert.Equal(TaskStage.Design, f.Task.State.Stage);
    }

    // R4, research: a consultation that does not name the external claim does not cover it.
    [Fact]
    public void R4_ResearchConsultationNotNamingTheExternalClaimIsRefused()
    {
        var f = new InternalReconFixture();
        f.Task.Apply(new AddClaimCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(),
            new ClaimId("C-two"), "A second external dependency", null));
        f.Resolve();
        Validate(f.Task, "C-two");
        ResearchPass(f, "RR1", [new ClaimId("C-topic")]);
        f.Eligible("external");

        var error = Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Design));

        Assert.Contains("C-two", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("C-topic", error.Message, StringComparison.Ordinal);
        ResearchPass(f, "RR2", [new ClaimId("C-two")]);
        f.Task.Transition(TaskStage.Design);
    }

    // R4, research: the run that consulted must have completed with real cognition. Active, failed and
    // cancelled runs are all refused; the same consultation passes once its run completes.
    [Theory]
    [InlineData(null)]
    [InlineData(AgentRunStatus.Failed)]
    [InlineData(AgentRunStatus.Cancelled)]
    public void R4_ResearchConsultationByARunThatDidNotCompleteIsRefused(AgentRunStatus? status)
    {
        var f = new InternalReconFixture();
        f.Resolve();
        ResearchPass(f, "RR1", [new ClaimId("C-topic")], status);
        f.Eligible("external");

        var error = Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Design));

        Assert.Contains("by a completed Researcher run in the current research episode", error.Message,
            StringComparison.Ordinal);
        if (status is null)
        {
            f.Task.Apply(new CompleteRunCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(),
                new RunId("RR1"), AgentRunStatus.Completed, "session-RR1"));
            f.Task.Transition(TaskStage.Design);
            Assert.Equal(TaskStage.Design, f.Task.State.Stage);
        }
    }

    // An internal-only recon asks nothing of research, so no consultation is required for Design.
    [Fact]
    public void InternalOnlyReconNeedsNoResearchConsultation()
    {
        var f = new InternalReconFixture();
        f.Eligible();
        f.Task.Transition(TaskStage.Design);
        Assert.DoesNotContain(f.Task.State.LessonConsultations,
            item => item.Purpose == LessonConsultationPurpose.Research);
    }

    // S4, A3 and R4 for reconsideration: each backward entry into Design opens a new reconsideration.
    // Leaving for Scope is refused until a consultation is recorded after that entry.
    [Fact]
    public void R4_ReconsiderationBeforeTheLatestBackwardDesignEntryIsRefused()
    {
        var f = new InternalReconFixture();
        f.Eligible();
        f.Task.Transition(TaskStage.Design);
        f.Task.RecordPromptContract();
        // A forward stay in Design is not a reconsideration.
        f.Task.Transition(TaskStage.Scope);
        Assert.Null(f.Task.State.ReconsiderationOpenedAtVersion);

        f.Task.Transition(TaskStage.Design);
        Assert.Equal(f.Task.State.Version, f.Task.State.ReconsiderationOpenedAtVersion);
        Assert.Equal(A3Refusal, Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Scope)).Message);
        f.Reconsider("reconsider-1");
        f.Task.Transition(TaskStage.Scope);

        f.Task.Transition(TaskStage.Design);
        Assert.Equal(A3Refusal, Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Scope)).Message);
        f.Reconsider("reconsider-2");
        f.Task.Transition(TaskStage.Scope);
        Assert.Equal(TaskStage.Scope, f.Task.State.Stage);
    }

    // The two episode markers are derived from transitions: any entry into Research moves one, and
    // only a backward entry into Design moves the other.
    [Fact]
    public void EpisodeMarkersFollowTheTransitionsThatOpenThem()
    {
        var f = new InternalReconFixture();
        var researchEntry = f.Task.Events.Last(item => item.Data is StageTransitioned).EventId;
        Assert.Equal(VersionOf(f.Task, researchEntry), f.Task.State.ResearchOpenedAtVersion);
        f.Eligible();
        f.Task.Transition(TaskStage.Design);
        Assert.Null(f.Task.State.ReconsiderationOpenedAtVersion);
        var researchOpened = f.Task.State.ResearchOpenedAtVersion;

        f.Task.Transition(TaskStage.Research);
        Assert.Equal(f.Task.State.Version, f.Task.State.ResearchOpenedAtVersion);
        Assert.NotEqual(researchOpened, f.Task.State.ResearchOpenedAtVersion);
        Assert.Null(f.Task.State.ReconsiderationOpenedAtVersion);
    }

    // R5 (reconsideration-reserves-visible-lessons), S3: reconsideration reviews every task lesson the
    // consulting role may see, plus new candidates it may see. A lesson whose audience excludes the
    // role is neither re-served nor imported, and nor is a lesson from this task itself.
    [Fact]
    public void R5_ReconsiderationReservesVisibleTaskLessonsOnly()
    {
        var task = new TestTask();
        var open = Lesson("earlier-task", "C1", null);
        var addressed = Lesson("earlier-task", "C2", [RoleKind.PlanningLead, RoleKind.Verifier]);
        var excluded = Lesson("earlier-task", "C3", [RoleKind.Verifier]);
        task.RecallLesson(open);
        task.RecallLesson(addressed);
        task.RecallLesson(excluded);
        var lead = task.GoverningLead();
        var run = task.StartGoverningRun("R-reconsider");
        var fresh = Lesson("other-task", "C1", []);
        var freshExcluded = Lesson("other-task", "C2", [RoleKind.Worker]);
        var ownTask = Lesson(task.TaskId.Value, "C9", null);

        var outcome = task.ConsultLessons(lead, run, LessonConsultationPurpose.Reconsideration,
            candidates: [freshExcluded, fresh, ownTask], tags: "strategy");

        var consulted = Assert.IsType<LessonsConsulted>(Assert.Single(outcome.Events).Data);
        Assert.Equal(
            new[] { open.Id, addressed.Id, fresh.Id }.OrderBy(id => id.Value, StringComparer.Ordinal),
            consulted.ServedLessonIds);
        Assert.Equal([fresh.Id], consulted.NewLessons.Select(item => item.Id));
        Assert.DoesNotContain(excluded.Id, consulted.ServedLessonIds);
        Assert.DoesNotContain(freshExcluded.Id, task.State.Lessons.Keys);
        Assert.DoesNotContain(ownTask.Id, task.State.Lessons.Keys);
        Assert.Contains(excluded.Id, task.State.Lessons.Keys);
    }

    // Recon and research serve only new candidates; a lesson already in the task is not re-served.
    [Fact]
    public void ReconServesOnlyCandidatesAndDoesNotReimportATaskLesson()
    {
        var task = new TestTask();
        var recalled = Lesson("earlier-task", "C1", null);
        task.RecallLesson(recalled);
        var lead = task.GoverningLead();
        var run = task.StartGoverningRun("R-recon-consult");
        var fresh = Lesson("other-task", "C1", null);

        var outcome = task.ConsultLessons(lead, run, LessonConsultationPurpose.Recon,
            candidates: [recalled, fresh], tags: "recon");

        var consulted = Assert.IsType<LessonsConsulted>(Assert.Single(outcome.Events).Data);
        Assert.Equal(
            new[] { recalled.Id, fresh.Id }.OrderBy(id => id.Value, StringComparer.Ordinal),
            consulted.ServedLessonIds);
        Assert.Equal([fresh.Id], consulted.NewLessons.Select(item => item.Id));
        Replay(task.Events);
    }

    // Command-time refusals: nothing is recorded, whatever the reason.
    [Theory]
    [InlineData("blank-question")]
    [InlineData("no-tags")]
    [InlineData("blank-tag")]
    [InlineData("duplicate-tag")]
    [InlineData("unknown-claim")]
    [InlineData("duplicate-claim")]
    [InlineData("research-without-claims")]
    [InlineData("research-by-lead")]
    [InlineData("recon-by-researcher")]
    [InlineData("reconsideration-at-research")]
    [InlineData("completed-run")]
    [InlineData("another-actors-run")]
    [InlineData("unknown-run")]
    [InlineData("duplicate-candidate")]
    [InlineData("undefined-purpose")]
    public void AnInvalidConsultationIsRefusedWithoutRecordingAnEvent(string defect)
    {
        var f = new InternalReconFixture();
        f.Start();
        var researcher = new ActorId("researcher");
        f.Task.Assign(researcher, RoleKind.Researcher, Capability.BuildContext);
        var researchRun = new RunId("RR-refusal");
        f.Task.Apply(new StartRunCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(), researchRun,
            null, "codex", null, null, null, null, researcher));
        var topic = new ClaimId("C-topic");
        var lesson = Lesson("earlier-task", "C1", null);
        if (defect == "completed-run") f.Complete();
        var command = new ConsultLessonsCommand(f.Lead, null, f.Task.NextCorrelation(), f.Run,
            LessonConsultationPurpose.Recon, "What do earlier recons say?", ["recon"], []);
        command = defect switch
        {
            "blank-question" => command with { Question = "  " },
            "no-tags" => command with { Tags = [] },
            "blank-tag" => command with { Tags = ["recon", " "] },
            "duplicate-tag" => command with { Tags = ["recon", " recon "] },
            "unknown-claim" => command with { ClaimIds = [new ClaimId("C-unknown")] },
            "duplicate-claim" => command with { ClaimIds = [topic, topic] },
            "research-without-claims" => command with
            {
                ActorId = researcher, RunId = researchRun, Purpose = LessonConsultationPurpose.Research
            },
            "research-by-lead" => command with { Purpose = LessonConsultationPurpose.Research, ClaimIds = [topic] },
            "recon-by-researcher" => command with { ActorId = researcher, RunId = researchRun },
            "reconsideration-at-research" => command with { Purpose = LessonConsultationPurpose.Reconsideration },
            "another-actors-run" => command with { ActorId = f.Task.OperatorId },
            "unknown-run" => command with { RunId = new RunId("ghost") },
            "duplicate-candidate" => command with { Candidates = [lesson, lesson] },
            "undefined-purpose" => command with { Purpose = (LessonConsultationPurpose)42 },
            _ => command
        };
        var version = f.Task.State.Version;

        Assert.Throws<GovernanceException>(() => f.Task.Apply(command));

        Assert.Equal(version, f.Task.State.Version);
        Assert.Empty(f.Task.State.LessonConsultations);
        Assert.Empty(f.Task.State.Lessons);
    }

    // A research consultation by a Researcher run naming its claims is admitted at Research.
    [Fact]
    public void AResearchConsultationRecordsTheClaimsItNames()
    {
        var f = new InternalReconFixture();
        f.Task.Apply(new AddClaimCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(),
            new ClaimId("A-first"), "Sorted before C-topic", null));
        var run = ResearchPass(f, "RR1", [new ClaimId("C-topic"), new ClaimId("A-first")]);

        var consultation = Assert.Single(f.Task.State.LessonConsultations);
        Assert.Equal(run, consultation.RunId);
        Assert.Equal(LessonConsultationPurpose.Research, consultation.Purpose);
        Assert.Equal([new ClaimId("A-first"), new ClaimId("C-topic")], consultation.ClaimIds);
    }

    // R2 (replay-compatibility-kept): a recon filed before consultation existed replays. The arm is
    // command-time only, so removing the consultation from a history leaves a history that was legal.
    [Fact]
    public void R2_HistoricalReconWithoutConsultationStillReplays()
    {
        var f = new InternalReconFixture();
        f.Eligible();
        f.Task.Transition(TaskStage.Design);
        var legacy = f.Task.Events.Where(item => item.Data is not LessonsConsulted).ToArray();
        Assert.True(legacy.Length < f.Task.Events.Count);

        var replayed = Replay(legacy);

        Assert.Equal(TaskStage.Design, replayed.Stage);
        Assert.Single(replayed.Artifacts.Values, item => item.Kind == GovernedArtifactKind.InternalRecon);
        Assert.Empty(replayed.LessonConsultations);
    }

    // R2: the three new properties sit after CoordinatorSessions, in contract order, at the end of the
    // record. An old history projects the same leading shape it always did.
    [Fact]
    public void R2_LegacyHistoryStateJsonKeepsPropertyOrder()
    {
        var f = new InternalReconFixture();
        f.Eligible();
        f.Task.Transition(TaskStage.Design);
        f.Task.RecordPromptContract();
        f.Task.Transition(TaskStage.Scope);
        f.Task.Transition(TaskStage.Design);
        var options = LedgerJson.CreateProjectionOptions();

        var names = PropertyNames(JsonSerializer.Serialize(f.Task.State, options));
        Assert.Equal(
            ["coordinatorSessions", "lessonConsultations", "reconsiderationOpenedAtVersion", "researchOpenedAtVersion"],
            names.TakeLast(4));

        var legacy = Replay(f.Task.Events.Where(item => item.Data is not LessonsConsulted).ToArray());
        var legacyNames = PropertyNames(JsonSerializer.Serialize(legacy, options));
        Assert.Equal(names, legacyNames);
        Assert.Equal("coordinatorSessions", legacyNames[^4]);
    }

    // S7: a forged lesson.consulted fails replay. Each case starts from a valid event the handler
    // produced and changes one thing about it; the unchanged event is the control.
    [Theory]
    [InlineData("control")]
    [InlineData("unknown-run")]
    [InlineData("served-missing")]
    [InlineData("new-not-served")]
    [InlineData("already-present")]
    [InlineData("own-task")]
    [InlineData("wrong-hash")]
    [InlineData("research-without-claims")]
    [InlineData("unknown-claim")]
    [InlineData("blank-question")]
    [InlineData("no-tags")]
    [InlineData("inactive-run")]
    [InlineData("other-actor")]
    [InlineData("undefined-purpose")]
    public void S7_ForgedConsultationEventsFailReplay(string forgery)
    {
        var f = new InternalReconFixture();
        f.Start();
        var lesson = Lesson("earlier-task", "C1", null);
        f.Task.ConsultLessons(f.Lead, f.Run, LessonConsultationPurpose.Recon, candidates: [lesson], tags: "recon");
        // For an inactive run, the real completion stays in the prefix and the consultation moves after it.
        if (forgery == "inactive-run") f.Complete();
        var events = f.Task.Events.ToList();
        var index = events.FindIndex(item => item.Data is LessonsConsulted);
        var original = events[index];
        var data = (LessonsConsulted)original.Data;
        var own = Lesson(f.Task.TaskId.Value, "C1", null);
        var prefix = forgery == "inactive-run"
            ? events.Where(item => item.Data is not LessonsConsulted).ToList()
            : events.Take(index).ToList();
        LedgerEventData forged = forgery switch
        {
            "unknown-run" => data with { RunId = new RunId("ghost") },
            "served-missing" => data with { ServedLessonIds = [lesson.Id, new LessonId("nowhere:validatedclaim:Q")] },
            "new-not-served" => data with { ServedLessonIds = [] },
            "own-task" => data with { ServedLessonIds = [own.Id], NewLessons = [own] },
            "wrong-hash" => data with { ClaimSetHash = new string('0', 64) },
            "research-without-claims" => data with { Purpose = LessonConsultationPurpose.Research, ClaimIds = [] },
            "unknown-claim" => data with { ClaimIds = [new ClaimId("C-unknown")] },
            "blank-question" => data with { Question = " " },
            "no-tags" => data with { Tags = [] },
            "undefined-purpose" => data with { Purpose = (LessonConsultationPurpose)42 },
            _ => data
        };
        var appended = original with { Data = forged };
        if (forgery == "already-present")
        {
            prefix.Add(original);
            appended = original with { EventId = new EventId("replayed-consultation") };
        }
        if (forgery == "other-actor")
        {
            appended = appended with { ActorId = f.Task.OperatorId };
        }
        if (forgery == "inactive-run")
        {
            appended = appended with { RecordedAt = events[^1].RecordedAt };
        }
        var state = Replay(prefix);

        if (forgery == "control")
        {
            Assert.Contains(lesson.Id, new TaskReducer().Apply(state, appended).Lessons.Keys);
            return;
        }
        var error = Assert.Throws<GovernanceException>(() => new TaskReducer().Apply(state, appended));
        // Where the validator names the defect, the refusal must be that defect and not an accident.
        var expected = forgery switch
        {
            "served-missing" => "is neither in the task nor newly consulted",
            "new-not-served" => "must be among the served lessons",
            "already-present" => "is already present in the task",
            "own-task" => "cannot consult its own lesson",
            "wrong-hash" => "claim-set hash",
            "research-without-claims" => "must name at least one claim",
            "inactive-run" or "other-actor" => "must name an active run of the consulting actor",
            _ => null
        };
        if (expected is not null)
        {
            Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        }
    }

    private static RunId ResearchPass(
        InternalReconFixture f,
        string runId,
        IReadOnlyList<ClaimId> claims,
        AgentRunStatus? status = AgentRunStatus.Completed)
    {
        var researcher = new ActorId("researcher");
        if (!f.Task.State.Roles.ContainsKey(researcher))
        {
            f.Task.Assign(researcher, RoleKind.Researcher, Capability.BuildContext);
        }
        var run = new RunId(runId);
        f.Task.Apply(new StartRunCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(), run,
            null, "codex", null, null, null, null, researcher));
        f.Task.ConsultLessons(researcher, run, LessonConsultationPurpose.Research, claims, tags: "research");
        if (status is { } final)
        {
            f.Task.Apply(new CompleteRunCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(), run,
                final, $"session-{runId}"));
        }
        return run;
    }

    private static void Validate(TestTask task, string claimId)
    {
        var claim = new ClaimId(claimId);
        var evidence = new EvidenceId($"proof-{claimId}");
        task.Apply(new AddEvidenceCommand(task.OperatorId, null, task.NextCorrelation(), evidence,
            "test-run", "ExternalProbe", "Directional proof", [claim], []));
        task.Apply(new ResolveClaimCommand(task.OperatorId, null, task.NextCorrelation(), claim,
            ClaimStatus.Validated, [evidence]));
    }

    private static long VersionOf(TestTask task, EventId eventId) =>
        task.Events.ToList().FindIndex(item => item.EventId == eventId) + 1;

    private static GovernedTaskState Replay(IEnumerable<LedgerEvent> events)
    {
        var reducer = new TaskReducer();
        GovernedTaskState? state = null;
        foreach (var item in events) state = reducer.Apply(state, item);
        return state!;
    }

    private static string[] PropertyNames(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    }

    // The shape a lesson arrives in from the store: minted by its source task's archive.
    internal static Lesson Lesson(string sourceTask, string record, IReadOnlyList<RoleKind>? audience) =>
        new(
            new LessonId($"{sourceTask}:validatedclaim:{record}"),
            new TaskId(sourceTask),
            LessonSourceKind.ValidatedClaim,
            record,
            $"{sourceTask} established {record}",
            "Validated",
            [$"src/AILedger.Core/Lessons/Logic/LessonConsultationRules.cs:{record}"],
            new Provenance(new ActorId("operator"), new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
                "stage.archive"),
            Class: LessonClass.Refuted,
            Repo: "AILedger",
            Tags: ["recon", "research", "strategy"],
            Audience: audience);
}
