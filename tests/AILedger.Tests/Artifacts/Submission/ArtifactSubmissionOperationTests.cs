using System.Text;
using System.Text.Json;
using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Core.Findings;
using AILedger.Core.Alternatives;
using AILedger.Storage.Artifacts;
using AILedger.Tests.Alternatives;

namespace AILedger.Tests.Artifacts.Submission;

[Collection(AlternativesRecordingCollection.Name)]
public sealed class ArtifactSubmissionOperationTests
{
    [Fact]
    public async Task ContentRegistrationAndReceiptCommitTogetherAndReplayAfterRunCompletes()
    {
        using var f = new ArtifactSubmissionFixture();
        await f.OpenAsync();
        var before = await f.StateAsync();
        var request = ArtifactSubmissionFixture.Request();
        var result = await f.SubmitAsync(request with { ExpectedContentSha256 = ArtifactSubmissionIdentity.ContentHash(request.Content) });
        Assert.Null(result.Error);
        var receipt = result.Receipt!;
        var state = await f.StateAsync();
        var artifact = state.Artifacts[new ArtifactId(receipt.Artifact.ArtifactId)];
        Assert.Equal(request.Content, artifact.Content);
        Assert.Equal(request.Title.Trim(), receipt.Artifact.Title);
        Assert.Equal(Encoding.UTF8.GetByteCount(request.Content), receipt.Artifact.ContentBytes);
        Assert.Equal(ArtifactSubmissionIdentity.ContentHash(request.Content), receipt.Artifact.ContentSha256);
        Assert.Equal(f.Actor, artifact.Provenance.ActorId);
        Assert.Equal("RV", receipt.RunId);
        Assert.Equal(new[] { "W1" }, receipt.Artifact.CoveredWorkItemIds);
        Assert.Null(receipt.Artifact.CandidateId);
        Assert.Empty(receipt.Artifact.WorkVersions);
        Assert.Equal(before.Version + 1, state.Version);
        Assert.Equal(JsonSerializer.Serialize(before.WorkItems.Values), JsonSerializer.Serialize(state.WorkItems.Values));
        Assert.Equal(AgentRunStatus.Active, state.Runs[f.Binding.RunId].Status);
        await f.ExecuteAsync(new CompleteRunCommand(new("operator"), null, "done", f.Binding.RunId, AgentRunStatus.Completed, "session"));
        var retry = await f.SubmitAsync(request with { ExpectedContentSha256 = receipt.Artifact.ContentSha256 });
        Assert.True(retry.Replayed);
        Assert.Equal(ArtifactSubmissionReceiptEnvelope.Serialize(receipt), ArtifactSubmissionReceiptEnvelope.Serialize(retry.Receipt!));
        var newRequest = await f.SubmitAsync(request with { RequestId = "new" });
        Assert.Equal("kernel_refused", newRequest.Error?.Code);
    }

    [Fact]
    public async Task ConcurrentRetriesCommitOneArtifactAndChangedBodyConflicts()
    {
        using var f = new ArtifactSubmissionFixture(); await f.OpenAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => f.SubmitAsync()));
        Assert.All(results, r => Assert.Null(r.Error));
        Assert.Single(results.Where(r => !r.Replayed));
        Assert.Single(results.Select(r => r.Receipt!.TransactionId).Distinct());
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var conflict = await f.SubmitAsync(ArtifactSubmissionFixture.Request() with { Content = ArtifactSubmissionFixture.Request().Content + "changed" });
        Assert.Equal("idempotency_conflict", conflict.Error?.Code);
        Assert.Equal("committed", conflict.Error?.CommitState);
        Assert.Null(conflict.Receipt);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
    }

    [Fact]
    public async Task LegacyRevisionsRequireCurrentSameScopePredecessorAndRetainHistory()
    {
        using var f = new ArtifactSubmissionFixture(); await f.OpenAsync();
        var first = (await f.SubmitAsync()).Receipt!;
        var revision = ArtifactSubmissionFixture.Request("revision") with { Content = ArtifactSubmissionFixture.Request().Content + "revision" };
        var missing = await f.SubmitAsync(revision);
        Assert.Equal("kernel_refused", missing.Error?.Code);
        Assert.Contains(first.Artifact.ArtifactId, missing.Error!.Message);
        Assert.Equal("kernel_refused", (await f.SubmitAsync(revision with { SupersedesArtifactId = "missing" })).Error?.Code);
        var accepted = await f.SubmitAsync(revision with { SupersedesArtifactId = first.Artifact.ArtifactId });
        Assert.Null(accepted.Error);
        Assert.Equal(first.Artifact.ArtifactId, accepted.Receipt!.Artifact.SupersedesArtifactId);
        Assert.Equal("kernel_refused", (await f.SubmitAsync(revision with { RequestId = "fork", SupersedesArtifactId = first.Artifact.ArtifactId })).Error?.Code);
        Assert.Equal(first.Artifact.ContentSha256, ArtifactSubmissionIdentity.ContentHash((await f.StateAsync()).Artifacts[new(first.Artifact.ArtifactId)].Content));
    }

    [Theory]
    [InlineData("blank")]
    [InlineData("unicode")]
    [InlineData("bytes")]
    [InlineData("title")]
    [InlineData("hash")]
    [InlineData("mismatch")]
    [InlineData("unsupported")]
    public async Task BadContentAndUnsupportedKindsNeverRegister(string problem)
    {
        using var f = new ArtifactSubmissionFixture(); await f.OpenAsync();
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var request = ArtifactSubmissionFixture.Request();
        request = problem switch
        {
            "blank" => request with { Content = " " },
            "unicode" => request with { Content = "\ud800" },
            "bytes" => request with { Content = new string('א', 70000) },
            "title" => request with { Title = new string('a', 513) },
            "hash" => request with { ExpectedContentSha256 = "bad" },
            "mismatch" => request with { ExpectedContentSha256 = new string('0', 64) },
            _ => request with { Kind = GovernedArtifactKind.UserRequest }
        };
        var result = await f.SubmitAsync(request);
        Assert.Equal(problem == "unsupported" ? "unsupported_artifact_kind" : problem == "mismatch" ? "content_identity_mismatch" : "invalid_request", result.Error?.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("run")]
    [InlineData("cause")]
    [InlineData("grant")]
    [InlineData("correlation")]
    [InlineData("role")]
    [InlineData("document")]
    public async Task TrustedBindingAndDomainRulesStillRejectBeforeMutation(string problem)
    {
        using var f = new ArtifactSubmissionFixture(); await f.OpenAsync();
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var binding = problem switch
        {
            "actor" => f.Binding with { ActorId = new("operator") },
            "run" => f.Binding with { RunId = new("missing"), CorrelationId = "missing" },
            "cause" => f.Binding with { CausationId = new("missing") },
            "grant" => f.Binding with { AllowSubmitArtifact = false },
            "correlation" => f.Binding with { CorrelationId = "other" },
            _ => f.Binding
        };
        var request = ArtifactSubmissionFixture.Request();
        if (problem == "role") request = request with { Kind = GovernedArtifactKind.CodeReviewOutput };
        if (problem == "document") request = request with { Content = "An unsupported pass" };
        var result = await f.SubmitAsync(request, binding);
        Assert.NotNull(result.Error);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
    }

    [Fact]
    public async Task CapabilityRevocationBlocksReceiptAccessWithoutChangingHistoricalAssignments()
    {
        using var f = new ArtifactSubmissionFixture(); await f.OpenAsync();
        await f.SubmitAsync();
        await f.ExecuteAsync(new AssignRoleCommand(new("operator"), null, "revoke", f.Actor, RoleKind.Verifier, [Capability.BuildContext]));
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        Assert.Equal("kernel_refused", (await f.SubmitAsync()).Error?.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
    }

    [Fact]
    public async Task CoexistsWithFrozenOperationsUsingSameKeyAndSeparateMeasurements()
    {
        using var f = new ArtifactSubmissionFixture(); await f.OpenAsync();
        await f.ExecuteAsync(new AssignRoleCommand(new("operator"), null, "grants", f.Actor, RoleKind.Verifier,
            [Capability.BuildContext, Capability.RecordArtifact, Capability.AddClaim, Capability.RecordAlternative]));
        var request = ArtifactSubmissionFixture.Request();
        var artifact = await f.SubmitAsync(); Assert.Null(artifact.Error);
        var findings = await f.Service().RecordAsync(new(f.TaskId, f.Actor, f.Binding.RunId, "RV", AllowRecordFindings: true),
            new(1, request.RequestId, [new("f", "Observation")], []), default);
        var alternatives = await f.Service().RecordAlternativesAsync(new(f.TaskId, f.Actor, f.Binding.RunId, "RV", AllowRecordAlternatives: true),
            new(1, request.RequestId, [new("a", "Approach", "Rejected")]), default);
        Assert.Null(findings.Error); Assert.Null(alternatives.Error);
        Assert.True((await f.SubmitAsync()).Replayed);
        var state = await f.StateAsync(); var history = await f.HistoryAsync();
        var measured = await f.Service().ReadArtifactSubmissionMeasurementAsync(state, history, null, default);
        Assert.Equal(1, measured.CommittedTransactions); Assert.Equal(1, measured.GranularEvents);
        Assert.Equal(2, measured.ObservedApplicationAttempts); Assert.Null(measured.ObservedToolAttempts);
        Assert.Equal(1, (await f.Service().ReadFindingsMeasurementAsync(state, history, null, default)).CommittedTransactions);
        Assert.Equal(1, (await f.Service().ReadAlternativesMeasurementAsync(state, history, null, default)).CommittedTransactions);
        Assert.Equal((await f.StateAsync()).Runs.Count, measured.Runs.Count);
        Assert.Single(measured.Runs.Where(r => r.RunId == "RV"));
    }
}
