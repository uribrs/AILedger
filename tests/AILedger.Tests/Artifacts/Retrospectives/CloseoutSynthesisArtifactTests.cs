using AILedger.Core.Application;
using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Artifacts;

public sealed class CloseoutSynthesisArtifactTests
{
    [Fact]
    public void AnOperatorFilesOneWithoutAProducerRun()
    {
        var task = AtReview();

        var outcome = task.Apply(Synthesis(task, task.OperatorId, "A-closeout"));

        var artifact = outcome.State.Artifacts[new ArtifactId("A-closeout")];
        Assert.Equal(GovernedArtifactKind.CloseoutSynthesis, artifact.Kind);
        Assert.Null(artifact.ProducerRunId);
        Assert.Null(artifact.WorkItemId);
    }

    [Fact]
    public void NamingAProducerRunIsRefused()
    {
        var task = AtReview();
        var run = new RunId("R-closeout");
        task.Apply(new StartRunCommand(
            task.OperatorId, null, task.NextCorrelation(), run, null, "codex",
            null, null, null, null, task.OperatorId));

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Synthesis(task, task.OperatorId, "A-closeout", producerRun: run)));

        Assert.StartsWith(
            "A closeout-synthesis artifact is task-wide judgement over the whole record and cannot " +
            "name a producer run",
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RoleKind.Worker)]
    [InlineData(RoleKind.Verifier)]
    [InlineData(RoleKind.CodeReviewer)]
    [InlineData(RoleKind.Researcher)]
    public void OnlyAnOperatorOrALeadMayRecordOne(RoleKind role)
    {
        var task = AtReview();
        var actor = new ActorId("actor-" + role);
        task.Assign(actor, role, Capability.BuildContext, Capability.RecordArtifact);

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Synthesis(task, actor, "A-closeout")));

        Assert.Equal(
            "Only an operator, planning lead, or implementation lead can record a governing workflow artifact.",
            error.Message);
    }

    [Theory]
    [InlineData(TaskStage.Discovery)]
    [InlineData(TaskStage.Execution)]
    [InlineData(TaskStage.Verification)]
    public void FilingBeforeTheTechnicalCycleFinishesIsRefused(TaskStage stage)
    {
        var task = new TestTask();
        if (stage != TaskStage.Discovery)
        {
            task.ReachStage(stage);
        }

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Synthesis(task, task.OperatorId, "A-closeout")));

        Assert.Contains("once the technical cycle has finished", error.Message, StringComparison.Ordinal);
        Assert.Contains($"this task is at stage '{stage}'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TaskStage.Review)]
    [InlineData(TaskStage.Learn)]
    public void ReviewAndLearnAdmitOne(TaskStage stage)
    {
        var task = new TestTask();
        task.ReachStage(stage);

        var outcome = task.Apply(Synthesis(task, task.OperatorId, "A-closeout"));

        Assert.Contains(new ArtifactId("A-closeout"), outcome.State.Artifacts.Keys);
    }

    [Fact]
    public void AnArchivedTaskMayStillFileOne()
    {
        var task = WorkflowRetrospectiveArtifactTests.Archived();

        var outcome = task.Apply(Synthesis(task, task.OperatorId, "A-closeout"));

        Assert.Equal(TaskStage.Archive, outcome.State.Stage);
        Assert.Contains(new ArtifactId("A-closeout"), outcome.State.Artifacts.Keys);
    }

    [Fact]
    public void AFiledSynthesisIsWithheldFromEveryContextAndDoesNotBreakContextBuild()
    {
        var task = WorkflowRetrospectiveArtifactTests.Archived();
        task.Apply(Synthesis(task, task.OperatorId, "A-closeout"));

        foreach (var role in Enum.GetValues<RoleKind>())
        {
            var actor = new ActorId("reader-" + role);
            task.Assign(actor, role, Capability.BuildContext);

            var manifest = new ContextAssembler().Build(
                task.State, actor, null, [], DateTimeOffset.UnixEpoch);

            Assert.DoesNotContain(manifest.Artifacts, artifact => artifact.Id == "A-closeout");
        }
    }

    [Fact]
    public void BothTablesMayBeEmpty()
    {
        var task = AtReview();

        var outcome = task.Apply(Synthesis(task, task.OperatorId, "A-closeout", body: Body()));

        Assert.Contains(new ArtifactId("A-closeout"), outcome.State.Artifacts.Keys);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("reintroduced")]
    [InlineData("repair-regression")]
    [InlineData("accepted-risk")]
    [InlineData("not-attempted")]
    [InlineData("not-rechecked")]
    [InlineData("demonstrated")]
    [InlineData("unverified")]
    public void RepairOutcomesRemainDistinctAdmittedValues(string repair)
    {
        var task = AtReview();

        var outcome = task.Apply(Synthesis(
            task, task.OperatorId, "A-closeout", body: Body(Finding(repair: repair))));

        Assert.Contains(new ArtifactId("A-closeout"), outcome.State.Artifacts.Keys);
    }

    [Theory]
    [InlineData("missed")]
    [InlineData("detected-in-scope")]
    [InlineData("outside-scope")]
    [InlineData("introduced-later")]
    [InlineData("insufficient-evidence")]
    public void OpportunityOutcomesRemainDistinctAdmittedValues(string opportunity)
    {
        var task = AtReview();

        var outcome = task.Apply(Synthesis(
            task, task.OperatorId, "A-closeout", body: Body(Finding(opportunity: opportunity))));

        Assert.Contains(new ArtifactId("A-closeout"), outcome.State.Artifacts.Keys);
    }

    [Theory]
    [InlineData("kind", "bug")]
    [InlineData("severity", "critical")]
    [InlineData("opportunity", "probably-missed")]
    [InlineData("repair", "fixed")]
    [InlineData("disposition", "done")]
    public void ValuesOutsideTheFrozenVocabularyAreRefused(string column, string value)
    {
        var task = AtReview();
        var finding = Finding(
            kind: column == "kind" ? value : "product-defect",
            severity: column == "severity" ? value : "high",
            opportunity: column == "opportunity" ? value : "missed",
            repair: column == "repair" ? value : "demonstrated",
            disposition: column == "disposition" ? value : "verified-fixed");

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Synthesis(task, task.OperatorId, "A-closeout", body: Body(finding))));

        Assert.Contains("outside the frozen vocabulary", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(8)]
    public void EmptyFindingAnswersAreRefused(int column)
    {
        var task = AtReview();
        var cells = FindingCells();
        cells[column] = " ";

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Synthesis(task, task.OperatorId, "A-closeout", body: Body(Row(cells)))));

        Assert.Contains("write 'none'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateFindingIdentifiersAreRefused()
    {
        var task = AtReview();

        var error = Assert.Throws<GovernanceException>(() => task.Apply(Synthesis(
            task, task.OperatorId, "A-closeout", body: Body(Finding("F1"), Finding("F1")))));

        Assert.Contains("finding IDs cannot contain duplicates", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("../other-task/events.jsonl")]
    [InlineData("runs/../../escape.json")]
    [InlineData("C:\\other-task\\events.jsonl")]
    public void RetentionPathsMustStayInsideTheTask(string path)
    {
        var task = AtReview();
        var body = Body() + RetentionRow(path, "delete", "disposable", "F1");

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Synthesis(task, task.OperatorId, "A-closeout", body: body)));

        Assert.Contains("must be relative to the task directory", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("delete", "", "F1")]
    [InlineData("delete", "disposable", "")]
    public void RetentionRowsRequireReasonAndEvidence(string decision, string reason, string evidence)
    {
        var task = AtReview();
        var body = Body() + RetentionRow("runs/R1.json", decision, reason, evidence);

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Synthesis(task, task.OperatorId, "A-closeout", body: body)));

        Assert.Contains("must carry a reason and evidence", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateRetentionPathsAreRefused()
    {
        var task = AtReview();
        var body = Body() +
            RetentionRow("runs/R1.json", "delete", "raw stream", "F1") +
            RetentionRow("runs/R1.json", "retain", "candidate", "F2");

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Synthesis(task, task.OperatorId, "A-closeout", body: body)));

        Assert.Contains("retention paths cannot contain duplicates", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pipe")]
    [InlineData("missing-leading-pipe")]
    [InlineData("missing-trailing-pipe")]
    [InlineData("blank-line")]
    public void AMalformedFindingLineRefusesTheWholeTableBeforeLaterVocabularyCanEscapeValidation(
        string shape)
    {
        var task = AtReview();
        var first = Finding();
        if (shape == "pipe")
        {
            var malformed = FindingCells();
            malformed[3] = "verifier R12 | artifact AVR4";
            first = Row(malformed);
        }

        var malformedLine = Malform(first, shape);
        var hidden = Finding(id: "F2", kind: "NOT-A-KIND", disposition: "NOT-A-DISPOSITION");

        var error = Assert.Throws<GovernanceException>(() => task.Apply(Synthesis(
            task, task.OperatorId, "A-closeout", body: Body(malformedLine, hidden))));

        Assert.Contains("findings table", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pipe")]
    [InlineData("missing-leading-pipe")]
    [InlineData("missing-trailing-pipe")]
    [InlineData("blank-line")]
    public void AMalformedRetentionLineRefusesTheWholeTableBeforeLaterRowsCanEscapeValidation(
        string shape)
    {
        var task = AtReview();
        var first = RetentionRow("runs/R1.json", "retain", "candidate output", "F1");
        if (shape == "pipe")
        {
            first = RetentionRow("runs/R1.json", "retain", "candidate | output", "F1");
        }

        var body = Body(Finding()) +
            Malform(first, shape) +
            RetentionRow("../escape", "NOT-A-DECISION", "hidden", "F2");

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Synthesis(task, task.OperatorId, "A-closeout", body: body)));

        Assert.Contains("retention table", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARowInTheSeparatorPositionCannotHideInvalidFindingVocabulary()
    {
        var task = AtReview();
        var body = Body(Finding(id: "F2"))
            .Replace(
                FindingsSeparatorRow(),
                Finding(id: "F1", kind: "NOT-A-KIND"),
                StringComparison.Ordinal);

        var error = Assert.Throws<GovernanceException>(() => task.Apply(Synthesis(
            task, task.OperatorId, "A-closeout", body: body)));

        Assert.Contains("findings table", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARowInTheSeparatorPositionCannotHideInvalidRetentionVocabulary()
    {
        var task = AtReview();
        var body = Body(Finding())
            .Replace(
                "\n" + RetentionSeparatorRow(),
                "\n" + RetentionRow("runs/R1.json", "NOT-A-DECISION", "hidden", "F1"),
                StringComparison.Ordinal);

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Synthesis(task, task.OperatorId, "A-closeout", body: body)));

        Assert.Contains("retention table", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AHeadingInsideATableRegionCannotHideLaterInvalidFindingVocabulary()
    {
        var task = AtReview();
        var body = Body(
            Finding(),
            "### Hidden finding rows\n",
            Finding(id: "F2", kind: "NOT-A-KIND", disposition: "NOT-A-DISPOSITION"));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(Synthesis(
            task, task.OperatorId, "A-closeout", body: body)));

        Assert.Contains("findings table", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AHeadingInsideATableRegionCannotHideLaterInvalidRetentionVocabulary()
    {
        var task = AtReview();
        var body = Body(Finding()) +
            "### Hidden retention rows\n" +
            RetentionRow("runs/R2.json", "NOT-A-DECISION", "hidden", "F2");

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Synthesis(task, task.OperatorId, "A-closeout", body: body)));

        Assert.Contains("retention table", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateFindingsHeaderCannotHideLaterInvalidFindingVocabulary()
    {
        var task = AtReview();
        var body = Body(
            Finding(),
            "### Repeated findings appendix\n",
            FindingsHeaderRow(),
            FindingsSeparatorRow(),
            Finding(id: "F2", disposition: "NOT-A-DISPOSITION"));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(Synthesis(
            task, task.OperatorId, "A-closeout", body: body)));

        Assert.Contains("findings table header appears more than once", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateRetentionHeaderCannotHideLaterInvalidRetentionVocabulary()
    {
        var task = AtReview();
        var body = Body(Finding()) +
            RetentionRow("runs/R1.json", "retain", "candidate output", "F1") +
            "### Repeated retention appendix\n" +
            RetentionHeaderRow() +
            RetentionSeparatorRow() +
            RetentionRow("runs/R2.json", "NOT-A-DECISION", "hidden", "F2");

        var error = Assert.Throws<GovernanceException>(() =>
            task.Apply(Synthesis(task, task.OperatorId, "A-closeout", body: body)));

        Assert.Contains("retention table header appears more than once", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosingProseAfterTheRetentionTableIsAdmitted()
    {
        var task = AtReview();
        var body = Body(Finding()) + "\nAll cited material has been accounted for.\n";

        var outcome = task.Apply(Synthesis(task, task.OperatorId, "A-closeout", body: body));

        Assert.Contains(new ArtifactId("A-closeout"), outcome.State.Artifacts.Keys);
    }

    [Fact]
    public void ATrailingHeadingAfterTheRetentionTableIsAdmitted()
    {
        var task = AtReview();
        var body = Body(Finding()) + "\n## Closing notes\n";

        var outcome = task.Apply(Synthesis(task, task.OperatorId, "A-closeout", body: body));

        Assert.Contains(new ArtifactId("A-closeout"), outcome.State.Artifacts.Keys);
    }

    [Fact]
    public void ProseBetweenTheTwoTablesIsAdmitted()
    {
        var task = AtReview();
        var body = Body(Finding()).Replace(
            "## Retention",
            "The findings above determine the retention decisions below.\n\n## Retention",
            StringComparison.Ordinal);

        var outcome = task.Apply(Synthesis(task, task.OperatorId, "A-closeout", body: body));

        Assert.Contains(new ArtifactId("A-closeout"), outcome.State.Artifacts.Keys);
    }

    [Fact]
    public void TwoHeadingsBetweenTheTablesAreAdmitted()
    {
        var task = AtReview();
        var body = Body(Finding()).Replace(
            "## Retention",
            "## Interpretation\n\n## Retention",
            StringComparison.Ordinal);

        var outcome = task.Apply(Synthesis(task, task.OperatorId, "A-closeout", body: body));

        Assert.Contains(new ArtifactId("A-closeout"), outcome.State.Artifacts.Keys);
    }

    [Fact]
    public void ASameWidthOrphanRowAfterABlankBoundaryIsRefused()
    {
        var task = AtReview();
        var body = Body(Finding(), "\n", Finding(id: "F2", kind: "NOT-A-KIND"));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(Synthesis(
            task, task.OperatorId, "A-closeout", body: body)));

        Assert.Contains("orphan pipe row", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMalformedRowInsideAContiguousRegionIsRefused()
    {
        var task = AtReview();
        var body = Body(
            Finding(),
            "not a pipe row\n",
            Finding(id: "F2", kind: "NOT-A-KIND"));

        var error = Assert.Throws<GovernanceException>(() => task.Apply(Synthesis(
            task, task.OperatorId, "A-closeout", body: body)));

        Assert.Contains("not a well-formed pipe row", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RetentionRowsReadBackThroughTheAdmittingParser()
    {
        var rows = CloseoutSynthesisDocument.ReadRetention(
            Body() + RetentionRow("runs/R1.json", "delete", "raw stream", "F1, E4"));

        var row = Assert.Single(rows);
        Assert.Equal("runs/R1.json", row.Path);
        Assert.Equal("delete", row.Decision);
        Assert.Equal("raw stream", row.Reason);
        Assert.Equal("F1, E4", row.Evidence);
    }

    [Fact]
    public void ARecordedSynthesisReplays()
    {
        var task = AtReview();
        task.Apply(Synthesis(task, task.OperatorId, "A-closeout"));
        var reducer = new TaskReducer();
        GovernedTaskState? replayed = null;

        foreach (var ledgerEvent in task.Events)
        {
            replayed = reducer.Apply(replayed, ledgerEvent);
        }

        Assert.Equal(
            GovernedArtifactKind.CloseoutSynthesis,
            replayed!.Artifacts[new ArtifactId("A-closeout")].Kind);
    }

    [Fact]
    public void ReplayDoesNotApplyTheCommandTimeStageGate()
    {
        var task = AtReview();
        task.Apply(Synthesis(task, task.OperatorId, "A-closeout"));
        var recorded = task.Events.Last(item => item.Data is ArtifactRecorded);
        var reducer = new TaskReducer();
        GovernedTaskState? replayed = null;
        foreach (var ledgerEvent in task.Events.TakeWhile(item =>
                     item.Data is not StageTransitioned { Current: TaskStage.Review }))
        {
            replayed = reducer.Apply(replayed, ledgerEvent);
        }

        Assert.Equal(TaskStage.Verification, replayed!.Stage);
        var after = reducer.Apply(replayed, recorded);
        Assert.Contains(new ArtifactId("A-closeout"), after.Artifacts.Keys);
    }

    internal static string[] FindingCells() =>
    [
        "F1", "product-defect", "high", "verifier R12 artifact AVR4", "none", "missed",
        "demonstrated", "verified-fixed", "Check both path aliases"
    ];

    internal static string Row(IReadOnlyList<string> cells) =>
        "| " + string.Join(" | ", cells) + " |\n";

    internal static string Finding(
        string id = "F1",
        string kind = "product-defect",
        string severity = "high",
        string opportunity = "missed",
        string repair = "demonstrated",
        string disposition = "verified-fixed")
    {
        var cells = FindingCells();
        cells[0] = id;
        cells[1] = kind;
        cells[2] = severity;
        cells[5] = opportunity;
        cells[6] = repair;
        cells[7] = disposition;
        return Row(cells);
    }

    internal static string RetentionRow(string path, string decision, string reason, string evidence) =>
        $"| {path} | {decision} | {reason} | {evidence} |\n";

    private static string FindingsHeaderRow() =>
        "| finding | kind | severity | detection | occurrences | opportunity | repair | disposition | lesson |\n";

    private static string FindingsSeparatorRow() =>
        "| --- | --- | --- | --- | --- | --- | --- | --- | --- |\n";

    private static string RetentionHeaderRow() =>
        "| path | decision | reason | evidence |\n";

    private static string RetentionSeparatorRow() =>
        "| --- | --- | --- | --- |\n";

    private static string Malform(string row, string shape) => shape switch
    {
        "pipe" => row,
        "missing-leading-pipe" => row.TrimStart('|'),
        "missing-trailing-pipe" => row.TrimEnd('\n')[..^1] + "\n",
        "blank-line" => row + "\n",
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
    };

    internal static string Body(params string[] findings) =>
        "# Closeout synthesis\n\n## Findings\n\n" +
        FindingsHeaderRow() +
        FindingsSeparatorRow() +
        string.Concat(findings) +
        "\n## Retention\n\n" +
        RetentionHeaderRow() +
        RetentionSeparatorRow();

    internal static RecordArtifactCommand Synthesis(
        TestTask task,
        ActorId actor,
        string artifactId,
        string? body = null,
        RunId? producerRun = null,
        string? supersedes = null) =>
        ArtifactCommands.Record(
            task, actor, artifactId, GovernedArtifactKind.CloseoutSynthesis,
            body ?? Body(Finding()), "What assurance found", producerRun: producerRun,
            supersedes: supersedes);

    private static TestTask AtReview()
    {
        var task = new TestTask();
        task.ReachStage(TaskStage.Review);
        return task;
    }
}
