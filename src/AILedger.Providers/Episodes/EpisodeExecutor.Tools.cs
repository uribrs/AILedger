using System.Text;
using AILedger.Core.Artifacts;
using AILedger.Core.Episodes;
using AILedger.Core.Handoffs;
using AILedger.Core.Inspection;

namespace AILedger.Providers.Episodes;

public sealed partial class EpisodeExecutor
{
    private async Task<string> HandleFrameAsync(string id, string invocationId, EpisodeAdmission admission,
        HandoffPackage package, string line, CancellationToken token)
    {
        var attemptId = Guid.NewGuid().ToString("N");
        var hash = ArtifactSubmissionIdentity.ContentHash(line);
        await store.AppendAsync(id, "tool_started", new { attemptId, invocationId, requestSha256 = hash }, token).ConfigureAwait(false);
        var operation = "invalid";
        try
        {
            var frame = HandoffJson.ParseDocument<EpisodeFrame>(line);
            operation = frame.Operation;
            if (frame.SchemaVersion != 1 || new object?[] { frame.Submission, frame.Retrieval, frame.Usage }.Count(p => p is not null) != 1)
                throw new ArgumentException("Expected one v1 protocol operation with its matching payload.");
            var authority = await RevalidateAsync(admission, package, token).ConfigureAwait(false);
            var records = await store.ReadAsync(id, token).ConfigureAwait(false);
            if (records.Count(r => r.Kind == "tool_started") > 128) throw new ArgumentException("Episode tool-attempt budget exhausted.");
            if (operation == "submit_result" && frame.Submission is { } submission)
            {
                var receipt = await SubmitAsync(id, invocationId, admission, submission, records, token).ConfigureAwait(false);
                await store.AppendAsync(id, "tool", new EpisodeToolAttempt(attemptId, invocationId, operation,
                    hash, "recorded", null, receipt.SubmissionId), token).ConfigureAwait(false);
                return "submitted";
            }
            if (operation == "retrieve_context" && frame.Retrieval is { } retrieval)
                return await RetrieveAsync(id, invocationId, attemptId, hash, package, authority, retrieval, records, token).ConfigureAwait(false);
            if (operation == "usage" && frame.Usage is { } usage)
            {
                if (records.Any(r => r.Kind == "usage" && r.Data.GetProperty("invocation_id").GetString() == invocationId) ||
                    usage.InputTokens < 0 || usage.OutputTokens < 0 || usage.CostUsd < 0 ||
                    usage.SessionId?.Length > 256 || usage.Model?.Length > 256)
                    throw new ArgumentException("Invalid or duplicate provider usage; do not double count an invocation.");
                await store.AppendAsync(id, "usage", new { invocationId, observation = "provider_reported", usage }, token).ConfigureAwait(false);
                if (usage.CostUsd > 0) throw new ArgumentException("Unexpected provider spend; zero-cost offline authorization violated.");
                await store.AppendAsync(id, "tool", new EpisodeToolAttempt(attemptId, invocationId, operation, hash, "observed", null, null), token).ConfigureAwait(false);
                return "usage";
            }
            throw new ArgumentException("Unsupported operation; business decisions, changed objectives and grants remain with the user.");
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            await store.AppendAsync(id, "tool", new EpisodeToolAttempt(attemptId, invocationId, operation,
                hash, e is IOException ? "outcome_unknown" : "refused", e.Message, null), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<EpisodeSubmissionReceipt> SubmitAsync(string id, string invocationId, EpisodeAdmission admission,
        EpisodeSubmission submission, IReadOnlyList<EpisodeRecord> records, CancellationToken token)
    {
        EpisodeExecutionValidation.Submission(submission, admission.Request.Handoff);
        var json = EpisodeExecutionValidation.Serialize(submission.Result);
        var hash = ArtifactSubmissionIdentity.ContentHash(json);
        var previous = records.Where(r => r.Kind == "submission").Select(EpisodeExecutionValidation.Data<EpisodeStoredSubmission>)
            .SingleOrDefault(s => s.Receipt.RequestId == submission.RequestId);
        if (previous is not null)
        {
            if (previous.Receipt.ContentSha256 != hash) throw new ArgumentException("Result key conflict: retry the exact original result.");
            return previous.Receipt;
        }
        if (records.Count(r => r.Kind == "submission") >= 32) throw new ArgumentException("Result submission budget exhausted.");
        var submissionId = "ES_" + Guid.NewGuid().ToString("N");
        var receipt = new EpisodeSubmissionReceipt(submissionId, submission.RequestId, admission.Principal, id,
            invocationId, hash, Encoding.UTF8.GetByteCount(json), $"ailedger-episode:{id}:{submissionId}", DateTimeOffset.UtcNow);
        await store.AppendAsync(id, "submission", new EpisodeStoredSubmission(receipt, submission.Result), token).ConfigureAwait(false);
        return receipt;
    }

    private async Task<string> RetrieveAsync(string id, string invocationId, string attemptId, string hash,
        HandoffPackage package, EpisodeAuthority authority, RetrievalQuery query, IReadOnlyList<EpisodeRecord> records, CancellationToken token)
    {
        if (!package.Inputs.Any(i => i.Reference.Retrieve.Kind == query.Kind && i.Reference.Retrieve.Id == query.Id &&
            query.ExpectedSha256 == i.Reference.Sha256 && query.ExpectedVersion == package.Snapshot.LedgerVersion &&
            query.Selection == package.Selection))
            throw new ArgumentException("Retrieval is outside the exact package input inventory.");
        var reads = records.Where(r => r.Kind == "read_reserved").ToArray();
        var priorReply = records.Where(r => r.Kind == "tool").Select(EpisodeExecutionValidation.Data<EpisodeToolAttempt>)
            .FirstOrDefault(t => t.Operation == "retrieve_context" && t.QuerySha256 == EpisodeExecutionValidation.Hash(query) && t.Retrieval?.Status == "ok");
        if (priorReply is not null)
        {
            await store.AppendAsync(id, "tool", new EpisodeToolAttempt(attemptId, invocationId, "retrieve_context", hash,
                "replayed", "Existing read reply; no new read or follow-up authorized.", null, priorReply.Retrieval, EpisodeExecutionValidation.Hash(query)), token).ConfigureAwait(false);
            return "replayed_read";
        }
        if (reads.Length >= package.Spec.Budget.MaximumAdditionalReads) throw new ArgumentException("Additional-read budget exhausted.");
        // A reserved read without a reply may safely be retried, charging a new read. Unlike
        // submissions it has no mutation to duplicate. A complete reply cannot schedule a loop.
        await store.AppendAsync(id, "read_reserved", new { invocationId, querySha256 = EpisodeExecutionValidation.Hash(query), query }, token).ConfigureAwait(false);
        var result = await inspector.RetrieveAsync(authority.Inspection, query, token).ConfigureAwait(false);
        await store.AppendAsync(id, "tool", new EpisodeToolAttempt(attemptId, invocationId, "retrieve_context", hash,
            result.Status, result.Diagnostic?.Reason, null, result, EpisodeExecutionValidation.Hash(query)), token).ConfigureAwait(false);
        if (result.Status != "ok") throw new ArgumentException("Retrieval unavailable or stale: " + result.Diagnostic?.Reason);
        return "retrieved";
    }
}
