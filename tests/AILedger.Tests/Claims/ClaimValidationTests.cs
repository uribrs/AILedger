using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Claims;

public sealed class ClaimValidationTests
{
    [Fact]
    public void ValidatedClaimRequiresExistingEvidence()
    {
        var task = new TestTask();
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), new ClaimId("C1"), "Claim", null));

        Assert.Throws<GovernanceException>(() => task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), new ClaimId("C1"), ClaimStatus.Validated, [])));
        Assert.Throws<GovernanceException>(() => task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), new ClaimId("C1"), ClaimStatus.Validated, [new EvidenceId("missing")])));
    }

    [Theory]
    [InlineData(ClaimStatus.Validated)]
    [InlineData(ClaimStatus.Rejected)]
    public void ClaimResolutionRejectsDirectionallyUnrelatedEvidence(ClaimStatus status)
    {
        var task = new TestTask();
        var target = new ClaimId("C1");
        var unrelated = new ClaimId("C2");
        var evidenceId = new EvidenceId("E1");
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), target, "Target", null));
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), unrelated, "Other", null));
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), evidenceId, "test", "citation", "summary",
            [unrelated], []));

        var exception = Assert.Throws<GovernanceException>(() => task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), target, status, [evidenceId])));

        Assert.Contains(status == ClaimStatus.Validated ? "does not support" : "does not refute",
            exception.Message, StringComparison.Ordinal);
    }

    // The sentence the refusal has always opened with. Everything the diagnostic adds goes after it,
    // on its own lines, so a reader and an assertion that knew the old refusal still recognise this
    // one. This test is the guard on that: it pins the first line byte for byte.
    [Theory]
    [InlineData(ClaimStatus.Validated, "Evidence 'E1' does not support claim 'C1'.")]
    [InlineData(ClaimStatus.Rejected, "Evidence 'E1' does not refute claim 'C1'.")]
    public void DirectionalRefusalKeepsItsOriginalFirstLine(ClaimStatus status, string expectedFirstLine)
    {
        var message = RefuseDirectionally(status, supports: [], refutes: []);

        Assert.Equal(expectedFirstLine, message.Split(Environment.NewLine)[0]);
    }

    // The shape behind every one of the sixteen paired refusals on 2026-09-17_1440: the evidence
    // pointed at nothing at all, and the old refusal could only say the pair was wrong.
    [Fact]
    public void DirectionalRefusalNamesEvidenceThatPointsAtNothing()
    {
        var message = RefuseDirectionally(ClaimStatus.Validated, supports: [], refutes: []);

        Assert.Equal(
            "Evidence 'E1' does not support claim 'C1'." + Environment.NewLine +
            "  E1 supports: (none), refutes: (none)" + Environment.NewLine +
            "  C1 is supported by: (none), refuted by: (none)",
            message);
    }

    // The evidence points somewhere, just not here — and something else already points at the claim.
    // Both lines carry content, and the second one names the record the caller should have passed.
    [Fact]
    public void DirectionalRefusalNamesWhatEachRecordActuallyPointsAt()
    {
        var task = Staged(out var target, out var other);
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E1"), "test", "citation", "summary",
            [other], []));
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E2"), "test", "citation", "summary",
            [target], []));

        var exception = Assert.Throws<GovernanceException>(() => task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), target, ClaimStatus.Validated, [new EvidenceId("E1")])));

        Assert.Equal(
            "Evidence 'E1' does not support claim 'C1'." + Environment.NewLine +
            "  E1 supports: C2, refutes: (none)" + Environment.NewLine +
            "  C1 is supported by: E2, refuted by: (none)",
            exception.Message);
    }

    // The pair is right and only the direction is wrong. Both lines carry the opposite direction as
    // well as the requested one, so this shape names the record that is already attached the wrong
    // way round instead of reporting it as absent.
    [Fact]
    public void DirectionalRefusalNamesEvidenceAttachedInTheOppositeDirection()
    {
        var message = RefuseDirectionally(ClaimStatus.Validated, supports: [], refutes: [new ClaimId("C1")]);

        Assert.Equal(
            "Evidence 'E1' does not support claim 'C1'." + Environment.NewLine +
            "  E1 supports: (none), refutes: C1" + Environment.NewLine +
            "  C1 is supported by: (none), refuted by: E1",
            message);
    }

    // The test that was missing. A first revision printed only the requested direction, so evidence
    // attached the wrong way round and evidence attached to nothing produced the same two lines and
    // the tests agreed with each other. Whatever the format is, these two shapes must not collide.
    [Fact]
    public void DirectionalRefusalTellsTheWrongDirectionApartFromPointingAtNothing()
    {
        var wrongDirection = RefuseDirectionally(
            ClaimStatus.Validated, supports: [], refutes: [new ClaimId("C1")]);
        var pointsAtNothing = RefuseDirectionally(ClaimStatus.Validated, supports: [], refutes: []);

        Assert.NotEqual(pointsAtNothing, wrongDirection);
    }

    // The direction the command asked for comes first on both lines, so a reader scanning a refused
    // rejection finds what it asked about where a refused validation puts it.
    [Fact]
    public void DirectionalRefusalLeadsWithTheDirectionTheCommandAsked()
    {
        var message = RefuseDirectionally(
            ClaimStatus.Rejected, supports: [new ClaimId("C1")], refutes: []);

        Assert.Equal(
            "Evidence 'E1' does not refute claim 'C1'." + Environment.NewLine +
            "  E1 refutes: (none), supports: C1" + Environment.NewLine +
            "  C1 is refuted by: (none), supported by: E1",
            message);
    }

    // Two evidence records pointing at the claim come back in the same order every time, so one
    // refusal can be compared with the next. The pair is 'EB' and 'Ea' because those two are the
    // cheapest ids the comparers disagree about: ordinal puts the upper-case letter first, the
    // current culture puts the lower-case one first, and they are added in the culture's order. A
    // pair of digits would have read the same under every comparer and under no sorting at all,
    // which is all the earlier E1/E2/E9 version of this test could prove.
    [Fact]
    public void DirectionalRefusalOrdersNamesOrdinally()
    {
        var task = Staged(out var target, out var other);
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E1"), "test", "citation", "summary",
            [], []));
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), new EvidenceId("Ea"), "test", "citation", "summary",
            [target], []));
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), new EvidenceId("EB"), "test", "citation", "summary",
            [target, other], []));

        var exception = Assert.Throws<GovernanceException>(() => task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), target, ClaimStatus.Validated, [new EvidenceId("E1")])));

        Assert.Equal(
            "  C1 is supported by: EB, Ea, refuted by: (none)",
            exception.Message.Split(Environment.NewLine)[2]);
    }

    // A resolution whose evidence does point the right way still succeeds and still emits its event.
    // The diagnostic sits after the direction check, so nothing on the accepting path went near it.
    [Fact]
    public void DirectionallySoundResolutionStillSucceeds()
    {
        var task = Staged(out var target, out _);
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E1"), "test", "citation", "summary",
            [target], []));

        var outcome = task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), target, ClaimStatus.Validated, [new EvidenceId("E1")]));

        Assert.Contains(outcome.Events, @event => @event.Data is ClaimResolved);
        Assert.Equal(ClaimStatus.Validated, task.State.Claims[target].Status);
    }

    private static string RefuseDirectionally(ClaimStatus status, ClaimId[] supports, ClaimId[] refutes)
    {
        var task = Staged(out var target, out _);
        task.Apply(new AddEvidenceCommand(
            task.OperatorId, null, task.NextCorrelation(), new EvidenceId("E1"), "test", "citation", "summary",
            supports, refutes));

        return Assert.Throws<GovernanceException>(() => task.Apply(new ResolveClaimCommand(
            task.OperatorId, null, task.NextCorrelation(), target, status, [new EvidenceId("E1")]))).Message;
    }

    private static TestTask Staged(out ClaimId target, out ClaimId other)
    {
        var task = new TestTask();
        target = new ClaimId("C1");
        other = new ClaimId("C2");
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), target, "Target", null));
        task.Apply(new AddClaimCommand(task.OperatorId, null, task.NextCorrelation(), other, "Other", null));
        return task;
    }
}
