using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.ClaimDispositions;
using AILedger.Core.Findings;
using AILedger.Storage.ClaimDispositions;

namespace AILedger.Tests.ClaimDispositions;

[Collection(ClaimDispositionsRecordingCollection.Name)]
public sealed class ClaimDispositionsOperationTests
{
    [Fact]
    public async Task ExplicitJudgmentsPreserveRationaleAttributionEvidenceAndOriginalReceipt()
    {
        using var f = new ClaimDispositionsFixture();
        await f.OpenAsync();
        Assert.All((await f.StateAsync()).Claims.Values, c => Assert.Equal(ClaimStatus.Open, c.Status));
        var first = await f.RecordAsync();
        Assert.Null(first.Error);
        Assert.Equal(9, first.Receipt!.LedgerVersion);
        var state = await f.StateAsync();
        Assert.Equal(ClaimStatus.Validated, state.Claims[new("C1")].Status);
        Assert.Equal(ClaimStatus.Rejected, state.Claims[new("C2")].Status);
        var resolutions = (await f.HistoryAsync()).Where(e => e.Data is ClaimResolved).ToArray();
        for (var i = 0; i < 2; i++)
        {
            var input = ClaimDispositionsFixture.Request().Dispositions[i];
            var e = resolutions[i];
            var resolved = (ClaimResolved)e.Data;
            Assert.Equal(input.Rationale, resolved.Rationale);
            Assert.Equal(input.Rationale, first.Receipt.Dispositions[i].Rationale);
            Assert.Equal(f.Actor, e.ActorId);
            Assert.Equal("stable", e.CorrelationId);
            Assert.Equal(input.Evidence.Select(x => x.EvidenceId), resolved.EvidenceIds.Select(x => x.Value));
        }
        await f.ExecuteAsync(new ResolveClaimCommand(f.Actor, null, "reverse", new("C1"), ClaimStatus.Rejected, [new("E3")]));
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var retry = await f.RecordAsync();
        Assert.True(retry.Replayed);
        Assert.NotEqual(first.AttemptId, retry.AttemptId);
        Assert.Equal(ClaimDispositionsReceiptEnvelope.Serialize(first.Receipt), ClaimDispositionsReceiptEnvelope.Serialize(retry.Receipt!));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
        Assert.Equal(ClaimStatus.Rejected, (await f.StateAsync()).Claims[new("C1")].Status);
    }

    [Theory]
    [InlineData("claim", "invalid_reference", ".claim.claim_id")]
    [InlineData("evidence", "invalid_reference", ".evidence[0].evidence_id")]
    [InlineData("direction", "kernel_refused", "")]
    [InlineData("state", "state_conflict", ".expected_status")]
    public async Task LateRefusalLeavesNoAcceptedPrefix(string error, string code, string path)
    {
        using var f = new ClaimDispositionsFixture();
        await f.OpenAsync();
        var request = ClaimDispositionsFixture.Request();
        var late = request.Dispositions[1];
        late = error switch
        {
            "claim" => late with { Claim = new("missing") },
            "evidence" => late with { Evidence = [new("missing")] },
            "direction" => late with { Evidence = [new("E1")] },
            _ => late with { ExpectedStatus = ClaimStatus.Validated }
        };
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var failed = await f.RecordAsync(request with { Dispositions = [request.Dispositions[0], late] });
        Assert.Equal(code, failed.Error?.Code);
        Assert.Equal("dispositions[1]" + path, failed.Error?.ItemPath);
        Assert.Equal("not_committed", failed.Error?.CommitState);
        Assert.Null(failed.Receipt);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
        if (error == "direction") Assert.Contains("does not refute claim 'C2'", failed.Error!.Message);
        Assert.Null((await f.RecordAsync()).Error);
    }

    [Fact]
    public async Task ConcurrentRetriesAndCompetingJudgmentsCommitOnlyOnce()
    {
        using var f = new ClaimDispositionsFixture();
        await f.OpenAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.RecordAsync()));
        Assert.All(results, x => Assert.Null(x.Error));
        Assert.Single(results.Where(x => !x.Replayed));
        Assert.Single(results.Select(x => x.Receipt!.TransactionId).Distinct());
        Assert.Equal(8, results.Select(x => x.AttemptId).Distinct().Count());
        Assert.Equal("state_conflict", (await f.RecordAsync(ClaimDispositionsFixture.Request("competing"))).Error?.Code);
        var request = ClaimDispositionsFixture.Request();
        Assert.Equal("idempotency_conflict", (await f.RecordAsync(request with { Dispositions =
            [request.Dispositions[0] with { Rationale = "Different judgment" }, request.Dispositions[1]] })).Error?.Code);
        Assert.Equal("idempotency_conflict", (await f.RecordAsync(binding: f.Binding with { CorrelationId = "different" })).Error?.Code);
        Assert.True((await f.Service(cap: 2, bytes: 1).RecordClaimDispositionsAsync(f.Binding, request, default)).Replayed);
    }

    [Fact]
    public async Task AuthorityAndRunOwnershipComeFromTrustedBindingAndRecordedCapabilities()
    {
        using var f = new ClaimDispositionsFixture();
        await f.OpenAsync();
        foreach (var role in new[] { RoleKind.Researcher, RoleKind.Worker, RoleKind.ImplementationLead, RoleKind.Verifier, RoleKind.CodeReviewer })
        {
            var actor = new ActorId(role.ToString());
            await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "assign", actor, role, [Capability.AddClaim, Capability.AddEvidence]));
            var denied = await f.RecordAsync(binding: f.Binding with { ActorId = actor });
            Assert.Equal("kernel_refused", denied.Error?.Code);
            Assert.Contains("ResolveClaim", denied.Error!.Message);
        }
        foreach (var binding in new[] { f.Binding with { AllowRecordClaimDispositions = false },
            f.Binding with { AllowRunless = false }, f.Binding with { RunId = new("missing"), CorrelationId = "missing" },
            f.Binding with { CausationId = new("missing") } })
            Assert.Equal("authorization_denied", (await f.RecordAsync(binding: binding)).Error?.Code);
        var lead = new ActorId("planner");
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "assign", lead, RoleKind.PlanningLead, [Capability.ResolveClaim]));
        await f.ExecuteAsync(new StartRunCommand(f.Actor, null, "start", new("RP"), null, "codex", null, SubjectActorId: lead));
        var trusted = f.Binding with { ActorId = lead, RunId = new("RP"), CorrelationId = "RP", AllowRunless = false };
        Assert.Equal("authorization_denied", (await f.RecordAsync(binding: trusted with { ActorId = f.Actor })).Error?.Code);
        Assert.Null((await f.RecordAsync(binding: trusted)).Error);
        await f.ExecuteAsync(new CompleteRunCommand(f.Actor, null, "close", new("RP"), AgentRunStatus.Completed, "session"));
        Assert.True((await f.RecordAsync(binding: trusted)).Replayed);
        await f.ExecuteAsync(new AssignRoleCommand(f.Actor, null, "revoke", lead, RoleKind.PlanningLead, [Capability.AddClaim]));
        Assert.Equal("kernel_refused", (await f.RecordAsync(binding: trusted)).Error?.Code);
    }

    [Fact]
    public async Task CapacityAndOperationKeyIsolationPreserveEarlierReceipts()
    {
        using var f = new ClaimDispositionsFixture();
        await f.OpenAsync();
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        foreach (var service in new[] { f.Service(cap: 8), f.Service(bytes: before.Length) })
        {
            var result = await service.RecordClaimDispositionsAsync(f.Binding, ClaimDispositionsFixture.Request(), default);
            Assert.Equal("capacity_exceeded", result.Error?.Code);
            Assert.Equal("not_committed", result.Error?.CommitState);
            Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
        }
        var request = ClaimDispositionsFixture.Request();
        var findings = await f.Service().RecordAsync(new(f.TaskId, f.Actor, null, "stable", AllowRecordFindings: true, AllowRunless: true),
            new(1, request.RequestId, [new("extra", "Separate finding")], []), default);
        Assert.Null(findings.Error);
        var alternatives = await f.Service().RecordAlternativesAsync(new(f.TaskId, f.Actor, null, "stable", AllowRecordAlternatives: true, AllowRunless: true),
            new(1, request.RequestId, [new("a", "Separate alternative", "Reason")]), default);
        Assert.Null(alternatives.Error);
        var dispositions = await f.RecordAsync();
        Assert.Null(dispositions.Error);
        Assert.Equal(3, new[] { findings.Receipt!.TransactionId, alternatives.Receipt!.TransactionId, dispositions.Receipt!.TransactionId }.Distinct().Count());
        Assert.Equal(ClaimStatus.Open, (await f.StateAsync()).Claims[new(findings.Receipt.Findings[0].ClaimId)].Status);
    }

    [Fact]
    public async Task OptionalRationalePreservesLegacyReplayAndRejectsOnlyInvalidNewFields()
    {
        using var f = new ClaimDispositionsFixture();
        await f.OpenAsync();
        var outcome = await f.ExecuteAsync(new ResolveClaimCommand(f.Actor, null, "old", new("C1"), ClaimStatus.Validated, [new("E1")]));
        var legacy = outcome.Events[0];
        Assert.DoesNotContain("rationale", JsonSerializer.Serialize(legacy, AILedger.Storage.LedgerJson.CreateOptions()));
        var reducer = new AILedger.Core.Domain.TaskReducer();
        GovernedTaskState? prior = null;
        foreach (var e in (await f.HistoryAsync()).Where(e => e.EventId != legacy.EventId)) prior = reducer.Apply(prior, e);
        Assert.Equal(ClaimStatus.Validated, reducer.Apply(prior, legacy).Claims[new("C1")].Status);
        var malformed = legacy with { Data = ((ClaimResolved)legacy.Data) with { Rationale = " " } };
        Assert.Throws<AILedger.Core.Domain.GovernanceException>(() => reducer.Apply(prior, malformed));
        await Assert.ThrowsAsync<AILedger.Core.Domain.GovernanceException>(() => f.ExecuteAsync(new ResolveClaimCommand(f.Actor, null,
            "blank", new("C2"), ClaimStatus.Rejected, [new("E2")], Rationale: " ")));
    }

    [Fact]
    public void BoundsRejectMalformedTypedRequestsAndSnapshotMutableLists()
    {
        var request = ClaimDispositionsFixture.Single();
        var item = request.Dispositions[0];
        var invalid = new[] { item with { Claim = null! }, item with { Evidence = null! }, item with { Evidence = [] },
            item with { Evidence = [null!] }, item with { Evidence = [new("E1"), new("E1")] },
            item with { ExpectedStatus = ClaimStatus.Rejected }, item with { Status = ClaimStatus.Superseded },
            item with { Status = (ClaimStatus)99 }, item with { Rationale = " " }, item with { Rationale = "\ud800" },
            item with { Rationale = new string('x', 8193) }, item with { Claim = new(" C1") } };
        foreach (var bad in invalid)
            Assert.Throws<FindingsRequestException>(() => ClaimDispositionsValidation.Snapshot(request with { Dispositions = [bad] }));
        foreach (var items in new IReadOnlyList<ClaimDispositionInput>[] { [], [item, item], Enumerable.Repeat(item, 33).ToArray(),
                     Enumerable.Range(0,32).Select(i => item with { Key = "j"+i, Claim = new("C"+i), Rationale = new string('x',8192) }).ToArray() })
            Assert.Throws<FindingsRequestException>(() => ClaimDispositionsValidation.Snapshot(request with { Dispositions = items }));
        var evidence = new[] { new DispositionEvidenceReference("E1") };
        var snapshot = ClaimDispositionsValidation.Snapshot(request with { Dispositions = [item with { Evidence = evidence }] });
        evidence[0] = new("changed");
        Assert.Equal("E1", snapshot.Dispositions[0].Evidence[0].EvidenceId);
    }
}
