using System.Text.Json;
using AILedger.Core.Assurance;
using AILedger.Core.Handoffs;

namespace AILedger.Tests.HandoffAssurance;

public sealed class AssuranceOperationTests
{
    [Fact]
    public async Task IndependentEvidenceSupportsOnlyExplicitAuthorizedAcceptance()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var review = await f.ReportAsync("reviewer"); var verification = await f.ReportAsync("verifier");
        Assert.Null(review.Response.Error); Assert.Null(verification.Response.Error);
        Assert.Equal("not_accepted", (await f.InspectAsync()).Acceptance);
        var accepted = await f.AcceptAsync([review.Response.Receipt!.Id, verification.Response.Receipt!.Id]);
        Assert.Null(accepted.Error); Assert.Equal("operator", accepted.Receipt!.Principal);
        Assert.Equal("accepted", (await f.InspectAsync()).Acceptance);
        var tests = await f.InspectAsync("verifier");
        Assert.Empty(tests.UntestedCriteria); Assert.Empty(tests.UninspectedPaths); Assert.Equal(1, f.Runner.Calls);
        Assert.Equal("external_candidate_host_snapshot; implementation execution is not observed", tests.Snapshot.Origin);
    }

    [Theory]
    [InlineData("a.cs", "candidate changed: a.cs")]
    [InlineData("a.txt", "requirement changed: a.txt")]
    [InlineData("a.source", "source changed: a.source")]
    public async Task RelevantChangeInvalidatesOnlyAffectedAreaAndTransitiveConsumers(string path, string reason)
    {
        using var f = await AssuranceFixture.CreateAsync();
        var a = await f.ReportAsync("reviewer"); var b = await f.ReportAsync("reviewer", "b"); var c = await f.ReportAsync("reviewer", "c");
        await File.AppendAllTextAsync(Path.Combine(f.Root, path), "changed");
        var changed = await f.InspectAsync();
        Assert.Contains(changed.History, h => h.Id == a.Response.Receipt!.Id && h.Status == "reassessment_required" && h.Reasons.Contains(reason));
        Assert.Contains((await f.InspectAsync(area: "b")).History, h => h.Id == b.Response.Receipt!.Id && h.Reasons.Contains("relied-on area changed: a"));
        Assert.Contains((await f.InspectAsync(area: "c")).History, h => h.Id == c.Response.Receipt!.Id && h.Status == "current");
        var stale = await f.CallAsync("reviewer", "record_assurance", a.Request with { RequestId = "stale", Supersedes = a.Response.Receipt!.Id });
        Assert.Equal("stale_candidate", stale.Error?.Code);
        var old = await f.CallAsync("operator", "inspect_assurance", new InspectAssuranceRequest(1, "a", a.Response.Receipt.Id));
        Assert.Equal(a.Request.ExpectedBinding, old.Data!.Value.GetProperty("payload").GetProperty("snapshot").GetProperty("binding_sha256").GetString());
    }

    [Theory]
    [InlineData("partial", "pass")] [InlineData("complete", "unknown")]
    [InlineData("complete", "not_checked")] [InlineData("complete", "fail")]
    public async Task PartialUnknownAndFailedInspectionNeverPassesAcceptance(string status, string observation)
    {
        using var f = await AssuranceFixture.CreateAsync();
        var review = await f.ReportAsync("reviewer", status: status, checkStatus: observation);
        var verification = await f.ReportAsync("verifier");
        Assert.Null(review.Response.Error);
        var accept = await f.AcceptAsync([review.Response.Receipt!.Id, verification.Response.Receipt!.Id]);
        Assert.Equal("incomplete_assurance", accept.Error?.Code);
    }

    [Theory]
    [InlineData("failure")] [InlineData("missing")] [InlineData("cancelled")] [InlineData("timeout")] [InlineData("truncated")]
    public async Task ActualUnavailableOrFailedChecksCannotSupportAuthoredPass(string outcome)
    {
        using var f = await AssuranceFixture.CreateAsync(); f.Runner.Outcome = outcome;
        var report = await f.ReportAsync("verifier");
        Assert.Equal("unsupported_pass", report.Response.Error?.Code);
        Assert.Contains("behavior", (await f.InspectAsync("verifier")).UntestedCriteria);
        Assert.Equal("not_accepted", (await f.InspectAsync()).Acceptance);
    }

    [Fact]
    public async Task CapturedBytesAreTestedAndMutationOfThemIsRecordedAsFailure()
    {
        using var f = await AssuranceFixture.CreateAsync();
        f.Runner.BeforeExit = invocation =>
        {
            Assert.NotEqual(f.Root, invocation.WorkingDirectory);
            Assert.Equal("return 1;\n", File.ReadAllText(Path.Combine(invocation.WorkingDirectory, "a.cs")));
            File.WriteAllText(Path.Combine(invocation.WorkingDirectory, "a.cs"), "mutated");
        };
        Assert.Equal("unsupported_pass", (await f.ReportAsync("verifier")).Response.Error?.Code);
        Assert.Equal("return 1;\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "a.cs")));
    }

    [Fact]
    public async Task ImplementerCannotRecordIndependentAssuranceOrUseAcceptanceTool()
    {
        using var f = await AssuranceFixture.CreateAsync();
        Assert.Equal("self_approval", (await f.ReportAsync("implementer")).Response.Error?.Code);
        Assert.Equal("authorization_denied", (await f.CallAsync("reviewer", "accept_assurance", new { })).Error?.Code);
    }

    [Fact]
    public async Task MissingReadEvidenceAndInventedTestReceiptsAreRefused()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var report = await f.ReportAsync("reviewer", status: "partial", checkStatus: "unknown");
        Assert.Equal("unobserved_inspection", (await f.CallAsync("reviewer", "record_assurance", report.Request with
            { RequestId = "missing-read", ReadReceipts = [], Supersedes = report.Response.Receipt!.Id })).Error?.Code);
        Assert.Equal("invalid_reference", (await f.CallAsync("reviewer", "record_assurance", report.Request with
            { RequestId = "invented-test", Checks = [new("behavior", "pass", "assertion", ["invented"])], Supersedes = report.Response.Receipt!.Id })).Error?.Code);
    }

    [Fact]
    public async Task RevocationStopsReadsAndMakesPriorAcceptanceNeedReassessment()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var review = await f.ReportAsync("reviewer"); var verify = await f.ReportAsync("verifier");
        Assert.Null((await f.AcceptAsync([review.Response.Receipt!.Id, verify.Response.Receipt!.Id])).Error);
        f.Policy = f.Policy with { Principals = f.Policy.Principals.Select(p => p.Id == "reviewer" ? p with { Enabled = false } : p).ToArray() };
        Assert.Equal("authorization_denied", (await f.CallAsync("reviewer", "inspect_assurance", new InspectAssuranceRequest(1, "a"))).Error?.Code);
        var inspect = await f.InspectAsync(); Assert.Equal("not_accepted", inspect.Acceptance);
        Assert.Contains(inspect.History, h => h.Operation == "accept_assurance" && h.Status == "reassessment_required");
    }

    [Fact]
    public async Task ContradictionsPersistAcrossCheckpointsAndRequireAnAttributableDecision()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var review = await f.ReportAsync("reviewer", findings: [new("F1", "ambiguity", "Potential requirement conflict", ["a.txt"], [])]);
        var verify = await f.ReportAsync("verifier");
        var reports = new[] { review.Response.Receipt!.Id, verify.Response.Receipt!.Id };
        Assert.Equal("unresolved_disagreement", (await f.AcceptAsync(reports)).Error?.Code);
        var next = await f.ReportAsync("reviewer", supersedes: review.Response.Receipt.Id);
        reports[0] = next.Response.Receipt!.Id;
        Assert.Equal("unresolved_disagreement", (await f.AcceptAsync(reports)).Error?.Code);
        var finding = review.Response.Receipt.Id + ":F1";
        var accepted = await f.AcceptAsync(reports, [new(finding, "not_applicable", "Independent captured requirements and observations disambiguate the concern", reports)]);
        Assert.Null(accepted.Error);
        Assert.Empty((await f.InspectAsync()).UnresolvedFindings);
        var original = await f.CallAsync("operator", "inspect_assurance", new InspectAssuranceRequest(1, "a", review.Response.Receipt.Id));
        Assert.Contains("Potential requirement conflict", original.Data!.Value.GetRawText()); // preserved despite disposition
    }

    [Fact]
    public async Task IndependentInspectorsCannotRetrieveEachOthersNarrativesButSynthesisCan()
    {
        using var f = await AssuranceFixture.CreateAsync(); var review = await f.ReportAsync("reviewer");
        var request = new InspectAssuranceRequest(1, "a", review.Response.Receipt!.Id);
        Assert.Equal("invalid_reference", (await f.CallAsync("verifier", "inspect_assurance", request)).Error?.Code);
        Assert.Null((await f.CallAsync("synthesizer", "inspect_assurance", request)).Error);
        Assert.Empty((await f.InspectAsync("verifier")).History);
    }

    [Theory]
    [InlineData("{\"schema_version\":1,\"area_id\":\"a\",\"principal\":\"operator\"}")]
    [InlineData("{\"schema_version\":1,\"schema_version\":1,\"area_id\":\"a\"}")]
    [InlineData("{\"area_id\":\"a\"}")]
    public async Task AuthoredPayloadCannotGrantAuthorityOrRelaxStrictShape(string json)
    {
        using var f = await AssuranceFixture.CreateAsync(); using var document = JsonDocument.Parse(json);
        Assert.Equal("invalid_request", (await f.Sessions["reviewer"].InvokeAsync("inspect_assurance", document.RootElement, default)).Error?.Code);
    }
    [Fact]
    public async Task RequirementCriterionChangeDoesNotInvalidateUnrelatedAcceptedArea()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var review = await f.ReportAsync("reviewer", "c"); var verify = await f.ReportAsync("verifier", "c");
        Assert.Null((await f.AcceptAsync([review.Response.Receipt!.Id, verify.Response.Receipt!.Id], area: "c")).Error);
        f.Policy = f.Policy with { Areas = f.Policy.Areas.Select(a => a.Id == "a" ? a with
            { Criteria = [new("behavior", "Changed intended behavior", "unit")] } : a).ToArray() };
        f.OpenSessions();
        Assert.Equal("accepted", (await f.InspectAsync(area: "c")).Acceptance);
    }

    [Fact]
    public async Task NewContradictionInvalidatesEndorsedConsumerWithoutChangingCandidateBytes()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var reviewA = await f.ReportAsync("reviewer"); var verifyA = await f.ReportAsync("verifier");
        var reviewB = await f.ReportAsync("reviewer", "b"); var verifyB = await f.ReportAsync("verifier", "b");
        var reportsB = new[] { reviewB.Response.Receipt!.Id, verifyB.Response.Receipt!.Id };
        Assert.Equal("dependency_reassessment", (await f.AcceptAsync(reportsB, area: "b")).Error?.Code);
        Assert.Null((await f.AcceptAsync([reviewA.Response.Receipt!.Id, verifyA.Response.Receipt!.Id])).Error);
        Assert.Null((await f.AcceptAsync(reportsB, area: "b")).Error);
        var before = (await f.InspectAsync(area: "b")).Snapshot.BindingSha256;
        var correction = await f.ReportAsync("reviewer", supersedes: reviewA.Response.Receipt.Id,
            findings: [new("new-finding", "contradiction", "The source conflicts with the requirement", ["a.source", "a.txt"], [])]);
        Assert.Null(correction.Response.Error);
        var consumer = await f.InspectAsync(area: "b");
        Assert.Equal(before, consumer.Snapshot.BindingSha256); Assert.Equal("not_accepted", consumer.Acceptance);
        Assert.Contains(consumer.History, h => h.Operation == "accept_assurance" && h.Reasons.Any(r => r.Contains("Relied-on area 'a'")));
    }

    [Fact]
    public async Task TargetedSynthesisPreservesAttributableCrossAreaContradiction()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var source = await f.ReportAsync("reviewer", "c", findings: [new("source-finding", "ambiguity", "Cross-area interface needs clarification", ["c.source"], [])]);
        var target = source.Response.Receipt!.Id + ":source-finding";
        var synthesis = await f.ReportAsync("synthesizer", findings: [new("cross-area", "contradiction", "This requirement conflicts with area c", ["a.txt"], [target])]);
        Assert.Null(synthesis.Response.Error); Assert.Equal("synthesizer", synthesis.Response.Receipt!.Principal);
        Assert.Contains(target, synthesis.Response.Data!.Value.GetRawText());
        var review = await f.ReportAsync("reviewer"); var verify = await f.ReportAsync("verifier");
        Assert.Equal("unresolved_disagreement", (await f.AcceptAsync([review.Response.Receipt!.Id, verify.Response.Receipt!.Id])).Error?.Code);
        var finding = synthesis.Response.Receipt.Id + ":cross-area";
        Assert.Contains(finding, (await f.InspectAsync()).UnresolvedFindings);
    }

}
