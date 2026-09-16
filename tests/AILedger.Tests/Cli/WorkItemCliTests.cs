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
public sealed class WorkItemCliTests
{
    // F2 at the surface the operator actually types. Releasing an area is only useful if the next
    // work item can take it, so the test asserts the reuse rather than the status alone.
    [Fact]
    public async Task WorkAbandonReleasesTheAreaForABetterSplit()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Wrong split", "--owner", "operator",
             "--scope", area], CancellationToken.None);

        var abandonExit = await application.RunAsync(
            ["work", "abandon", .. common, "--id", "W1", "--reason", "The split was wrong"],
            CancellationToken.None);
        var reuseExit = await application.RunAsync(
            ["work", "add", .. common, "--id", "W2", "--title", "Better split", "--owner", "operator",
             "--scope", area], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, abandonExit);
        Assert.Equal(0, reuseExit);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(WorkItemStatus.Abandoned, state!.WorkItems[new WorkItemId("W1")].Status);
        Assert.Equal("The split was wrong", state.WorkItems[new WorkItemId("W1")].AbandonReason);
        Assert.Equal(WorkItemStatus.Proposed, state.WorkItems[new WorkItemId("W2")].Status);
    }

    // The statuses that release a work item's area are written out twice with no shared predicate:
    // once in the occupancy check behind `work add`, once in the `who` projection's occupied query.
    // If the two lists ever drift, `who` calls an area free that `work add` refuses, or — the way
    // round that costs real work — `who` calls an area held after `work add` has already handed the
    // same directory to a second agent. Asserting either fact alone still passes while they
    // disagree, so both are asserted here, in one test, against one abandonment.
    [Fact]
    public async Task AbandoningReleasesAnAreaInBothTheWhoProjectionAndTheOccupancyCheck()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var error = new StringWriter();
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await Create(TextWriter.Null, error).RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(Create(TextWriter.Null, error), root.Path);
        await Create(TextWriter.Null, error).RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Wrong split", "--owner", "operator",
             "--scope", area], CancellationToken.None);
        // The stored scope is canonicalised — on macOS /var resolves to /private/var — so what is
        // matched against the projection is what the ledger holds, not what was typed.
        var added = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var canonicalArea = Assert.Single(added!.WorkItems[new WorkItemId("W1")].ResourceScope);

        var whileHeld = new StringWriter();
        await Create(whileHeld, error).RunAsync(["who", .. common], CancellationToken.None);
        await Create(TextWriter.Null, error).RunAsync(
            ["work", "abandon", .. common, "--id", "W1", "--reason", "The split was wrong"],
            CancellationToken.None);
        var afterRelease = new StringWriter();
        await Create(afterRelease, error).RunAsync(["who", .. common], CancellationToken.None);

        // Only now is the area claimed again, so the projection above was read while it was free.
        var reuseExit = await Create(TextWriter.Null, error).RunAsync(
            ["work", "add", .. common, "--id", "W2", "--title", "Better split", "--owner", "operator",
             "--scope", area], CancellationToken.None);

        // The control. Without it, an absence proves nothing: a `who` that never named areas at all
        // would satisfy the second assertion on its own.
        Assert.Contains(canonicalArea, whileHeld.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(canonicalArea, afterRelease.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, reuseExit);
        Assert.Equal(string.Empty, error.ToString());
    }

    // The whole defect, end to end, at the surface the operator uses. An abandoned item releases
    // its area; a second item takes it; then rejecting the claim the first depended on moves it
    // from Abandoned to Stale — and blocking a stale item used to be accepted, leaving two live
    // work items holding one directory. The exit code is only half the evidence: the area has to
    // still read as held by exactly one item, in `who` and in what a third `work add` is told.
    [Fact]
    public async Task AnAreaReleasedByAbandonmentIsNotRetakenWhenTheItemGoesStale()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["claim", "add", .. common, "--id", "C1", "--statement", "The API is stable"],
            CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Wrong split", "--owner", "operator",
             "--depends-on", "C1", "--scope", area], CancellationToken.None);
        await application.RunAsync(
            ["work", "abandon", .. common, "--id", "W1", "--reason", "The split was wrong"],
            CancellationToken.None);
        var reuseExit = await application.RunAsync(
            ["work", "add", .. common, "--id", "W2", "--title", "Better split", "--owner", "operator",
             "--scope", area], CancellationToken.None);

        // The claim W1 was built on is refuted, which invalidates W1 and moves it to Stale.
        await application.RunAsync(
            ["evidence", "add", .. common, "--id", "E1", "--source-type", "probe", "--citation", "cite",
             "--summary", "refutes C1", "--refutes", "C1"], CancellationToken.None);
        await application.RunAsync(
            ["claim", "resolve", .. common, "--id", "C1", "--status", "rejected", "--evidence", "E1"],
            CancellationToken.None);
        var staleState = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var canonicalArea = Assert.Single(staleState!.WorkItems[new WorkItemId("W2")].ResourceScope);

        var blockExit = await application.RunAsync(
            ["work", "block", .. common, "--id", "W1", "--reason", "Reopening it"], CancellationToken.None);
        // The other way back in, and the one that would also rewrite the record: abandoning again
        // would replace the reason the operator actually acted on with a later, different one.
        var abandonAgainExit = await application.RunAsync(
            ["work", "abandon", .. common, "--id", "W1", "--reason", "A different reason"],
            CancellationToken.None);

        var who = new StringWriter();
        await Create(who, TextWriter.Null).RunAsync(["who", .. common], CancellationToken.None);
        var thirdAdd = new StringWriter();
        var thirdExit = await Create(TextWriter.Null, thirdAdd).RunAsync(
            ["work", "add", .. common, "--id", "W3", "--title", "A third claimant", "--owner", "operator",
             "--scope", area], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, reuseExit);
        Assert.Equal(1, blockExit);
        Assert.Equal(1, abandonAgainExit);
        Assert.Equal("The split was wrong", state!.WorkItems[new WorkItemId("W1")].AbandonReason);
        Assert.Equal(WorkItemStatus.Stale, state.WorkItems[new WorkItemId("W1")].Status);
        Assert.Equal(WorkItemStatus.Proposed, state.WorkItems[new WorkItemId("W2")].Status);
        // One holder, not two: the area is named once in `who`, and the item turned away is told
        // which single work item holds it.
        Assert.Equal(1, Occurrences(who.ToString(), canonicalArea));
        Assert.Equal(1, thirdExit);
        Assert.Contains("already held by work item 'W2'", thirdAdd.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("'W1'", thirdAdd.ToString(), StringComparison.Ordinal);
    }

    // F3b at the operator's surface. The refusal has to arrive as an exit code and a sentence that
    // says what to do next, because the operator's next move is to write the alternative down.
    [Fact]
    public async Task WorkAddRefusesASecondAreaUntilAnAlternativeExplainsTheNonSplit()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var first = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "second")).FullName;
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);

        var refusedExit = await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Two areas", "--owner", "operator",
             "--scope", first, "--scope", second], CancellationToken.None);

        await application.RunAsync(
            ["alternative", "record", .. common, "--id", "ALT1",
             "--statement", "Split the two areas into separate work items",
             "--rejected-because", "The two areas only ever change together"], CancellationToken.None);
        var acceptedExit = await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Two areas", "--owner", "operator",
             "--scope", first, "--scope", second, "--not-split-because", "ALT1"], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, refusedExit);
        Assert.Contains("more than one area", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, acceptedExit);
        Assert.Equal(new AlternativeId("ALT1"), state!.WorkItems[new WorkItemId("W1")].NotSplitJustification);
    }

    // F3a at the same surface, and the gate the operator hits first: nothing has run at all.
    [Fact]
    public async Task WorkCompleteIsRefusedUntilSomeoneHasActuallyWorkedOnTheItem()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Untouched work", "--owner", "operator",
             "--scope", area], CancellationToken.None);

        var exit = await application.RunAsync(
            ["work", "complete", .. common, "--id", "W1"], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, exit);
        Assert.Contains("no completed run", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(WorkItemStatus.Proposed, state!.WorkItems[new WorkItemId("W1")].Status);
    }

    // F1 at the same surface: the gate is refused with an exit code and a message the operator can
    // act on, and the waiver is the only way past it without a verifier.
    [Fact]
    public async Task WorkCompleteIsRefusedWithoutAVerifierRunUntilTheOperatorWaivesIt()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Unverifiable work", "--owner", "operator",
             "--scope", area], CancellationToken.None);
        // The work itself was done, so the refusal below is about the verifier pass and nothing else.
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "worker", "--role", "worker"], CancellationToken.None);
        await CliStageFixture.ToExecutionAsync(application, root.Path);
        await application.RunAsync(
            ["run", "start", .. common, "--subject", "worker", "--run", "RW", "--work", "W1",
             "--provider", "codex", "--session", "worker-session"], CancellationToken.None);
        await application.RunAsync(
            ["run", "complete", .. common, "--run", "RW", "--status", "completed",
             "--session", "worker-session"], CancellationToken.None);

        var refusedExit = await application.RunAsync(
            ["work", "complete", .. common, "--id", "W1"], CancellationToken.None);
        var waivedExit = await application.RunAsync(
            ["work", "complete", .. common, "--id", "W1",
             "--without-verification", "No verifier is attached to this task"], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(1, refusedExit);
        Assert.Contains("verifier run", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, waivedExit);
        Assert.Equal(WorkItemStatus.Completed, state!.WorkItems[new WorkItemId("W1")].Status);
    }

    // The path the pipeline is meant to take, driven end to end: an operator completes the worker,
    // verifier, and reviewer passes in order, and only then does work completion go through.
    [Fact]
    public async Task AReviewerRunRecordedThroughTheCliUnlocksCompletion()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "verifier", "--role", "verifier"],
            CancellationToken.None);
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "reviewer", "--role", "code-reviewer"],
            CancellationToken.None);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "Verified work", "--owner", "operator",
             "--scope", area], CancellationToken.None);
        // The work itself was done, so the refusal below is about the verifier pass and nothing else.
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "worker", "--role", "worker"], CancellationToken.None);
        await RecordExecutionArtifactsAsync(application, common);
        await CliStageFixture.ToExecutionAsync(application, root.Path);
        await application.RunAsync(
            ["run", "start", .. common, "--subject", "worker", "--run", "RW", "--work", "W1",
             "--provider", "codex", "--session", "worker-session"], CancellationToken.None);
        await application.RunAsync(
            ["run", "complete", .. common, "--run", "RW", "--status", "completed",
             "--session", "worker-session"], CancellationToken.None);

        await CliStageFixture.ToVerificationAsync(application, root.Path);
        await application.RunAsync(
            ["run", "start", .. common, "--subject", "verifier", "--run", "RV", "--work", "W1",
             "--provider", "claude", "--session", "verifier-session"], CancellationToken.None);
        // The pass files its findings before it closes: a verifier run cannot be recorded as
        // completed without the VerifierOutput artifact that run produced.
        await RecordArtifactAsync(
            application, ["--root", root.Path, "--task", "T1"], "verifier", "A-RV", "verifier-output",
            "W1", "RV", ArtifactCommands.VerifierBody);
        await application.RunAsync(
            ["run", "complete", .. common, "--run", "RV", "--status", "completed",
             "--session", "verifier-session"], CancellationToken.None);
        await CliStageFixture.ToReviewAsync(application, root.Path);
        await application.RunAsync(
            ["run", "start", .. common, "--subject", "reviewer", "--run", "RCR", "--work", "W1",
             "--provider", "codex", "--session", "reviewer-session"], CancellationToken.None);
        await RecordArtifactAsync(
            application, ["--root", root.Path, "--task", "T1"], "reviewer", "A-RCR", "code-review-output",
            "W1", "RCR", ArtifactCommands.Body);
        await application.RunAsync(
            ["run", "complete", .. common, "--run", "RCR", "--status", "completed",
             "--session", "reviewer-session"], CancellationToken.None);
        var completeExit = await application.RunAsync(
            ["work", "complete", .. common, "--id", "W1"], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(0, completeExit);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(RoleKind.Verifier, state!.Runs[new RunId("RV")].SubjectRole);
        Assert.Equal(RoleKind.CodeReviewer, state.Runs[new RunId("RCR")].SubjectRole);
        Assert.Equal(WorkItemStatus.Completed, state.WorkItems[new WorkItemId("W1")].Status);
    }

    // Accepted decision D1: an area may be a single file, so two agents can hold two files in one
    // directory. The kernel accepted that from the beginning; the check that held every scope to a
    // directory was here, in the operator surface, which is why these two tests are at this level.
    [Fact]
    public async Task WorkCreationAcceptsAFileAsAnAreaAndStoresTheFileItself()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var file = Path.Combine(area, "ClaimRules.cs");
        await File.WriteAllTextAsync(file, "// the area itself");
        var error = new StringWriter();
        var application = Create(TextWriter.Null, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);

        var exit = await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "One file", "--owner", "operator",
             "--scope", file], CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var scope = Assert.Single(state!.WorkItems[new WorkItemId("W1")].ResourceScope);
        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        // The stored scope is the file, not the directory around it. A scope widened to the parent
        // here would hand one agent every other file beside it and read in `who` as the whole area.
        Assert.True(Path.IsPathFullyQualified(scope));
        Assert.True(File.Exists(scope));
        Assert.Equal("ClaimRules.cs", Path.GetFileName(scope));
    }

    // A process cannot start inside a file, so a file-scoped item resolves its provider's working
    // directory to the file's parent. That widening is the launch's alone: the recorded scope stays
    // the file, and occupancy keeps reading it as one.
    [Fact]
    public async Task AFileScopedWorkItemLaunchesItsProviderInTheFilesDirectory()
    {
        using var root = new TemporaryDirectory();
        using var scopeRoot = new TemporaryDirectory();
        var area = Directory.CreateDirectory(Path.Combine(scopeRoot.Path, "area")).FullName;
        var file = Path.Combine(area, "ClaimRules.cs");
        await File.WriteAllTextAsync(file, "// the area itself");
        var capture = new CapturingAdapter();
        var application = new CliApplication(
            TextWriter.Null, TextWriter.Null, Service, _ => capture, new ContextAssembler());
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await ContextBrief.BuildAsync(root.Path, "T1");
        await CliStageFixture.ToReadyAsync(application, root.Path);
        await application.RunAsync(
            ["work", "add", .. common, "--id", "W1", "--title", "One file", "--owner", "operator",
             "--scope", file], CancellationToken.None);
        // The launch names the work item, because the item's scope is what this test is about. That
        // makes the subject a worker: a coordinating role cannot hold a run against an item.
        await application.RunAsync(
            ["actor", "attach", .. common, "--target", "worker", "--role", "worker"],
            CancellationToken.None);
        await CliStageFixture.ToExecutionAsync(application, root.Path);

        var exit = await application.RunAsync(
            ["provider", "launch", .. common, "--subject", "worker",
             "--run", "R1", "--work", "W1", "--provider", "codex",
             "--executable", "/usr/bin/true", "--cognitive-root", FindCognitiveRoot()],
            CancellationToken.None);

        var state = await Service(root.Path).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var scope = Assert.Single(state!.WorkItems[new WorkItemId("W1")].ResourceScope);
        var request = Assert.Single(capture.Requests);
        Assert.Equal(0, exit);
        // Both sides are canonical already — the stored scope was canonicalised when it was
        // recorded — so the parent is compared as a path rather than through a sentinel file.
        Assert.Equal(Path.GetDirectoryName(scope), request.WorkingDirectory);
        Assert.EndsWith("ClaimRules.cs", scope, StringComparison.Ordinal);
    }

}
