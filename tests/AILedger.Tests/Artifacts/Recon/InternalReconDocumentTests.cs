using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Tests.Artifacts.Recon;

public sealed class InternalReconDocumentTests
{
    [Theory]
    [InlineData("malformed")]
    [InlineData("trailing")]
    [InlineData("duplicate-property")]
    [InlineData("extra-property")]
    [InlineData("missing-property")]
    [InlineData("version")]
    [InlineData("task")]
    [InlineData("hash")]
    [InlineData("missing-claim")]
    [InlineData("duplicate-claim")]
    [InlineData("unknown-claim")]
    [InlineData("domain")]
    [InlineData("null")]
    [InlineData("row-extra")]
    [InlineData("row-duplicate")]
    [InlineData("blank-report")]
    [InlineData("template")]
    public void StrictDocumentRefusesInvalidContentWithoutRecordingAnEvent(string defect)
    {
        var f = new InternalReconFixture();
        f.Start();
        var body = f.Body();
        var node = JsonNode.Parse(body)!.AsObject();
        var rows = node["assessments"]!.AsArray();
        switch (defect)
        {
            case "extra-property": node["extra"] = true; break;
            case "missing-property": node.Remove("report"); break;
            case "version": node["schemaVersion"] = 2; break;
            case "task": node["taskId"] = "other"; break;
            case "hash": node["claimSetHash"] = new string('0', 64); break;
            case "missing-claim": rows.Clear(); break;
            case "duplicate-claim": rows.Add(rows[0]!.DeepClone()); break;
            case "unknown-claim": rows[0]!["claimId"] = "unknown"; break;
            case "domain": rows[0]!["domain"] = "Internal"; break;
            case "null": rows[0]!["domain"] = null; break;
            case "row-extra": rows[0]!["extra"] = true; break;
            case "blank-report": node["report"] = " "; break;
        }
        body = node.ToJsonString();
        body = defect switch
        {
            "malformed" => "{",
            "trailing" => body + "{}",
            "duplicate-property" => body.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
            "row-duplicate" => body.Replace("\"domain\":\"internal\"", "\"domain\":\"internal\",\"domain\":\"internal\""),
            "template" => JsonSerializer.Serialize(InternalReconDocuments.CreateTemplate(f.Task.State)),
            _ => body
        };
        var version = f.Task.State.Version;
        Assert.Throws<GovernanceException>(() => f.File(body));
        Assert.Equal(version, f.Task.State.Version);
        Assert.Empty(f.Task.State.Artifacts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void R2_ClaimChangesInvalidateBinding(bool resolve)
    {
        var f = new InternalReconFixture();
        f.Eligible();
        var before = InternalReconDocuments.ComputeClaimSetHash(f.Task.State);
        if (resolve) f.Resolve();
        else f.Task.Apply(new AddClaimCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(),
            new ClaimId("new"), "Additional uncertainty", null));
        Assert.NotEqual(before, InternalReconDocuments.ComputeClaimSetHash(f.Task.State));
        Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Design));
        f.Start("refresh");
        f.File(id: "refreshed", supersedes: "recon");
        f.Complete();
        f.Task.Transition(TaskStage.Design);
    }

    [Fact]
    public void UnattachedEvidenceContextAndProducerCompletionPreserveBinding()
    {
        var f = new InternalReconFixture();
        f.Start();
        f.File();
        var hash = InternalReconDocuments.ComputeClaimSetHash(f.Task.State);
        f.Task.BuildContext(f.Task.OperatorId);
        f.Task.Apply(new AddEvidenceCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(),
            new EvidenceId("unattached"), "source-read", "example.cs:1", "Not attached to a claim", [], []));
        f.Complete();
        Assert.Equal(hash, InternalReconDocuments.ComputeClaimSetHash(f.Task.State));
        f.Task.Transition(TaskStage.Design);
    }
    [Fact]
    public void SupersessionInvalidatesBindingAndRetainsHistoricalAssessment()
    {
        var f = new InternalReconFixture();
        f.Task.Apply(new AddClaimCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(),
            new ClaimId("replacement"), "Refined uncertainty", null));
        f.Eligible();
        var old = InternalReconDocuments.ComputeClaimSetHash(f.Task.State);
        f.Task.Apply(new ResolveClaimCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(),
            new ClaimId("C-topic"), ClaimStatus.Superseded, [], new ClaimId("replacement")));
        Assert.NotEqual(old, InternalReconDocuments.ComputeClaimSetHash(f.Task.State));
        Assert.Throws<GovernanceException>(() => f.Task.Transition(TaskStage.Design));
        Assert.Equal(2, InternalReconDocuments.CreateTemplate(f.Task.State).Assessments.Count);
    }

    [Fact]
    public void DigestUsesFrozenOrdinalCompactJsonContract()
    {
        var f = new InternalReconFixture();
        f.Task.Apply(new AddClaimCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(),
            new ClaimId("a"), "λ", null));
        f.Task.Apply(new AddClaimCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(),
            new ClaimId("A"), "ASCII", null));
        const string canonical = """[["A","ASCII","Open",[],null],["C-topic","The stage arms are reachable from state the kernel already holds","Open",[],null],["a","\u03BB","Open",[],null]]""";
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        Assert.Equal(expected, InternalReconDocuments.ComputeClaimSetHash(f.Task.State));
    }

    [Fact]
    public void EmptyClaimSetHasCanonicalEmptyArrayDigestAndNoAssessments()
    {
        var task = new AILedger.Tests.Support.TestTask(placeEntryStages: false);
        var template = InternalReconDocuments.CreateTemplate(task.State);
        Assert.Empty(template.Assessments);
        Assert.Equal("4f53cda18c2baa0c0354bb5f9a3ecbe5ed12ab4d8e11ba873c2f11161202b945", template.ClaimSetHash);
    }

    [Fact]
    public void EvidenceIdOrderDoesNotChangeCanonicalBinding()
    {
        string Hash(bool reverse)
        {
            var f = new InternalReconFixture();
            var claim = new ClaimId("C-topic");
            var ids = new[] { new EvidenceId("a"), new EvidenceId("A") };
            foreach (var id in ids)
                f.Task.Apply(new AddEvidenceCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(),
                    id, "test-run", "proof", "proof", [claim], []));
            f.Task.Apply(new ResolveClaimCommand(f.Task.OperatorId, null, f.Task.NextCorrelation(),
                claim, ClaimStatus.Validated, reverse ? ids.AsEnumerable().Reverse().ToArray() : ids));
            return InternalReconDocuments.ComputeClaimSetHash(f.Task.State);
        }
        Assert.Equal(Hash(false), Hash(true));
    }

}
