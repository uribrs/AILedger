using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Storage;

// The second and every later time one rule refuses one actor on one task, the refusal says which
// occurrence it is. Every test here drives the real FileGovernedTaskService, because the question is
// what the kernel's own refusal path tells a caller, not what a re-reading of the journal would say.
//
// The counter is read from refusals.jsonl on a command path, which is the first read of that file
// from inside a command and the whole risk of this increment. K4 is what bounds it: the number
// reaches the message and nothing else, and a journal that is absent, empty, unreadable or partly
// unparseable leaves the refusal exactly as it was. Those four conditions have a test each below,
// and none of them asserts an absence on its own — each sits beside a refusal that proves the
// counting path is alive.
public sealed class RefusalRepetitionTests
{
    private const string VerifierGate =
        "Work item 'W1' has no completed verifier run and cannot be completed. " +
        "An operator may complete it without one by recording why.";

    // One refusal is an answer. Only the repetition is the signal, so the first says nothing and the
    // rule's own sentence is the whole message.
    [Fact]
    public async Task TheFirstRefusalOfARuleSaysNothingAboutRepetition()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-first-task");
        var service = await StageAsync(root.Path, taskId, "W1");

        var refusal = await RefuseCompletionAsync(service, taskId, "W1");

        Assert.Equal(VerifierGate, refusal.Message);
    }

    // The rule's sentence first and byte-identical, the counter appended below it. Asserted as one
    // string rather than with Contains, so a revision that kept the counter and lost the sentence
    // fails here.
    [Fact]
    public async Task TheSecondRefusalOfOneRuleSaysItIsTheSecond()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-second-task");
        var service = await StageAsync(root.Path, taskId, "W1");

        var first = await RefuseCompletionAsync(service, taskId, "W1");
        var second = await RefuseCompletionAsync(service, taskId, "W1");

        Assert.Equal(VerifierGate, first.Message);
        Assert.Equal(
            VerifierGate + Environment.NewLine + "  refusal 2 of this rule for you on this task",
            second.Message);
    }

    // Uncapped, and the test goes past three because three is where a cap would be put. On
    // 2026-09-17_1440 the third occurrence of one rule was the beginning of a sixteen-refusal pattern
    // and not the end of it: a counter that stopped at three would have said the same thing at the
    // third as at the sixteenth, which is the point at which the line stops being information.
    [Fact]
    public async Task TheCounterIsUncappedAndStillCountsPastThree()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-uncapped-task");
        var service = await StageAsync(root.Path, taskId, "W1");

        var messages = new List<string>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            messages.Add((await RefuseCompletionAsync(service, taskId, "W1")).Message);
        }

        Assert.Equal(VerifierGate, messages[0]);
        Assert.EndsWith("  refusal 2 of this rule for you on this task", messages[1], StringComparison.Ordinal);
        Assert.EndsWith("  refusal 4 of this rule for you on this task", messages[3], StringComparison.Ordinal);
        Assert.EndsWith("  refusal 6 of this rule for you on this task", messages[5], StringComparison.Ordinal);
    }

    // One rule refusing about two different records is one lesson, not two. Nearly every refusal this
    // kernel raises interpolates an identifier, so a counter keyed on the raw text would restart at
    // one for every record the actor named and would never print anything at all.
    [Fact]
    public async Task TheSameRuleRefusingAboutADifferentRecordIsStillTheSameRule()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-one-rule-task");
        var service = await StageAsync(root.Path, taskId, "W1", "W2");

        var first = await RefuseCompletionAsync(service, taskId, "W1");
        var second = await RefuseCompletionAsync(service, taskId, "W2");

        Assert.Contains("'W1'", first.Message, StringComparison.Ordinal);
        Assert.Contains("'W2'", second.Message, StringComparison.Ordinal);
        Assert.EndsWith("  refusal 2 of this rule for you on this task", second.Message, StringComparison.Ordinal);
    }

    // One rule whose own sentence contains an apostrophe is still one rule. The dispatch refusal
    // raised at RunDispatchRules.cs:45 says "another actor's behalf" and then quotes two ids, and a
    // key that reads the possessive as an opening quote pairs it with the quote before the first id:
    // the pairing then runs out of step, both ids survive into the key, and one actor refused twice
    // is told nothing and leaves the retrospective entirely. The message here is the kernel's own
    // rather than a copy, which is the point — the defect was a real message that no test had been
    // given, and the first assertion is what fails if its wording ever moves.
    [Fact]
    public async Task ARuleWhoseSentenceCarriesAPossessiveIsStillOneRuleAcrossSubjects()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-possessive-task");
        var service = await StageAsync(root.Path, taskId, "W1");
        var lead = new ActorId("lead");
        // A planning lead holds ManageRuns, so the refusal below is about dispatching and not about
        // authority — the same staging OperatorDispatchTests uses for this rule.
        await Run(service, taskId, new AssignRoleCommand(
            Operator, null, NextCorrelation(), lead, RoleKind.PlanningLead, [Capability.ManageRuns]));

        var first = await RefuseDispatchAsync(service, taskId, lead, "RX1", new ActorId("alice"));
        var second = await RefuseDispatchAsync(service, taskId, lead, "RX2", new ActorId("bob"));

        Assert.Equal(
            "Only an operator can start a run on another actor's behalf; 'lead' cannot dispatch for 'alice'.",
            first.Message);
        Assert.StartsWith(
            "Only an operator can start a run on another actor's behalf; 'lead' cannot dispatch for 'bob'.",
            second.Message,
            StringComparison.Ordinal);
        Assert.EndsWith("  refusal 2 of this rule for you on this task", second.Message, StringComparison.Ordinal);
    }

    // And a second rule is a second lesson with its own count. Both directions are asserted in one
    // test: the other rule starts at one, and the first rule resumes at three rather than being
    // reset by the row that landed between its own two.
    [Fact]
    public async Task ADifferentRuleIsCountedSeparatelyAndDoesNotResetTheFirst()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-two-rules-task");
        var service = await StageAsync(root.Path, taskId, "W1");

        await RefuseCompletionAsync(service, taskId, "W1");
        await RefuseCompletionAsync(service, taskId, "W1");
        var otherRule = await Assert.ThrowsAsync<GovernanceException>(() => Run(
            service, taskId, new AddWorkItemCommand(
                Operator, null, NextCorrelation(), new WorkItemId("W9"), "Owned by nobody",
                new ActorId("nobody"), [], [])));
        var third = await RefuseCompletionAsync(service, taskId, "W1");

        Assert.Equal("Work owner 'nobody' has no assigned role.", otherRule.Message);
        Assert.EndsWith("  refusal 3 of this rule for you on this task", third.Message, StringComparison.Ordinal);
    }

    // The count is one actor's own repetition. Two strangers hit one rule here — the message names
    // the actor and quotes it, so both rows carry the same rule key and only the actor tells them
    // apart. Without the actor in the count, the second stranger would be told it had already been
    // refused by a rule it was meeting for the first time.
    [Fact]
    public async Task AnotherActorsRefusalsOfTheSameRuleAreNotCounted()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-per-actor-task");
        var service = await StageAsync(root.Path, taskId, "W1");

        var strangerFirst = await RefuseUnassignedAsync(service, taskId, new ActorId("stranger-one"));
        var strangerSecond = await RefuseUnassignedAsync(service, taskId, new ActorId("stranger-one"));
        var otherStranger = await RefuseUnassignedAsync(service, taskId, new ActorId("stranger-two"));

        Assert.Equal("Actor 'stranger-one' has no assigned role.", strangerFirst.Message);
        Assert.EndsWith(
            "  refusal 2 of this rule for you on this task", strangerSecond.Message, StringComparison.Ordinal);
        Assert.Equal("Actor 'stranger-two' has no assigned role.", otherStranger.Message);
    }

    // The counter goes below every line the rule itself printed, and the claim-resolution refusal is
    // the one rule that prints any: increment 1 appends two diagnostic lines naming what each record
    // points at. They are about this refusal; the counter is about the actor, which is why it is
    // last. ProviderLauncher matches a run-output refusal with Contains and with StartsWith, and both
    // survive a line appended at the end and neither would survive one inserted above.
    [Fact]
    public async Task TheCounterIsPrintedBelowTheDiagnosticLinesTheRuleItselfAdds()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-line-order-task");
        var service = await StageAsync(root.Path, taskId, "W1");
        await StageClaimAndEvidenceAsync(service, taskId, "C1", "E1");
        await StageClaimAndEvidenceAsync(service, taskId, "C2", "E2");

        var first = await RefuseWrongDirectionAsync(service, taskId, "C1", "E1");
        var second = await RefuseWrongDirectionAsync(service, taskId, "C2", "E2");

        Assert.Equal(
            [
                "Evidence 'E1' does not support claim 'C1'.",
                "  E1 supports: (none), refutes: (none)",
                "  C1 is supported by: (none), refuted by: (none)"
            ],
            Lines(first.Message));
        Assert.Equal(
            [
                "Evidence 'E2' does not support claim 'C2'.",
                "  E2 supports: (none), refutes: (none)",
                "  C2 is supported by: (none), refuted by: (none)",
                "  refusal 2 of this rule for you on this task"
            ],
            Lines(second.Message));
    }

    // The row keeps the kernel's own text. A counter written into the row would make the journal
    // self-referential — the next count would read rows whose text already carries a count — and
    // every consumer of a refusal's message would see a different message for each occurrence of one
    // rule. Both rows are compared to each other and to the first thrown message, so a revision that
    // persisted the augmented text fails here rather than at the next reader.
    [Fact]
    public async Task ThePersistedRowCarriesTheKernelsTextAndNeverTheCounter()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-row-task");
        var service = await StageAsync(root.Path, taskId, "W1");

        var first = await RefuseCompletionAsync(service, taskId, "W1");
        var second = await RefuseCompletionAsync(service, taskId, "W1");

        var rows = ReadJournal(root.Path, taskId);
        Assert.Equal(2, rows.Count);
        Assert.Equal(first.Message, rows[0]);
        Assert.Equal(first.Message, rows[1]);
        // The caller was told something the row does not carry, which is the whole shape of this
        // increment stated in one assertion.
        Assert.NotEqual(second.Message, rows[1]);
        Assert.DoesNotContain("refusal 2", rows[1], StringComparison.Ordinal);
    }

    // K4, and criterion 6. Nothing materialises on the count: three refusals here fired the counter
    // twice, and deleting the journal still leaves the task replaying to a byte-identical state. The
    // count reached the message and stopped there.
    [Fact]
    public async Task CountingChangesNothingThatReplayCanSee()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-replay-task");
        var service = await StageAsync(root.Path, taskId, "W1");
        await RefuseCompletionAsync(service, taskId, "W1");
        await RefuseCompletionAsync(service, taskId, "W1");
        var third = await RefuseCompletionAsync(service, taskId, "W1");

        var before = await service.GetStateAsync(taskId, CancellationToken.None);
        var stateBytes = await File.ReadAllBytesAsync(StatePath(root.Path, taskId));
        // Without this the assertions below would pass against a counter that never ran.
        Assert.EndsWith("  refusal 3 of this rule for you on this task", third.Message, StringComparison.Ordinal);

        File.Delete(JournalPath(root.Path, taskId));
        File.Delete(StatePath(root.Path, taskId));
        var replayed = await Service(root.Path).GetStateAsync(taskId, CancellationToken.None);

        Assert.Equal(before!.Version, replayed!.Version);
        Assert.Equal(stateBytes, await File.ReadAllBytesAsync(StatePath(root.Path, taskId)));
    }

    // A short count reported as a total understates repetition exactly when the journal is damaged,
    // which is the defect 2026-09-09_1010 recorded as RC1. Two rows of this rule are seeded beside
    // one line that does not parse, the refusal appends the third, and the number is stated as a
    // floor with the rows it could not read named.
    [Fact]
    public async Task APartlyReadableJournalReportsTheCountAsAFloorAndSaysWhy()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-floor-task");
        var service = await StageAsync(root.Path, taskId, "W1");
        await RefuseCompletionAsync(service, taskId, "W1");
        await RefuseCompletionAsync(service, taskId, "W1");
        await File.AppendAllTextAsync(JournalPath(root.Path, taskId), "{ this row is not json" + "\n");

        var third = await RefuseCompletionAsync(service, taskId, "W1");

        Assert.EndsWith(
            "  refusal 3 or more of this rule for you on this task; 1 journal row could not be read",
            third.Message,
            StringComparison.Ordinal);
    }

    // A row can be well-formed JSON and still unusable: this one carries no message, so the rule key
    // has nothing to read and throws where it reads it. A fault on one row must cost that row, the
    // same as a row that does not parse at all. Guarded only against a parse failure, the throw
    // escapes the loop instead, every row already counted is discarded, and a journal holding two
    // genuine occurrences beside one damaged row reports nothing at all — the understatement the
    // floor exists to prevent, reached by the one route it did not cover.
    [Fact]
    public async Task ARowThatParsesAndStillCannotBeReadCostsThatRowAndNotTheCount()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-unusable-row-task");
        var service = await StageAsync(root.Path, taskId, "W1");
        await RefuseCompletionAsync(service, taskId, "W1");
        await RefuseCompletionAsync(service, taskId, "W1");
        await File.AppendAllTextAsync(
            JournalPath(root.Path, taskId),
            """
            {"recordedAt":"2026-09-20T08:17:00+00:00","actorId":"operator","command":"CompleteWorkItemCommand","site":"service","taskVersion":9,"message":null}
            """ + "\n");

        var third = await RefuseCompletionAsync(service, taskId, "W1");

        Assert.EndsWith(
            "  refusal 3 or more of this rule for you on this task; 1 journal row could not be read",
            third.Message,
            StringComparison.Ordinal);
    }

    // The count is taken after the row for this refusal is appended, so a lost append leaves the file
    // one row short of the truth — short by exactly the row for the refusal being reported, which is
    // one row and is known to be one. It is added back before the number is printed, so the number is
    // the occurrence this refusal actually is and is stated as a total. The journal here is readable
    // and not writable, which is the one shape that fails the write and still answers the read; every
    // other damaged shape loses both and prints nothing.
    [Fact]
    public async Task ARefusalWhoseOwnRowWasNotWrittenIsStillTheOccurrenceItIs()
    {
        // The permission bits are the mechanism, so there is nothing to assert on Windows.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-lost-row-task");
        var service = await StageAsync(root.Path, taskId, "W1");
        await RefuseCompletionAsync(service, taskId, "W1");
        await RefuseCompletionAsync(service, taskId, "W1");
        var path = JournalPath(root.Path, taskId);
        File.SetUnixFileMode(path, UnixFileMode.UserRead);

        try
        {
            var third = await RefuseCompletionAsync(service, taskId, "W1");

            // Two rows are all the file holds and the third refusal is the one that could not be
            // written, so the actor is on their third and is told so. No row was unreadable, so
            // nothing is said about rows that could not be read and the number is not a floor: the
            // only shortfall is this refusal's own row, and its size is known.
            Assert.EndsWith(
                "  refusal 3 of this rule for you on this task",
                third.Message,
                StringComparison.Ordinal);
            Assert.Equal(2, ReadJournal(root.Path, taskId).Count);
        }
        finally
        {
            // Restored so the temporary directory can be removed.
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    // The same journal failure at the occurrence the increment exists to catch. Gated on the count
    // the file gives rather than on the count including this refusal, the second refusal of a rule
    // reads as one, one is below the gate, and the refusal says nothing at all — a silence, which is
    // the flattering direction and the one failure shape the rest of this path is written against.
    // The test above cannot see it: three refusals clear a gate of two either way, so only the second
    // occurrence distinguishes counting before the gate from counting after it.
    [Fact]
    public async Task ASecondRefusalWhoseOwnRowWasNotWrittenStillSaysItIsTheSecond()
    {
        // The permission bits are the mechanism, so there is nothing to assert on Windows.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-lost-second-row-task");
        var service = await StageAsync(root.Path, taskId, "W1");
        await RefuseCompletionAsync(service, taskId, "W1");
        var path = JournalPath(root.Path, taskId);
        File.SetUnixFileMode(path, UnixFileMode.UserRead);

        try
        {
            var second = await RefuseCompletionAsync(service, taskId, "W1");

            Assert.Equal(
                VerifierGate + Environment.NewLine + "  refusal 2 of this rule for you on this task",
                second.Message);
            // One row is what the file holds, and the count printed above is two. The assertion is
            // here because it is the whole difference between this test and the second-refusal test
            // at the top of this file: the number does not come from the file alone.
            Assert.Equal(VerifierGate, Assert.Single(ReadJournal(root.Path, taskId)));
        }
        finally
        {
            // Restored so the temporary directory can be removed.
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    // The refusal a caller is given on the second and every later occurrence is a replacement
    // exception, because GovernanceException is sealed and carries only a message. Its stack must be
    // the one the rule threw from: without the copy, the frame naming the rule is discarded exactly
    // when a rule has fired more than once, which is when it is most worth having, while the first
    // refusal keeps it through the bare rethrow. The first refusal's own trace is the expectation, so
    // this asserts the two agree rather than pinning a frame name that a later refactor would move.
    [Fact]
    public async Task TheCountedRefusalKeepsTheStackOfTheRuleThatRefused()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-stack-task");
        var service = await StageAsync(root.Path, taskId, "W1");

        var first = await RefuseCompletionAsync(service, taskId, "W1");
        var second = await RefuseCompletionAsync(service, taskId, "W1");

        var origin = (first.StackTrace ?? string.Empty).Split(Environment.NewLine)[0];
        // The deepest frame of the first refusal is the rule itself, which is what the second must
        // still carry. Named once here so that a test failing on the second assertion is failing
        // about the stack being replaced and not about the first refusal having moved.
        Assert.Contains("WorkItemLifecycleRules", origin, StringComparison.Ordinal);
        Assert.Contains(origin, second.StackTrace ?? string.Empty, StringComparison.Ordinal);
        Assert.EndsWith("  refusal 2 of this rule for you on this task", second.Message, StringComparison.Ordinal);
    }

    // An entirely unreadable journal is a first occurrence as far as the count can tell, and saying
    // "at least 1" would put a line under a rule the actor may genuinely be meeting for the first
    // time. The refusal is left exactly as it is, and the assertion that the row was still appended
    // is what proves the write path stayed alive through a read that found nothing.
    [Fact]
    public async Task AJournalWhoseRowsNoneParseLeavesTheRefusalAsItIs()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-corrupt-task");
        var service = await StageAsync(root.Path, taskId, "W1");
        await File.WriteAllTextAsync(JournalPath(root.Path, taskId), "not json\nalso not json\n");

        var refusal = await RefuseCompletionAsync(service, taskId, "W1");

        Assert.Equal(VerifierGate, refusal.Message);
        Assert.Equal(VerifierGate, Assert.Single(ReadJournal(root.Path, taskId)));
    }

    // The command never fails because of the journal. A directory where the file belongs fails both
    // the append and the read, and the refusal the operator is waiting for arrives unchanged, with
    // the type the exit status is derived from.
    [Fact]
    public async Task AJournalThatCannotBeWrittenOrReadStillLeavesTheRefusalIntact()
    {
        using var root = new TemporaryDirectory();
        var taskId = new TaskId("repetition-unwritable-task");
        var service = await StageAsync(root.Path, taskId, "W1");
        Directory.CreateDirectory(JournalPath(root.Path, taskId));

        var refusal = await RefuseCompletionAsync(service, taskId, "W1");
        var second = await RefuseCompletionAsync(service, taskId, "W1");

        Assert.Equal(VerifierGate, refusal.Message);
        Assert.Equal(VerifierGate, second.Message);
    }

    // The four shapes the count must survive, asked of the journal directly, because the service
    // path above can only reach them through a file it has already written to.
    [Fact]
    public async Task AnAbsentEmptyOrUnopenableJournalCountsNothingAndThrowsNothing()
    {
        using var root = new TemporaryDirectory();
        var journal = new RefusalJournal();
        var actor = new ActorId("operator");
        var absent = Path.Combine(root.Path, "absent");
        var empty = Path.Combine(root.Path, "empty");
        var unopenable = Path.Combine(root.Path, "unopenable");
        Directory.CreateDirectory(absent);
        Directory.CreateDirectory(empty);
        Directory.CreateDirectory(unopenable);
        await File.WriteAllTextAsync(Path.Combine(empty, "refusals.jsonl"), string.Empty);
        Directory.CreateDirectory(Path.Combine(unopenable, "refusals.jsonl"));

        Assert.Equal(RefusalRecurrence.None, await journal.TryCountAsync(absent, actor, VerifierGate));
        Assert.Equal(RefusalRecurrence.None, await journal.TryCountAsync(empty, actor, VerifierGate));
        Assert.Equal(RefusalRecurrence.None, await journal.TryCountAsync(unopenable, actor, VerifierGate));

        // The same three answers are what a journal holding one unrelated row gives, so the test
        // above cannot be passing on a method that returns None unconditionally.
        await journal.TryAppendUnderTaskLockAsync(absent, new RefusalRecord(
            DateTimeOffset.UtcNow, actor, "CompleteWorkItemCommand", RefusalSite.Service, 4,
            VerifierGate, null));
        Assert.Equal(new RefusalRecurrence(1, 0), await journal.TryCountAsync(absent, actor, VerifierGate));
    }

    // A journal that is present and holds the rows, and that this process cannot open. The three
    // shapes above all stop at the existence check and so leave the read's own failure path
    // untested: narrowing the catch that guards it changed nothing they assert. This is the case
    // that reaches it, and it is the one that matters most — a read on a command path that throws
    // turns telemetry into an outage, refusing commands the kernel accepts.
    [Fact]
    public async Task AJournalThatExistsAndCannotBeOpenedCountsNothingRatherThanThrowing()
    {
        // The permission bits are the mechanism, so there is nothing to assert on Windows.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryDirectory();
        var journal = new RefusalJournal();
        var actor = new ActorId("operator");
        var taskDirectory = Path.Combine(root.Path, "sealed-journal");
        Directory.CreateDirectory(taskDirectory);
        await journal.TryAppendUnderTaskLockAsync(taskDirectory, new RefusalRecord(
            DateTimeOffset.UtcNow, actor, "CompleteWorkItemCommand", RefusalSite.Service, 4,
            VerifierGate, null));
        await journal.TryAppendUnderTaskLockAsync(taskDirectory, new RefusalRecord(
            DateTimeOffset.UtcNow, actor, "CompleteWorkItemCommand", RefusalSite.Service, 5,
            VerifierGate, null));
        var path = Path.Combine(taskDirectory, "refusals.jsonl");
        // Readable, this is a count of two. The assertion below is about the file becoming
        // unopenable and not about it being empty or absent.
        Assert.Equal(new RefusalRecurrence(2, 0), await journal.TryCountAsync(taskDirectory, actor, VerifierGate));

        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            Assert.True(File.Exists(path));
            Assert.Equal(RefusalRecurrence.None, await journal.TryCountAsync(taskDirectory, actor, VerifierGate));
        }
        finally
        {
            // Restored so the temporary directory can be removed.
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static string NextCorrelation() => "c" + Guid.NewGuid().ToString("N")[..8];

    private static readonly ActorId Operator = new("operator");

    private static readonly ActorId Worker = new("worker");

    // Open, a brief for the operator, a worker to hold the runs, and for each named work item one
    // item with one completed working run. That leaves work complete refused on the verifier gate —
    // the second of its four, so the refusal is a real rule and not the easiest one to reach.
    private static async Task<FileGovernedTaskService> StageAsync(
        string root,
        TaskId taskId,
        params string[] workItems)
    {
        var service = Service(root);
        await Run(service, taskId, new OpenTaskCommand(
            Operator, null, NextCorrelation(), taskId, "Task", "Goal"));
        await ContextBrief.RecordAsync(service, taskId.Value);
        await Run(service, taskId, new AssignRoleCommand(
            Operator, null, NextCorrelation(), Worker, RoleKind.Worker, [Capability.BuildContext]));

        var index = 0;
        foreach (var item in workItems)
        {
            index++;
            await Run(service, taskId, new AddWorkItemCommand(
                Operator, null, NextCorrelation(), new WorkItemId(item), $"Work {item}", Operator, [], []));
            await Run(service, taskId, new StartRunCommand(
                Operator, null, NextCorrelation(), new RunId($"R{index}"), new WorkItemId(item),
                "codex", null, SubjectActorId: Worker));
            await Run(service, taskId, new CompleteRunCommand(
                Operator, null, NextCorrelation(), new RunId($"R{index}"), AgentRunStatus.Completed,
                $"session-{index}"));
        }

        return service;
    }

    private static async Task StageClaimAndEvidenceAsync(
        IGovernedTaskService service,
        TaskId taskId,
        string claimId,
        string evidenceId)
    {
        await Run(service, taskId, new AddClaimCommand(
            Operator, null, NextCorrelation(), new ClaimId(claimId), $"Claim {claimId}", null));
        // Attached to nothing in either direction, which is the shape the directional refusal exists
        // to tell apart from evidence attached the wrong way round.
        await Run(service, taskId, new AddEvidenceCommand(
            Operator, null, NextCorrelation(), new EvidenceId(evidenceId), "source-read",
            $"a.cs:{evidenceId}", "summary", [], []));
    }

    private static Task<GovernanceException> RefuseCompletionAsync(
        IGovernedTaskService service,
        TaskId taskId,
        string workItem) =>
        Assert.ThrowsAsync<GovernanceException>(() => Run(
            service, taskId, new CompleteWorkItemCommand(
                Operator, null, NextCorrelation(), new WorkItemId(workItem))));

    private static Task<GovernanceException> RefuseUnassignedAsync(
        IGovernedTaskService service,
        TaskId taskId,
        ActorId stranger) =>
        Assert.ThrowsAsync<GovernanceException>(() => Run(
            service, taskId, new AddClaimCommand(
                stranger, null, NextCorrelation(), new ClaimId("CX"), "Claim CX", null)));

    // The run id is new on each call and the subject is named but never assigned a role: the
    // dispatch-authority refusal speaks before either the subject's role or the work item is looked
    // at, which is the ordering RunDispatchRules states at its own site.
    private static Task<GovernanceException> RefuseDispatchAsync(
        IGovernedTaskService service,
        TaskId taskId,
        ActorId actor,
        string runId,
        ActorId subject) =>
        Assert.ThrowsAsync<GovernanceException>(() => Run(
            service, taskId, new StartRunCommand(
                actor, null, NextCorrelation(), new RunId(runId), new WorkItemId("W1"), "codex", null,
                SubjectActorId: subject)));

    private static Task<GovernanceException> RefuseWrongDirectionAsync(
        IGovernedTaskService service,
        TaskId taskId,
        string claimId,
        string evidenceId) =>
        Assert.ThrowsAsync<GovernanceException>(() => Run(
            service, taskId, new ResolveClaimCommand(
                Operator, null, NextCorrelation(), new ClaimId(claimId), ClaimStatus.Validated,
                [new EvidenceId(evidenceId)])));

    private static string[] Lines(string message) =>
        message.Split(Environment.NewLine, StringSplitOptions.None);

    private static string JournalPath(string root, TaskId taskId) =>
        Path.Combine(root, taskId.Value, "refusals.jsonl");

    private static string StatePath(string root, TaskId taskId) =>
        Path.Combine(root, taskId.Value, "state.json");

    private static IReadOnlyList<string> ReadJournal(string root, TaskId taskId)
    {
        var path = JournalPath(root, taskId);
        if (!File.Exists(path))
        {
            return [];
        }

        // Unparseable lines are skipped rather than thrown on, because one test seeds them
        // deliberately and the question it asks is whether the kernel still appended its own row
        // beside them.
        var messages = new List<string>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                messages.Add(JsonDocument.Parse(line).RootElement.GetProperty("message").GetString()!);
            }
            catch (JsonException)
            {
            }
        }

        return messages;
    }

    private static FileGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new StagePlacingCommandHandler(reducer), reducer);
    }

    private static Task<CommandOutcome> Run(IGovernedTaskService service, TaskId taskId, LedgerCommand command) =>
        service.ExecuteAsync(taskId, ContextBrief.WithServedSkills(command), CancellationToken.None);
}
