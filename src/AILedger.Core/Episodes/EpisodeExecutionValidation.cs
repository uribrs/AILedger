using System.Text;
using System.Text.Json;
using AILedger.Core.Artifacts;
using AILedger.Core.Findings;
using AILedger.Core.Handoffs;

namespace AILedger.Core.Episodes;

public static class EpisodeExecutionValidation
{
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, HandoffJson.Options);
    public static string Hash<T>(T value) => ArtifactSubmissionIdentity.ContentHash(Serialize(value));
    public static T Data<T>(EpisodeRecord record) => record.Data.Deserialize<T>(HandoffJson.Options)
        ?? throw new InvalidDataException("Missing episode record data.");
    public static string ExecutionId(EpisodeAuthority authority, string requestId)
    {
        if (!FindingsValidation.IsRequestId(requestId)) throw new ArgumentException("Invalid execution request ID.");
        return Hash(new { authority.Principal, requestId });
    }
    public static string AuthorityHash(EpisodeAuthority authority) => Hash(authority with { Enabled = true });

    public static HandoffPackage Package(EpisodeStart start)
    {
        if (start is null || start.SchemaVersion != 1 || !FindingsValidation.IsRequestId(start.RequestId) || start.Handoff is null)
            throw new ArgumentException("Expected episode-start v1 with a stable request ID and prepared handoff.");
        var input = start.Handoff;
        if (input.SchemaVersion != 1 || input.PackageBytes > 524288 || input.PackageBytes < 1 ||
            input.PackageJson is null || Encoding.UTF8.GetByteCount(input.PackageJson) != input.PackageBytes ||
            ArtifactSubmissionIdentity.ContentHash(input.PackageJson) != input.PackageSha256)
            throw new ArgumentException("Invalid package content identity.");
        var package = HandoffJson.ParseDocument<HandoffPackage>(input.PackageJson);
        if (package.SchemaVersion != 1 || package.Snapshot is null || package.Inputs is null ||
            package.Inputs.Any(i => i is null || i.Selection is null || i.Reference is null) || package.Spec is null)
            throw new ArgumentException("Incomplete package.");
        HandoffValidation.Validate(new(1, package.LedgerIdentity, package.Snapshot.LedgerVersion,
            package.Selection, package.Spec, package.Inputs.Select(i => i.Selection).ToArray(), package.Sources, package.Preservation));
        if (package.SpecSha256 != Hash(package.Spec) || input.PackageBytes > package.Spec.Budget.MaximumPackageBytes)
            throw new ArgumentException("Spec identity or package budget mismatch.");
        foreach (var item in package.Inputs)
        {
            var query = item.Reference.Retrieve;
            if (item.Key != item.Selection.Kind + ":" + item.Selection.Id ||
                item.Reference.Kind != item.Selection.Kind || item.Reference.Id != item.Selection.Id ||
                item.Reference.Sha256 != item.Selection.Sha256 || query is null ||
                query.Kind != item.Reference.Kind || query.Id != item.Reference.Id ||
                query.ExpectedVersion != package.Snapshot.LedgerVersion || query.ExpectedSha256 != item.Reference.Sha256 ||
                query.Selection != package.Selection || item.Selection.Include != (item.RecordJson is not null) ||
                (item.RecordJson is { } json && (ArtifactSubmissionIdentity.ContentHash(json) != item.Reference.Sha256 ||
                    json.Length != item.Reference.JsonCharacters)))
                throw new ArgumentException("Package input identity mismatch.");
            CheckArtifactIdentity(item);
        }
        return package;
    }

    private static void CheckArtifactIdentity(PackagedInput item)
    {
        try
        {
            var artifact = item.RecordJson is null ? null : HandoffPreparer.ArtifactIdentity(item.Reference, item.RecordJson);
            if (item.Artifact != artifact) throw new ArgumentException("Task-8 receipt identity differs from the exact packaged record.");
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new ArgumentException("Invalid packaged task-8 receipt identity.", e); }
    }

    public static void Authority(EpisodeAuthority authority, EpisodeStart start, HandoffPackage package)
    {
        if (authority is null || authority.SchemaVersion != 1 || string.IsNullOrWhiteSpace(authority.Principal) ||
            authority.Principal.Length > 128 || !authority.Enabled || authority.ExpiresAt <= DateTimeOffset.UtcNow ||
            !authority.AllowSubmitResult || authority.Inspection is null || !authority.Inspection.AllowInspect ||
            authority.Grants is null || authority.Sources is null)
            throw new ArgumentException("Missing, expired or revoked trusted episode grants.");
        if (authority.PackageSha256 != start.Handoff.PackageSha256 ||
            authority.LedgerRoot != package.LedgerIdentity || !Path.IsPathFullyQualified(authority.LedgerRoot) ||
            authority.Inspection.TaskId.Value != package.Snapshot.TaskId ||
            authority.Inspection.ActorId.Value != package.Snapshot.ActorId ||
            authority.Inspection.RunId?.Value != package.Snapshot.RunId)
            throw new ArgumentException("Trusted principal/input binding differs from the package snapshot.");
        if (authority.MaximumInvocations is < 1 or > 8 || authority.MaximumAttempts is < 1 or > 3 ||
            authority.MaximumSeconds < 1 || authority.MaximumSeconds > package.Spec.Budget.MaximumElapsedMinutes * 60 ||
            package.Spec.Budget.MaximumCostUsd != 0)
            throw new ArgumentException("Invalid execution limits; this offline path authorizes no model spend.");
        if (!Path.IsPathFullyQualified(authority.Executable) || !ArtifactSubmissionIdentity.IsHash(authority.ExecutableSha256) ||
            package.Spec.RequestedGrants.Any(g => !authority.Grants.Contains(g)) ||
            !authority.Grants.Contains(new EpisodeGrant("read_context", package.Snapshot.TaskId)))
            throw new ArgumentException("Missing exact resource grant or pinned executable identity.");
        if (package.Sources.Any(s => !authority.Sources.Contains(new(s.Key, s.Version, s.Sha256))))
            throw new ArgumentException("Untrusted source version: every supplied source requires a host pin.");
    }

    public static void Submission(EpisodeSubmission submission, PreparedHandoff handoff)
    {
        if (submission is null || submission.SchemaVersion != 1 || !FindingsValidation.IsRequestId(submission.RequestId))
            throw new ArgumentException("Expected result submission v1 and a stable request ID.");
        EpisodeResultValidation.Validate(submission.Result, handoff);
        if (submission.Result.RecordedOutputs.Count != 0)
            throw new ArgumentException("Episode results cannot assert new or existing kernel artifact receipts; cite inputs in checks.");
    }
}
