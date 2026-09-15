using AILedger.Cli;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Tests.Cli;

// CLI tests that target rules other than stage admission still traverse the production command
// path. The prerequisite waiver keeps their setup focused without bypassing the entry-action gate:
// every move is recorded, authorized, and validated by the same CLI the test exercises.
internal static class CliStageFixture
{
    internal static Task ToReadyAsync(CliApplication application, string root, string task = "T1") =>
        AdvanceAsync(application, root, task,
            TaskStage.Research, TaskStage.Design, TaskStage.Scope, TaskStage.Ready);

    internal static Task ToExecutionAsync(CliApplication application, string root, string task = "T1") =>
        AdvanceAsync(application, root, task, TaskStage.Execution);

    internal static Task ToVerificationAsync(CliApplication application, string root, string task = "T1") =>
        AdvanceAsync(application, root, task, TaskStage.Verification);

    internal static Task ToReviewAsync(CliApplication application, string root, string task = "T1") =>
        AdvanceAsync(application, root, task, TaskStage.Review);

    internal static async Task BackAsync(
        CliApplication application,
        string root,
        TaskStage stage,
        string task = "T1")
    {
        var exit = await application.RunAsync(
            ["stage", "transition", "--root", root, "--task", task, "--actor", "operator",
             "--stage", stage.ToString(), "--reason", "Fixture returns to revise a governing artifact",
             "--without-prerequisites", "Fixture setup for a CLI rule other than transition prerequisites"],
            CancellationToken.None);
        if (exit != 0)
        {
            throw new InvalidOperationException(
                $"Could not return CLI fixture task '{task}' to stage '{stage}'.");
        }
    }

    internal static async Task AdvanceAsync(
        CliApplication application,
        string root,
        string task,
        params TaskStage[] stages)
    {
        foreach (var stage in stages)
        {
            var exit = await application.RunAsync(
                ["stage", "transition", "--root", root, "--task", task, "--actor", "operator",
                 "--stage", stage.ToString(), "--without-prerequisites",
                 "Fixture setup for a CLI rule other than transition prerequisites"],
                CancellationToken.None);
            if (exit != 0)
            {
                throw new InvalidOperationException(
                    $"Could not place CLI fixture task '{task}' at stage '{stage}'.");
            }
        }
    }
}
