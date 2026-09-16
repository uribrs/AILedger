using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Cli;

[Collection(StandardInput.Collection)]
public sealed class TaskStatusCliTests
{
    // LC3: the owed node had no test at its call site, so acceptance criteria 5 and 6 were
    // unproven — nothing pinned the node being absent on a clean task, and nothing pinned it
    // carrying counts and nothing else. VC1's partial-citation defect lived in exactly that gap.
    [Fact]
    public async Task StatusOmitsTheOwedNodeWhenTheTaskOwesNothing()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var application = new CliApplication(
            output, TextWriter.Null, Service,
            _ => new FixedResultAdapter(AgentRunStatus.Completed), new ContextAssembler());
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None);

        // Every command writes to the same writer, so the setup calls have to be cleared or the
        // parse sees two documents.
        output.GetStringBuilder().Clear();
        await application.RunAsync(["status", "--root", root.Path, "--task", "T1"], CancellationToken.None);

        Assert.Null(JsonNode.Parse(output.ToString())!["owed"]);
    }

    [Fact]
    public async Task StatusReportsOwedCountsAndNoDerivedVerdict()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var application = new CliApplication(
            output, TextWriter.Null, Service,
            _ => new FixedResultAdapter(AgentRunStatus.Completed), new ContextAssembler());
        await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["claim", "add", "--root", root.Path, "--task", "T1", "--actor", "operator",
             "--id", "C1", "--statement", "Unearned assumption"], CancellationToken.None);

        output.GetStringBuilder().Clear();
        await application.RunAsync(["status", "--root", root.Path, "--task", "T1"], CancellationToken.None);

        var owed = JsonNode.Parse(output.ToString())!["owed"]!.AsObject();
        Assert.Equal(1, owed["openClaims"]!.GetValue<int>());
        // Counts, and one fact. No score, no colour, no health word, and no field whose value is
        // fixed by the condition under which the node is written. The retrospective debt is a
        // boolean because whether an archived task carries a retrospective is a yes or a no, and a
        // count that can only be zero or one would read as a measure of something.
        // Two facts joined the one: whether a coordinating session is open, because a waiver made
        // with none records origin 'manual' and a coordinator that never opens one cannot be told
        // from the operator; and which stage the task's own records imply, because the stage arms
        // fire only on a transition nothing asks for. Both are facts, neither is a judgement.
        // stageBehindActivity is present here because this fixture records a claim and a work item
        // while sitting in Discovery, which is the drift the field exists to name.
        // workItemsRunByNoWorkingRole is a count and reads zero on this fixture, which holds no work
        // item at all. What it says here is only that the field reaches the output; the value that
        // matters is pinned by StatusCountsAWorkItemWhoseOnlyRunDidNoWorkTheGateAccepts below.
        Assert.Equal(
            new[] { "coordinatorSessionOpen", "lessonsCited", "lessonsRecalled", "openClaims", "openClaimsWithSupportingEvidence", "retrospectiveOwed", "stageBehindActivity", "workItemsAwaitingCodeReview", "workItemsAwaitingVerification", "workItemsRunByNoWorkingRole" },
            owed.Select(pair => pair.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray());
        Assert.Equal(0, owed["workItemsAwaitingCodeReview"]!.GetValue<int>());
    }

    // The value, at the surface an operator reads. TaskDebtTests pins the projection; what no test
    // on the projection can show is that the number survives serialisation and that the task stops
    // reading clear because of it, which is the whole of what this field was added to do.
    //
    // The state is built with a verifier and no worker on purpose. The obvious way to make an item
    // "run by no working role" — give it a coordinating run — is no longer reachable: RunDispatchRules
    // refuses a run against a work item held by an Operator, PlanningLead or ImplementationLead
    // outright. Verifier and CodeReviewer are neither coordinating nor working, so they are the live
    // path, and a verifier is the one of the two that can start with no work behind it. So the field
    // is not a reader of old histories only: this sequence is four ordinary commands, each of which
    // the kernel accepts today, and it ends with an item 'work complete' refuses.
    [Fact]
    public async Task StatusCountsAWorkItemWhoseOnlyRunDidNoWorkTheGateAccepts()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var output = new StringWriter();
        var error = new StringWriter();
        var application = Create(output, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "verifier", "--role", "verifier"],
            CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Unworked work", "--owner", "operator",
             "--scope", area], CancellationToken.None);
        // A verifier output is refused without a current prompt contract behind it, so the task-wide
        // artifacts are filed first. They are filed by a coordinating run that names no work item,
        // which is the only kind such a role may hold, so they add nothing to either count below.
        await RecordExecutionArtifactsAsync(application, common);
        await CliStageFixture.ToExecutionAsync(application, root.Path);
        await CliStageFixture.ToVerificationAsync(application, root.Path);
        // No worker run precedes this one. The verifier has nothing to read, and the kernel does not
        // refuse it — that is the hole, not an artificial fixture.
        await application.RunAsync(
            ["run", "start", .. common, "--subject", "verifier", "--run", "RV", "--work", "W1",
             "--provider", "claude", "--session", "verifier-session"], CancellationToken.None);
        await RecordArtifactAsync(
            application, ["--root", root.Path, "--task", "T1"], "verifier", "A-RV", "verifier-output",
            "W1", "RV", ArtifactCommands.VerifierBody);
        await application.RunAsync(
            ["run", "complete", .. common, "--run", "RV", "--status", "completed",
             "--session", "verifier-session"], CancellationToken.None);
        Assert.Equal(string.Empty, error.ToString());

        output.GetStringBuilder().Clear();
        await application.RunAsync(["status", "--root", root.Path, "--task", "T1"], CancellationToken.None);

        var owed = JsonNode.Parse(output.ToString())!["owed"]!.AsObject();
        Assert.Equal(1, owed["workItemsRunByNoWorkingRole"]!.GetValue<int>());
        // Zero, and that is the point of the second count rather than a widening of the first: this
        // item has not been worked, so it is not awaiting verification. Before the field existed
        // both counts read zero here and the owed block said nothing about an item the kernel would
        // not complete.
        Assert.Equal(0, owed["workItemsAwaitingVerification"]!.GetValue<int>());

        // The half that makes the number mean something: the gate refuses the same item the debt
        // now names. A projection that disagreed with the gate is what this field was added to fix.
        var completeExit = await application.RunAsync(
            ["work", "complete", .. common, "--id", "W1"], CancellationToken.None);
        Assert.NotEqual(0, completeExit);
        Assert.Contains("no completed run", error.ToString(), StringComparison.Ordinal);
    }

}
