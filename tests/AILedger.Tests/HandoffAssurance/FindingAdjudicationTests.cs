using AILedger.Core.Assurance;

namespace AILedger.Tests.HandoffAssurance;

public sealed class FindingAdjudicationTests
{
    [Theory]
    [InlineData("refuted", true)] [InlineData("not_applicable", true)] [InlineData("upheld", false)]
    public async Task IndependentSourceTraceAdjudicatesButOnlyResolvedFindingsCanBeAccepted(string decision, bool accepts)
    {
        using var f = await AssuranceFixture.CreateAsync();
        var review = await f.ReportAsync("reviewer", findings: [new("F", "defect", "Reviewer assertion to investigate", ["a.cs"], [])
            { RequirementIds = ["behavior"], Uncertainty = ["Needs source trace"] }]);
        var verify = await f.ReportAsync("verifier");
        var synthesis = await f.ReportAsync("synthesizer");
        var id = review.Response.Receipt!.Id + ":F";
        var judged = await f.CallAsync("synthesizer", "record_assurance", synthesis.Request with
        {
            RequestId = "judgment", Supersedes = synthesis.Response.Receipt!.Id,
            FindingJudgments = [new(1, id, decision, "Independent source trace establishes the disposition", ["behavior"], [],
                [new("source_trace", synthesis.Request.ReadReceipts[0], "a.cs", "return 1;")])]
        });
        Assert.Null(judged.Error);
        var result = await f.AcceptAsync([review.Response.Receipt.Id, verify.Response.Receipt!.Id, judged.Receipt!.Id],
            [new(id, decision, "Explicit accepting judgment", [judged.Receipt.Id])]);
        Assert.Equal(accepts, result.Error is null);
        Assert.Contains(id, (await f.InspectAsync()).UnresolvedFindings.Concat(accepts ? [id] : []));
        var original = await f.CallAsync("operator", "inspect_assurance", new InspectAssuranceRequest(1, "a", review.Response.Receipt.Id));
        Assert.Contains("Reviewer assertion to investigate", original.Data!.Value.GetRawText());
    }

    [Theory]
    [InlineData("no_evidence")] [InlineData("invented_quote")] [InlineData("wrong_requirement")]
    [InlineData("self_disposition")] [InlineData("accepted_risk")]
    public async Task UnsupportedDispositionCannotEraseOriginalFinding(string failure)
    {
        using var f = await AssuranceFixture.CreateAsync();
        var review = await f.ReportAsync("reviewer", findings: [new("F", "defect", "Independent finding", ["a.cs"], [])]);
        var principal = failure == "self_disposition" ? "reviewer" : "synthesizer";
        var report = principal == "reviewer" ? review : await f.ReportAsync(principal);
        var id = review.Response.Receipt!.Id + ":F";
        var judgment = new AssuranceFindingJudgment(1, id, failure == "accepted_risk" ? "accepted_risk" : "refuted", "Attempted disposition",
            [failure == "wrong_requirement" ? "unknown" : "behavior"], [], failure == "no_evidence" ? [] :
            [new("source_trace", report.Request.ReadReceipts[0], "a.cs", failure == "invented_quote" ? "return 42;" : "return 1;")]);
        var result = await f.CallAsync(principal, "record_assurance", report.Request with
            { RequestId = "invalid-judgment", Supersedes = report.Response.Receipt!.Id, FindingJudgments = [judgment] });
        Assert.NotNull(result.Error);
        Assert.Contains(id, (await f.InspectAsync()).UnresolvedFindings);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReproductionSupportsIndependentRefutationOrRepair(bool repair)
    {
        using var f = await AssuranceFixture.CreateAsync();
        var original = await f.ReportAsync("reviewer", findings: [new("F", "defect", "Finding requiring investigation", ["a.cs"], [])]);
        if (repair) await File.AppendAllTextAsync(Path.Combine(f.Root, "a.cs"), "// actual changed candidate");
        var review = await f.ReportAsync("reviewer", supersedes: original.Response.Receipt!.Id);
        var verification = await f.ReportAsync("verifier");
        var synthesis = await f.ReportAsync("synthesizer");
        var id = original.Response.Receipt.Id + ":F";
        var decision = repair ? "repaired" : "refuted";
        var adjudicated = await f.CallAsync("synthesizer", "record_assurance", synthesis.Request with
        {
            RequestId = "reproduction-judgment", Supersedes = synthesis.Response.Receipt!.Id,
            FindingJudgments = [new(1, id, decision, "Independent reproduction supports this result", ["behavior"], [],
                [new("reproduction", verification.Request.Checks[0].TestReceipts[0], "unit", "Scripted observed check output")])]
        });
        Assert.Null(adjudicated.Error);
        var accepted = await f.AcceptAsync([review.Response.Receipt!.Id, verification.Response.Receipt!.Id, adjudicated.Receipt!.Id],
            [new(id, decision, "Explicit evidence-based acceptance", [adjudicated.Receipt.Id])]);
        Assert.Null(accepted.Error);
        Assert.Empty((await f.InspectAsync()).UnresolvedFindings);
    }

    [Fact]
    public async Task UncertainAdjudicationRemainsUnresolvedAndCannotBeHiddenBySupersession()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var original = await f.ReportAsync("reviewer", findings: [new("F", "ambiguity", "Disputed requirement", ["a.txt"], [])]);
        var verify = await f.ReportAsync("verifier");
        var synthesis = await f.ReportAsync("synthesizer");
        var id = original.Response.Receipt!.Id + ":F";
        var request = synthesis.Request with
        {
            RequestId = "uncertain-judgment", Supersedes = synthesis.Response.Receipt!.Id,
            FindingJudgments = [new(1, id, "refuted", "Source trace is insufficient to settle the external premise", ["behavior"], ["External premise unresolved"],
                [new("source_trace", synthesis.Request.ReadReceipts[0], "a.txt", "Return one")])]
        };
        var judgment = await f.CallAsync("synthesizer", "record_assurance", request); Assert.Null(judgment.Error);
        Assert.NotNull((await f.AcceptAsync([original.Response.Receipt.Id, verify.Response.Receipt!.Id, judgment.Receipt!.Id],
            [new(id, "refuted", "Attempted acceptance", [judgment.Receipt.Id])])).Error);
        var next = await f.ReportAsync("synthesizer", supersedes: judgment.Receipt.Id);
        Assert.NotNull((await f.AcceptAsync([original.Response.Receipt.Id, verify.Response.Receipt.Id, next.Response.Receipt!.Id],
            [new(id, "refuted", "Omission is not resolution", [next.Response.Receipt.Id])])).Error);
    }

    [Fact]
    public async Task ConflictingAdjudicatorsMustResolveTheirDisagreementBeforeAcceptance()
    {
        using var f = await AssuranceFixture.CreateAsync();
        f.Policy = f.Policy with { Principals = f.Policy.Principals.Append(new AssurancePrincipal("second-synthesis", "synthesis", true,
            DateTimeOffset.UtcNow.AddHours(1), ["a", "b", "c"])).ToArray() };
        f.OpenSessions();
        var review = await f.ReportAsync("reviewer", findings: [new("F", "defect", "Disputed finding", ["a.cs"], [])]);
        var verification = await f.ReportAsync("verifier");
        var first = await f.ReportAsync("synthesizer"); var second = await f.ReportAsync("second-synthesis");
        var finding = review.Response.Receipt!.Id + ":F";
        RecordAssuranceRequest Judgment(RecordAssuranceRequest request, string predecessor, string key, string decision) => request with
        {
            RequestId = key, Supersedes = predecessor, FindingJudgments = [new(1, finding, decision, "Independent source trace judgment", ["behavior"], [],
                [new("source_trace", request.ReadReceipts[0], "a.cs", "return 1;")])]
        };
        var upheld = await f.CallAsync("synthesizer", "record_assurance", Judgment(first.Request, first.Response.Receipt!.Id, "upheld", "upheld"));
        var refuted = await f.CallAsync("second-synthesis", "record_assurance", Judgment(second.Request, second.Response.Receipt!.Id, "refuted", "refuted"));
        Assert.Null(upheld.Error); Assert.Null(refuted.Error);
        Assert.NotNull((await f.AcceptAsync([review.Response.Receipt.Id, verification.Response.Receipt!.Id, refuted.Receipt!.Id],
            [new(finding, "refuted", "Cannot ignore the upheld judgment", [refuted.Receipt.Id])])).Error);
        var corrected = await f.CallAsync("synthesizer", "record_assurance", Judgment(first.Request, upheld.Receipt!.Id, "corrected", "refuted"));
        Assert.Null(corrected.Error);
        Assert.Null((await f.AcceptAsync([review.Response.Receipt.Id, verification.Response.Receipt.Id, refuted.Receipt.Id, corrected.Receipt!.Id],
            [new(finding, "refuted", "Both independent adjudicators now agree on the source evidence", [refuted.Receipt.Id, corrected.Receipt.Id])])).Error);
    }

    [Fact]
    public async Task RevocationDuringDelayedAcceptanceIsRevalidatedAtCommit()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var review = await f.ReportAsync("reviewer"); var verification = await f.ReportAsync("verifier");
        var snapshot = (await f.InspectAsync()).Snapshot;
        var source = new RevokingPolicy(f);
        var host = new AILedger.Providers.Assurance.AssuranceService(f.Store, source, f.Policy,
            new("operator", "delayed-acceptance", "external-client", null), f.Runner);
        var result = await host.InvokeAsync("accept_assurance", AssuranceValidation.Json(new AcceptAssuranceRequest(1,
            "delayed", "a", snapshot.BindingSha256, [review.Response.Receipt!.Id, verification.Response.Receipt!.Id], "Judgment delayed while authority changes", [])), default);
        Assert.Equal("stale_assurance", result.Error?.Code);
        Assert.Equal("not_accepted", (await f.InspectAsync()).Acceptance);
    }

    private sealed class RevokingPolicy(AssuranceFixture fixture) : IAssurancePolicySource
    {
        private int _reads;
        public Task<AssurancePolicy> ReadAsync(CancellationToken token)
        {
            if (++_reads == 2) fixture.Policy = fixture.Policy with { Principals = fixture.Policy.Principals.Select(p =>
                p.Id == "reviewer" ? p with { Enabled = false } : p).ToArray() };
            return Task.FromResult(fixture.Policy);
        }
    }

    [Fact]
    public async Task ChangedCandidateAndReportSupersessionDoNotSilentlyDisposeDefects()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var review = await f.ReportAsync("reviewer", findings: [new("F", "defect", "Persistent finding", ["a.cs"], [])]);
        await File.AppendAllTextAsync(Path.Combine(f.Root, "a.cs"), "// claimed repair");
        var next = await f.ReportAsync("reviewer", supersedes: review.Response.Receipt!.Id);
        var verify = await f.ReportAsync("verifier");
        Assert.Contains(review.Response.Receipt.Id + ":F", (await f.InspectAsync()).UnresolvedFindings);
        Assert.Equal("unresolved_disagreement", (await f.AcceptAsync([next.Response.Receipt!.Id, verify.Response.Receipt!.Id])).Error?.Code);
    }
}
