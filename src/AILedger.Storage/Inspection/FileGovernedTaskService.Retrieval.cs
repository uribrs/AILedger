using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Findings;
using AILedger.Core.Inspection;
using AILedger.Storage.Inspection;

namespace AILedger.Storage;

public sealed partial class FileGovernedTaskService
{
    public async Task<InspectionResult> InspectAsync(InspectionBinding binding, InspectionQuery query, CancellationToken cancellationToken)
    {
        InspectionSnapshot? snapshot = null;
        try
        {
            if (query.SchemaVersion != 1 || query.Offset < 0 || query.Limit is < 1 or > 32)
                throw new FindingsRequestException("invalid_request", "Expected v1, nonnegative offset and limit 1–32.");
            var data = await ReadInspectionAsync(binding, cancellationToken).ConfigureAwait(false);
            snapshot = Snapshot(data.State, binding);
            if (query.Offset > 0 && query.ExpectedVersion is null)
                throw new FindingsRequestException("invalid_request", "Pagination requires expected_version from the first page.");
            CheckVersion(query.ExpectedVersion, data.State);
            var records = InspectionRecords(data, binding, query.Selection).ToArray();
            if (query.Offset > records.Length) throw new FindingsRequestException("invalid_request", "Offset exceeds selected record count.");
            var page = new List<ContextReference>();
            var pageBytes = 0;
            foreach (var record in records.Skip(query.Offset).Take(query.Limit))
            {
                var reference = Reference(record, query.Selection, data.State.Version);
                var size = JsonSerializer.SerializeToUtf8Bytes(reference).Length;
                if (pageBytes + size > 64 * 1024)
                {
                    if (page.Count == 0) throw new FindingsRequestException("reference_too_large", "This reference exceeds the 64 KiB index budget; retrieve it through the authorized CLI path.");
                    break;
                }
                page.Add(reference);
                pageBytes += size;
            }
            var next = query.Offset + page.Count;
            return new("ok", snapshot, page, records.Length, next < records.Length ? next : null,
                "Only context-selected records and this binding's authorized receipts are indexed. Use selection=task for unrelated current records; role exclusions still apply. Historical/superseded records outside selection and run transcripts remain CLI gaps. Cognitive files are absent when the host cannot inspect them. Page and summary omissions have explicit retrieval paths.",
                ReadinessActions, null);
        }
        catch (Exception e) when (InspectionError(e))
        { return new("error", snapshot, [], 0, null, "Selection unavailable; no completeness claim.", ReadinessActions, InspectionFailure(e, "inspect_task")); }
    }

    public async Task<RetrievalResult> RetrieveAsync(InspectionBinding binding, RetrievalQuery query, CancellationToken cancellationToken)
    {
        InspectionSnapshot? snapshot = null;
        try
        {
            if (query.SchemaVersion != 1 || query.ExpectedVersion < 1 || query.Offset < 0 || query.Length is < 1 or > 16384 ||
                string.IsNullOrWhiteSpace(query.Kind) || string.IsNullOrWhiteSpace(query.Id) || query.Id.Length > 256)
                throw new FindingsRequestException("invalid_request", "Expected v1, a versioned reference, nonnegative offset and length 1–16384.");
            var data = await ReadInspectionAsync(binding, cancellationToken).ConfigureAwait(false);
            snapshot = Snapshot(data.State, binding);
            CheckVersion(query.ExpectedVersion, data.State);
            var record = InspectionRecords(data, binding, query.Selection).SingleOrDefault(r => r.Kind == query.Kind && r.Id == query.Id)
                ?? throw new FindingsRequestException("not_visible", "Reference is absent from this authorized selection. Inspect selection=task; protected records remain unavailable.");
            var json = RecordJson(record);
            if (query.ExpectedSha256 is null || query.ExpectedSha256 != Digest(json))
                throw new FindingsRequestException("stale_content", "Record identity is missing or changed. Inspect again and use its retrieval reference.");
            if (query.Offset > json.Length) throw new FindingsRequestException("invalid_request", "Offset exceeds record length.");
            if (query.Offset == 0 && json.Length <= query.Length)
                return new("ok", snapshot, record.Kind, record.Id, JsonSerializer.SerializeToElement(record.Value, record.Value.GetType(), LedgerJson.CreateOptions()), null, null, json.Length, Digest(json), null);
            var length = Math.Min(query.Length, json.Length - query.Offset);
            if (query.Offset > 0 && query.Offset < json.Length && char.IsLowSurrogate(json[query.Offset]))
                throw new FindingsRequestException("invalid_request", "Offset splits a Unicode surrogate pair; use the returned next_offset.");
            if (length > 0 && query.Offset + length < json.Length && char.IsHighSurrogate(json[query.Offset + length - 1])) length--;
            if (length == 0 && query.Offset < json.Length) throw new FindingsRequestException("invalid_request", "Length must fit the next Unicode scalar (at least 2).");
            var next = query.Offset + length;
            return new("ok", snapshot, record.Kind, record.Id, null, json.Substring(query.Offset, length),
                next < json.Length ? next : null, json.Length, Digest(json), null);
        }
        catch (Exception e) when (InspectionError(e))
        { return new("error", snapshot, query.Kind, query.Id, null, null, null, 0, null, InspectionFailure(e, "retrieve_context", query.Id)); }
    }

    private static IEnumerable<SelectedRecord> InspectionRecords(InspectionSnapshotData data, InspectionBinding binding, string selection)
    {
        var state = data.State;
        var manifest = InspectionManifest(state, binding, selection);
        foreach (var artifact in manifest.Artifacts)
        {
            object value = artifact.Kind switch
            {
                ContextArtifactKind.Claim => state.Claims[new(artifact.Id)],
                ContextArtifactKind.Evidence => state.Evidence[new(artifact.Id)],
                ContextArtifactKind.Decision => state.Decisions[new(artifact.Id)],
                ContextArtifactKind.WorkItem => state.WorkItems[new(artifact.Id)],
                _ => artifact
            };
            yield return new(artifact.Kind.ToString(), artifact.Id, artifact.Content, value);
        }
        // These are the actor's own brief and run, not other actors' protected narrative or launch tokens.
        if (state.ContextBuilds.TryGetValue(binding.ActorId, out var brief))
            yield return new("ContextBuild", binding.ActorId.Value, "Recorded brief; inspection does not refresh it.", brief);
        if (binding.RunId is { } runId)
        {
            var run = state.Runs[runId];
            yield return new("Run", runId.Value, "Trusted current run and assurance binding.",
                new { run.Id, run.Status, run.SubjectRole, run.WorkItemId, run.Assurance });
        }
        foreach (var receipt in VisibleReceipts(data, binding))
            yield return new(receipt.Kind, receipt.Receipt.RequestId, "Recording receipt observed in committed history; not a durability re-flush or current judgment.", receipt.Receipt);
    }

    private static IEnumerable<(string Kind, IRecordingReceiptIdentity Receipt)> VisibleReceipts(InspectionSnapshotData data, InspectionBinding binding)
    {
        foreach (var r in data.Findings.Where(r => OwnReceipt(r, binding)))
            if (ReceiptAuthorized(data.State, binding, "record_findings", r)) yield return ("FindingsReceipt", r);
        foreach (var r in data.Alternatives.Where(r => OwnReceipt(r, binding)))
            if (ReceiptAuthorized(data.State, binding, "record_alternatives", r)) yield return ("AlternativesReceipt", r);
        foreach (var r in data.Artifacts.Where(r => OwnReceipt(r, binding)))
            if (ReceiptAuthorized(data.State, binding, "submit_artifact", r)) yield return ("ArtifactSubmissionReceipt", r);
        foreach (var r in data.Dispositions.Where(r => OwnReceipt(r, binding)))
            if (ReceiptAuthorized(data.State, binding, "record_claim_dispositions", r)) yield return ("ClaimDispositionsReceipt", r);
    }

    private static bool OwnReceipt(IRecordingReceiptIdentity receipt, InspectionBinding binding) =>
        receipt.ActorId == binding.ActorId.Value && receipt.RunId == binding.RunId?.Value;

    private static ContextReference Reference(SelectedRecord record, string selection, long version)
    {
        if (record.Id.Length > 256)
            throw new FindingsRequestException("unsupported_reference", "A selected legacy identifier exceeds the 256-character retrieval bound; use the authorized CLI read path.");
        var json = RecordJson(record);
        var length = Math.Min(240, record.Summary.Length);
        if (length > 0 && char.IsHighSurrogate(record.Summary[length - 1])) length--;
        return new(record.Kind, record.Id, record.Summary[..length], length < record.Summary.Length,
            json.Length, Digest(json), new(1, selection, record.Kind, record.Id, version, ExpectedSha256: Digest(json)));
    }

    private static string RecordJson(SelectedRecord record) => JsonSerializer.Serialize(record.Value, record.Value.GetType(), LedgerJson.CreateOptions());
    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private sealed record SelectedRecord(string Kind, string Id, string Summary, object Value);
}
