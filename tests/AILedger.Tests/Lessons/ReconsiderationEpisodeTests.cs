using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Lessons;

// R6 (reconsideration-episode-on-research-return), PPC3 S4a: a reconsideration episode opens on any
// return from a stage after Design into Design or Research (PD9). Every task here is walked by the
// production handler and the stage transition command; nothing places a stage or waives an arm.
public sealed class ReconsiderationEpisodeTests
{
    private const string A3Refusal =
        "Scope after replanning requires a reconsideration lesson consultation by a completed lead run " +
        "with real cognition, recorded after the latest return from a later stage into Design or Research.";

    // Null case: a task that never retreated into Design goes Execution->Research->Design->Scope. The
    // return into Research opens the episode, so Scope waits for a reconsideration consultation.
    [Fact]
    public void R6_ReturnIntoResearchFromExecutionRequiresAReconsiderationBeforeScope()
    {
        var task = ExecutingTask();
        Assert.Null(task.State.ReconsiderationOpenedAtVersion);
        task.Transition(TaskStage.Research);
        task.Transition(TaskStage.Design);

        var error = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Scope));

        Assert.Equal(A3Refusal, error.Message);
        Assert.Equal(TaskStage.Design, task.State.Stage);
        Reconsider(task, "reconsider-after-research");
        task.Transition(TaskStage.Scope);
        Assert.Equal(TaskStage.Scope, task.State.Stage);
    }

    // Stale case: a consultation from an earlier, finished replanning episode does not cover the
    // return into Research that follows it.
    [Fact]
    public void R6_ReconsiderationFromAnEarlierEpisodeDoesNotCoverAReturnIntoResearch()
    {
        var task = new TestTask(placeEntryStages: false);
        task.ReachStage(TaskStage.Scope);
        task.Transition(TaskStage.Design);
        Reconsider(task, "reconsider-first");
        task.Transition(TaskStage.Scope);
        task.ReachStage(TaskStage.Execution);
        task.Transition(TaskStage.Research);
        task.Transition(TaskStage.Design);

        var error = Assert.Throws<GovernanceException>(() => task.Transition(TaskStage.Scope));

        Assert.Equal(A3Refusal, error.Message);
        Assert.Single(task.State.LessonConsultations,
            item => item.Purpose == LessonConsultationPurpose.Reconsideration);
        Reconsider(task, "reconsider-second");
        task.Transition(TaskStage.Scope);
        Assert.Equal(TaskStage.Scope, task.State.Stage);
    }

    // Marker value: Execution->Research sets the marker to that event's version; the following
    // Research->Design keeps it.
    [Fact]
    public void R6_ReturnIntoResearchSetsTheMarkerAndTheFollowingDesignEntryKeepsIt()
    {
        var task = ExecutingTask();

        task.Transition(TaskStage.Research);
        var opened = task.State.Version;
        Assert.Equal(opened, task.State.ReconsiderationOpenedAtVersion);
        task.Transition(TaskStage.Design);

        Assert.Equal(opened, task.State.ReconsiderationOpenedAtVersion);
    }

    // Planning-internal edge: a Design->Research detour in first-pass planning has no strategy past
    // Scope to reconsider, so it opens nothing and Scope is admitted without a consultation.
    [Fact]
    public void R6_FirstPassDesignToResearchDetourOpensNoEpisode()
    {
        var task = new TestTask(placeEntryStages: false);
        task.ReachStage(TaskStage.Design);
        task.Transition(TaskStage.Research);
        task.Transition(TaskStage.Design);
        task.RecordPromptContract();

        task.Transition(TaskStage.Scope);

        Assert.Equal(TaskStage.Scope, task.State.Stage);
        Assert.Null(task.State.ReconsiderationOpenedAtVersion);
        Assert.DoesNotContain(task.State.LessonConsultations,
            item => item.Purpose == LessonConsultationPurpose.Reconsideration);
    }

    // Detour inside an open episode: Design->Research keeps the marker, so the one consultation made
    // after Execution->Design still covers the stay and Scope needs no second one.
    [Fact]
    public void R6_ResearchDetourInsideAnOpenEpisodeKeepsItOpen()
    {
        var task = ExecutingTask();
        task.Transition(TaskStage.Design);
        var opened = task.State.Version;
        Assert.Equal(opened, task.State.ReconsiderationOpenedAtVersion);
        Reconsider(task, "reconsider-once");

        task.Transition(TaskStage.Research);
        Assert.Equal(opened, task.State.ReconsiderationOpenedAtVersion);
        task.Transition(TaskStage.Design);
        task.Transition(TaskStage.Scope);

        Assert.Equal(TaskStage.Scope, task.State.Stage);
        Assert.Equal(opened, task.State.ReconsiderationOpenedAtVersion);
        Assert.Single(task.State.LessonConsultations,
            item => item.Purpose == LessonConsultationPurpose.Reconsideration);
    }

    // Replay: a history containing Execution->Research replays through the validator, and the
    // replayed state serializes byte-for-byte as the stored one.
    [Fact]
    public void R6_HistoryWithAReturnIntoResearchReplaysToTheSameState()
    {
        var task = ExecutingTask();
        task.Transition(TaskStage.Research);
        task.Transition(TaskStage.Design);
        Reconsider(task, "reconsider-replay");
        task.Transition(TaskStage.Scope);
        Assert.Contains(task.Events, item =>
            item.Data is StageTransitioned { Previous: TaskStage.Execution, Current: TaskStage.Research });

        var reducer = new TaskReducer();
        GovernedTaskState? replayed = null;
        foreach (var item in task.Events) replayed = reducer.Apply(replayed, item);

        var options = LedgerJson.CreateProjectionOptions();
        Assert.Equal(JsonSerializer.Serialize(task.State, options), JsonSerializer.Serialize(replayed, options));
    }

    private static TestTask ExecutingTask()
    {
        var task = new TestTask(placeEntryStages: false);
        task.ReachStage(TaskStage.Execution);
        return task;
    }

    // A3 (reconsideration-consultation-arm): a task-wide lead run consults in Design and closes.
    private static void Reconsider(TestTask task, string runId)
    {
        var run = task.StartGoverningRun(runId);
        task.ConsultLessons(task.GoverningLead(), run, LessonConsultationPurpose.Reconsideration);
        task.Apply(new CompleteRunCommand(task.OperatorId, null, task.NextCorrelation(), run,
            AgentRunStatus.Completed, $"session-{runId}"));
    }
}
