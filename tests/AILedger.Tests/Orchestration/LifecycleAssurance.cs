using System.Text.Json;
using AILedger.Cli.Assurance;
using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
using AILedger.Core.Handoffs;
using AILedger.Storage;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Tests.Orchestration;

// Exercises the real assurance transport/check owner alongside the intake/preparation fixture.
internal sealed class LifecycleAssurance(string authority, string store, FileGovernedTaskService host)
{
    private string? _verification;
    private readonly List<string> _findings = [];
    internal async Task<bool> VerifyAsync(CognitiveRelay relay, string run)
    {
        var inspected = await relay.CallAsync("inspect_assurance", Json(new InspectAssuranceRequest(1, "a")));
        var snapshot = inspected.GetProperty("data").Deserialize<AssuranceInspection>(HandoffJson.Options)!.Snapshot;
        var paths = snapshot.Inputs.Select(i => i.Path).ToArray();
        var read = await relay.CallAsync("read_assurance", Json(new ReadAssuranceRequest(1, run + "-read", "a", snapshot.BindingSha256, paths)));
        var tests = await relay.CallAsync("run_assurance_checks", Json(new RunAssuranceChecksRequest(1, run + "-check", "a", snapshot.BindingSha256, ["unit"])));
        Assert.Equal("recorded", tests.GetProperty("status").GetString());
        var passed = tests.GetProperty("data").Deserialize<AssuranceTestBatch>(HandoffJson.Options)!.Tests.All(t => t.Outcome == "succeeded");
        var report = await relay.CallAsync("record_assurance", Json(new RecordAssuranceRequest(1, run + "-report", "a", snapshot.BindingSha256,
            "complete", "Independent observed return-one check", paths, [Id(read)],
            [new("behavior", passed ? "pass" : "fail", "Captured shell assertion on declared source", [Id(tests)])],
            passed ? [] : [new("incorrect-return", "defect", "Implementation does not return one", ["a.cs"], []) { RequirementIds = ["behavior"] }], [], _verification)));
        Assert.True(report.GetProperty("status").GetString() == "recorded", report.GetRawText());
        _verification = Id(report);
        if (!passed) _findings.Add(_verification + ":incorrect-return");
        return passed;
    }

    internal async Task AcceptAsync()
    {
        var review = await AssuranceHost.OpenAsync(authority, store, new("requirements-review", "requirements-review-session", "external-client", null), null, default, host);
        var snapshot = (await RepairContinuationTests.InspectAsync(review, "a")).Snapshot;
        var paths = snapshot.Inputs.Select(i => i.Path).ToArray();
        var read = await review.InvokeAsync("read_assurance", Json(new ReadAssuranceRequest(1, "review-read", "a", snapshot.BindingSha256, paths)), default);
        var report = await review.InvokeAsync("record_assurance", Json(new RecordAssuranceRequest(1, "review-report", "a", snapshot.BindingSha256,
            "complete", "Independent requirements inspection", paths, [read.Receipt!.Id], [new("behavior", "pass", "Source satisfies captured requirement", [])], [], [])), default);
        Assert.Null(report.Error);
        var reports = new List<string> { _verification!, report.Receipt!.Id };
        var dispositions = new List<AssuranceDisposition>();
        if (_findings.Count > 0)
        {
            var synthesis = await AssuranceHost.OpenAsync(authority, store, new("lead", "independent-repair-disposition", "external-client", null), null, default, host);
            var observed = await synthesis.InvokeAsync("read_assurance", Json(new ReadAssuranceRequest(1, "repair-read", "a", snapshot.BindingSha256, paths)), default);
            var judgment = await synthesis.InvokeAsync("record_assurance", Json(new RecordAssuranceRequest(1, "repair-judgment", "a", snapshot.BindingSha256,
                "complete", "Independent trace confirms repair of original finding", paths, [observed.Receipt!.Id],
                [new("behavior", "pass", "Fresh independent verification and exact source trace", [])], [], [])
            {
                FindingJudgments = _findings.Select(finding => new AssuranceFindingJudgment(1, finding, "repaired", "Changed implementation now returns one", ["behavior"], [],
                    [new("source_trace", observed.Receipt.Id, "a.cs", "return 1;")])).ToArray(),
                RepairImpact = new(1, _findings[0].Split(':')[0], ["a"], ["behavior"], [], "Single declared area; all source and requirement inputs inspected", ["unit"], _findings, [], [_findings[0].Split(':')[0]], [])
            }), default);
            Assert.True(judgment.Error is null, Json(judgment).GetRawText());
            reports.Add(judgment.Receipt!.Id);
            dispositions.AddRange(_findings.Select(finding => new AssuranceDisposition(finding, "repaired", "Independent current adjudication", [judgment.Receipt.Id])));
        }
        var acceptor = await AssuranceHost.OpenAsync(authority, store, new("operator", "explicit-lifecycle-acceptance", "external-client", null), null, default, host);
        var decision = new AcceptAssuranceRequest(1, "accept-candidate", "a", snapshot.BindingSha256, reports, "Explicit acceptance of current independently checked candidate", dispositions);
        var accepted = await acceptor.InvokeAsync("accept_assurance", Json(decision), default);
        Assert.True(accepted.Error is null, Json(accepted).GetRawText());
        var replay = await acceptor.InvokeAsync("accept_assurance", Json(decision), default);
        Assert.True(replay.Replayed); Assert.Equal(accepted.Receipt!.Id, replay.Receipt!.Id);
    }
    private static string Id(JsonElement response) => response.GetProperty("receipt").GetProperty("id").GetString()!;
}
