using System.Text;
using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Lessons;

// PPC4 S4b (PD10): who may satisfy the reconsideration arm, and which role a consultation reads.
// R7 walks the production handler and the stage transition command; R8 replays a legacy event log
// through the durable service. Nothing places a stage, waives an arm or hand-builds a state.
public sealed class ReconsiderationConsultantTests
{
    private const string A3Refusal =
        "Scope after replanning requires a reconsideration lesson consultation by a completed lead run " +
        "with real cognition, recorded after the latest return from a later stage into Design or Research.";

    // R7 (reconsideration-consultation-needs-cognition): after a return into Design, a consultation
    // from a lead run that did no real work leaves Scope refused. The same fixture is admitted once a
    // completed real-provider lead run consults.
    [Theory]
    [InlineData("provider-none")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("active")]
    public void R7_ReconsiderationConsultationNeedsACompletedRunWithCognition(string consultant)
    {
        var task = new TestTask(placeEntryStages: false);
        task.ReachStage(TaskStage.Execution);
        task.Transition(TaskStage.Design);
        Assert.NotNull(task.State.ReconsiderationOpenedAtVersion);
        var lead = task.GoverningLead();
        var run = new RunId($"R-{consultant}");
        task.Apply(new StartRunCommand(task.OperatorId, null, task.NextCorrelation(), run, null,
            consultant == "provider-none" ? AgentRun.NoProvider : "codex", null, null, null, null, lead));
        task.ConsultLessons(lead, run, LessonConsultationPurpose.Reconsideration);
        var ending = consultant switch
        {
            "provider-none" => (AgentRunStatus?)AgentRunStatus.Completed,
            "failed" => AgentRunStatus.Failed,
            "cancelled" => AgentRunStatus.Cancelled,
            _ => null
        };
        if (ending is { } status)
        {
            task.Apply(new CompleteRunCommand(task.OperatorId, null, task.NextCorrelation(), run, status,
                consultant == "provider-none" ? null : $"session-{consultant}"));
        }
        Assert.Single(task.State.LessonConsultations,
            item => item.Purpose == LessonConsultationPurpose.Reconsideration && item.RunId == run);

        var error = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Scope));

        Assert.Equal(A3Refusal, error.Message);
        Assert.Equal(TaskStage.Design, task.State.Stage);
        var qualifying = task.StartGoverningRun("R-qualifying");
        task.ConsultLessons(lead, qualifying, LessonConsultationPurpose.Reconsideration);
        task.Apply(new CompleteRunCommand(task.OperatorId, null, task.NextCorrelation(), qualifying,
            AgentRunStatus.Completed, "session-qualifying"));
        task.Transition(TaskStage.Scope);
        Assert.Equal(TaskStage.Scope, task.State.Stage);
    }

    // R8 (consultation-role-at-run-start): a run whose run.started predates SubjectRole, replayed from
    // an event log, cannot consult. The refusal records nothing, and the same history still replays
    // to the stored state.json byte for byte.
    [Fact]
    public async Task R8_ConsultationRequiresARecordedSubjectRole()
    {
        using var root = new TemporaryDirectory();
        using var lessonRoot = new TemporaryDirectory();
        var taskId = new TaskId("2026-09-24_1400-legacy-run");
        var operatorId = new ActorId("operator");
        var legacyRun = new RunId("R-legacy");
        var service = Service(root.Path, lessonRoot.Path);
        await service.ExecuteAsync(taskId,
            new OpenTaskCommand(operatorId, null, "open", taskId, "Legacy run", "Consult", null, ["opening-only"]),
            CancellationToken.None);
        await service.ExecuteAsync(taskId,
            new AddClaimCommand(operatorId, null, "claim", new ClaimId("C1"), "Something to recon", null),
            CancellationToken.None);
        await service.ExecuteAsync(taskId,
            new RequestStageTransitionCommand(operatorId, null, "research", TaskStage.Research),
            CancellationToken.None);
        var taskDirectory = Path.Combine(root.Path, taskId.Value);
        var eventsPath = Path.Combine(taskDirectory, "events.jsonl");
        var statePath = Path.Combine(taskDirectory, "state.json");
        var before = (await service.GetStateAsync(taskId, CancellationToken.None))!;

        // Exactly the shape of a run recorded before runs carried the role they ran under: the
        // operator's own run, active, with no SubjectRole in the serialized event.
        var legacy = new LedgerEvent(
            GovernedTaskState.CurrentSchemaVersion,
            // The service's own event id sequence, which replay checks position by position.
            new EventId($"{taskId.Value}:{before.Version + 1:D10}"),
            taskId,
            operatorId,
            DateTimeOffset.UtcNow,
            null,
            "legacy-run",
            new RunStarted(new AgentRun(legacyRun, operatorId, null, "codex", null, AgentRunStatus.Active,
                DateTimeOffset.UtcNow, null)));
        var line = JsonSerializer.Serialize(legacy, LedgerJson.CreateOptions());
        Assert.DoesNotContain("subjectRole", line, StringComparison.OrdinalIgnoreCase);
        await File.AppendAllTextAsync(eventsPath, line + "\n", new UTF8Encoding(false));
        File.Delete(statePath);
        var replayed = (await service.GetStateAsync(taskId, CancellationToken.None))!;
        Assert.Equal(before.Version + 1, replayed.Version);
        Assert.Null(replayed.Runs[legacyRun].SubjectRole);
        Assert.Equal(AgentRunStatus.Active, replayed.Runs[legacyRun].Status);
        var storedEvents = await File.ReadAllBytesAsync(eventsPath);
        var storedState = await File.ReadAllBytesAsync(statePath);

        var error = await Assert.ThrowsAsync<GovernanceException>(() => service.ExecuteAsync(taskId,
            new ConsultLessonsCommand(operatorId, null, "consult", legacyRun, LessonConsultationPurpose.Recon,
                "What do earlier recons say?", ["recon"], []),
            CancellationToken.None));

        Assert.Equal(
            "A lesson consultation requires a run that recorded its subject role at start; run 'R-legacy' did not.",
            error.Message);
        Assert.Equal(storedEvents, await File.ReadAllBytesAsync(eventsPath));
        File.Delete(statePath);
        var again = (await Service(root.Path, lessonRoot.Path).GetStateAsync(taskId, CancellationToken.None))!;
        Assert.Empty(again.LessonConsultations);
        Assert.Equal(storedState, await File.ReadAllBytesAsync(statePath));
    }

    private static FileGovernedTaskService Service(string root, string lessonRoot)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(
            root,
            new CommandHandler(reducer, new AuthorizationPolicy()),
            reducer,
            lessonStore: new FileLessonStore(lessonRoot));
    }
}
