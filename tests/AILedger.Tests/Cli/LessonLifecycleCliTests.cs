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
public sealed class LessonLifecycleCliTests
{
    [Fact]
    public async Task LessonMarkArchiveAndRecallFlowWorksThroughCli()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var application = Create(output, error);
        var source = new[]
        {
            "--root", root.Path, "--task", "2026-09-01_1200-source", "--actor", "operator"
        };
        var exits = new List<int>
        {
            await application.RunAsync(
                ["task", "open", .. source, "--title", "Source", "--goal", "Learn"], CancellationToken.None),
            await application.RunAsync(
                ["claim", "add", .. source, "--id", "C1", "--statement", "Retries stop after three attempts"],
                CancellationToken.None),
            await application.RunAsync(
                ["evidence", "add", .. source, "--id", "E1", "--source-type", "test-run",
                 "--citation", "RetryTests.Bounded", "--summary", "The retry policy stopped after three attempts",
                 "--supports", "C1"], CancellationToken.None),
            await application.RunAsync(
                ["claim", "resolve", .. source, "--id", "C1", "--status", "validated", "--evidence", "E1"],
                CancellationToken.None),
            await application.RunAsync(
                ["claim", "add", .. source, "--id", "C2", "--statement", "The research topic remains open"],
                CancellationToken.None),
            await application.RunAsync(
                ["alternative", "record", .. source, "--id", "ALT1", "--statement", "Skip bounded retries",
                 "--rejected-because", "The validated claim requires a bounded policy"], CancellationToken.None),
            await application.RunAsync(
                ["actor", "attach", .. source, "--target", "researcher", "--role", "researcher"],
                CancellationToken.None),
            await application.RunAsync(
                ["actor", "attach", .. source, "--target", "worker", "--role", "worker"],
                CancellationToken.None),
            await application.RunAsync(
                ["actor", "attach", .. source, "--target", "verifier", "--role", "verifier"],
                CancellationToken.None),
            await application.RunAsync(
                ["actor", "attach", .. source, "--target", "reviewer", "--role", "code-reviewer"],
                CancellationToken.None),
            await BriefAsync(source),
            await RecordArtifactAsync(
                application, source, "operator", "A-request", "user-request", null, null,
                "The governed request")
        };

        exits.Add(await application.RunAsync(
            ["stage", "transition", .. source, "--stage", "research"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["run", "start", .. source, "--subject", "researcher", "--run", "R-research",
             "--provider", "codex", "--session", "research-session"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["run", "complete", .. source, "--run", "R-research", "--status", "completed",
             "--session", "research-session"], CancellationToken.None));

        exits.Add(await application.RunAsync(
            ["stage", "transition", .. source, "--stage", "design"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["run", "start", .. source, "--run", "R-artifacts", "--provider", "codex",
             "--session", "artifact-session"], CancellationToken.None));
        exits.Add(await RecordArtifactAsync(
            application, source, "operator", "A-contract", "prompt-contract", null, "R-artifacts",
            ArtifactCommands.Body));
        exits.Add(await RecordArtifactAsync(
            application, source, "operator", "A-plan", "orchestration-plan", null, "R-artifacts",
            ArtifactCommands.PlanBody));
        exits.Add(await application.RunAsync(
            ["run", "complete", .. source, "--run", "R-artifacts", "--status", "completed",
             "--session", "artifact-session"], CancellationToken.None));
        foreach (var stage in new[] { "scope", "ready" })
        {
            exits.Add(await application.RunAsync(
                ["stage", "transition", .. source, "--stage", stage], CancellationToken.None));
        }
        exits.Add(await application.RunAsync(
            ["work", "add", .. source, "--id", "W1", "--title", "Source work", "--owner", "worker"],
            CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["stage", "transition", .. source, "--stage", "execution"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["run", "start", .. source, "--subject", "worker", "--run", "R-work", "--work", "W1",
             "--provider", "codex", "--session", "work-session"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["run", "complete", .. source, "--run", "R-work", "--status", "completed",
             "--session", "work-session"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["stage", "transition", .. source, "--stage", "verification"], CancellationToken.None));

        exits.Add(await application.RunAsync(
            ["run", "start", .. source, "--subject", "verifier", "--run", "R-verify", "--work", "W1",
             "--provider", "claude", "--session", "verify-session"], CancellationToken.None));
        exits.Add(await RecordArtifactAsync(
            application, source, "verifier", "A-verifier", "verifier-output", "W1", "R-verify",
            ArtifactCommands.VerifierBody));
        exits.Add(await application.RunAsync(
            ["run", "complete", .. source, "--run", "R-verify", "--status", "completed",
             "--session", "verify-session"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["stage", "transition", .. source, "--stage", "review"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["run", "start", .. source, "--subject", "reviewer", "--run", "R-review", "--work", "W1",
             "--provider", "codex", "--session", "review-session"], CancellationToken.None));
        exits.Add(await RecordArtifactAsync(
            application, source, "reviewer", "A-review", "code-review-output", "W1", "R-review",
            "Review findings"));
        exits.Add(await application.RunAsync(
            ["run", "complete", .. source, "--run", "R-review", "--status", "completed",
             "--session", "review-session"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["work", "complete", .. source, "--id", "W1"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["stage", "transition", .. source, "--stage", "learn"], CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["lesson", "mark", .. source, "--kind", "validated-claim", "--source", "C1",
             "--class", "untested", "--repo", "AILedger", "--tag", "retry", "--tag", "bounded",
             "--verify", "dotnet test --filter RetryTests.Bounded",
             "--do-not", "Do not assume retries are bounded without rerunning the test",
             "--lesson-actor", "verifier", "--verify-expects", "present",
             "--lesson-kind", "workflow", "--audience", "verifier"],
            CancellationToken.None));
        exits.Add(await application.RunAsync(
            ["stage", "transition", .. source, "--stage", "archive"], CancellationToken.None));

        exits.Add(await application.RunAsync(
            ["task", "open", "--root", root.Path, "--task", "2026-09-02_1200-target", "--actor", "operator",
             "--title", "Target", "--goal", "Recall"], CancellationToken.None));

        Assert.All(exits, exit => Assert.Equal(0, exit));
        Assert.Equal(string.Empty, error.ToString());
        var service = Service(root.Path);
        var sourceHistory = new List<LedgerEvent>();
        await foreach (var @event in service.GetHistoryAsync(
                           new TaskId("2026-09-01_1200-source"), CancellationToken.None))
        {
            sourceHistory.Add(@event);
        }

        var minted = Assert.IsType<LessonMinted>(
            Assert.Single(sourceHistory, item => item.Data is LessonMinted).Data).Lesson;
        Assert.Equal(LessonSourceKind.ValidatedClaim, minted.SourceKind);
        Assert.Equal("C1", minted.SourceRecordId);
        Assert.Equal(LessonClass.Untested, minted.Class);
        Assert.Equal("AILedger", minted.Repo);
        Assert.Equal(["retry", "bounded"], minted.Tags);
        // The three routing options reach the minted lesson through the CLI, not only through the
        // command record: a flag the dispatcher drops would leave every other assertion here true.
        Assert.Equal(LessonKind.Workflow, minted.Kind);
        Assert.Equal([RoleKind.Verifier], minted.Audience);
        Assert.Equal(VerifyExpectation.Present, minted.VerifyExpects);
        var targetHistory = new List<LedgerEvent>();
        await foreach (var @event in service.GetHistoryAsync(
                           new TaskId("2026-09-02_1200-target"), CancellationToken.None))
        {
            targetHistory.Add(@event);
        }

        var recalledLesson = Assert.IsType<LessonRecalled>(
            Assert.Single(targetHistory, item => item.Data is LessonRecalled).Data).Lesson;
        Assert.Equal(minted.Id, recalledLesson.Id);
        Assert.Equal(minted.Statement, recalledLesson.Statement);
        Assert.Equal(minted.Citations, recalledLesson.Citations);
    }

}
