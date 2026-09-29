using AILedger.Core.Assurance;
using AILedger.Core.Episodes;

namespace AILedger.Tests.HandoffAssurance;

public sealed class AssuranceRecoveryTests
{
    [Fact]
    public async Task ExactRetryReturnsSamePartialIdentityAcrossRestartAndChangedCandidateDoesNotPromoteIt()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var partial = await f.ReportAsync("reviewer", status: "partial", checkStatus: "unknown");
        f.OpenSessions();
        var replay = await f.CallAsync("reviewer", "record_assurance", partial.Request);
        Assert.True(replay.Replayed); Assert.Equal(partial.Response.Receipt, replay.Receipt);
        Assert.Equal("request_conflict", (await f.CallAsync("reviewer", "record_assurance", partial.Request with { Status = "complete" })).Error?.Code);
        await File.AppendAllTextAsync(Path.Combine(f.Root, "a.cs"), "changed");
        Assert.True((await f.CallAsync("reviewer", "record_assurance", partial.Request)).Replayed);
        var fresh = await f.ReportAsync("reviewer", status: "partial", checkStatus: "unknown", supersedes: partial.Response.Receipt!.Id);
        Assert.Null(fresh.Response.Error); Assert.NotEqual(partial.Request.ExpectedBinding, fresh.Request.ExpectedBinding);
        Assert.Equal("not_accepted", (await f.InspectAsync()).Acceptance);
    }

    [Fact]
    public async Task CommittedReceiptSurvivesLostAcknowledgmentWithoutDuplicateReport()
    {
        using var f = await AssuranceFixture.CreateAsync();
        f.Store = new LostResponseStore(f.Store); f.OpenSessions();
        var snapshot = (await f.InspectAsync("reviewer")).Snapshot;
        var request = new ReadAssuranceRequest(1, "lost-response", "a", snapshot.BindingSha256, ["a.cs"]);
        Assert.Equal("outcome_unknown", (await f.CallAsync("reviewer", "read_assurance", request)).Error?.Code);
        var replay = await f.CallAsync("reviewer", "read_assurance", request);
        Assert.True(replay.Replayed); Assert.NotNull(replay.Receipt);
        Assert.Single((await f.InspectAsync("reviewer")).History);
    }

    [Fact]
    public async Task UnobservedCheckEndingRequiresExplicitReconciliationAndNeverRerunsOnRetry()
    {
        using var f = await AssuranceFixture.CreateAsync();
        var partial = await f.ReportAsync("verifier", status: "partial", checkStatus: "unknown");
        f.Runner.Outcome = "crash"; var binding = partial.Request.ExpectedBinding;
        var request = new RunAssuranceChecksRequest(1, "crashed-check", "a", binding, ["unit"]);
        await Assert.ThrowsAsync<HostDeath>(() => f.CallAsync("verifier", "run_assurance_checks", request));
        Assert.Equal("inspection_interrupted", (await f.CallAsync("verifier", "run_assurance_checks", request)).Error?.Code);
        Assert.Equal("inspection_interrupted", (await f.CallAsync("verifier", "run_assurance_checks", request with { RequestId = "another" })).Error?.Code);
        Assert.Equal(1, f.Runner.Calls); Assert.Single((await f.InspectAsync()).PendingChecks);
        await Assert.ThrowsAsync<AssuranceRefusal>(() => f.Sessions["operator"].ReconcileChecksAsync(request.RequestId, "verifier", false, default));
        await f.Sessions["operator"].ReconcileChecksAsync(request.RequestId, "verifier", true, default);
        Assert.Empty((await f.InspectAsync()).PendingChecks);
        Assert.Equal("inspection_interrupted", (await f.CallAsync("verifier", "run_assurance_checks", request)).Error?.Code);
        f.Runner.Outcome = "success";
        var repaired = await f.ReportAsync("verifier", supersedes: partial.Response.Receipt!.Id);
        Assert.Null(repaired.Response.Error); Assert.Equal("not_accepted", (await f.InspectAsync()).Acceptance);
    }

    private sealed class LostResponseStore(IEpisodeStore inner) : IEpisodeStore
    {
        private bool _lost;
        public Task<IAsyncDisposable> AcquireAsync(string id, CancellationToken token) => inner.AcquireAsync(id, token);
        public Task<IReadOnlyList<EpisodeRecord>> ReadAsync(string id, CancellationToken token) => inner.ReadAsync(id, token);
        public Task<int> CountUncommittedAsync(string id, CancellationToken token) => inner.CountUncommittedAsync(id, token);
        public async Task<EpisodeRecord> AppendAsync(string id, string kind, object data, CancellationToken token)
        {
            var record = await inner.AppendAsync(id, kind, data, token);
            if (!_lost && kind == "assurance_entry") { _lost = true; throw new IOException("Simulated lost acknowledgment after durable append"); }
            return record;
        }
    }
}
