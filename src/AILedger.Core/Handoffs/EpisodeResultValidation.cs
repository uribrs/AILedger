using System.Text;
using System.Text.Json;
using AILedger.Core.Artifacts;

namespace AILedger.Core.Handoffs;

public static class EpisodeResultValidation
{
    // Checks an authored report's shape and input binding only. No execution/assurance judgment.
    public static void Validate(EpisodeResult result, PreparedHandoff input)
    {
        if (input.SchemaVersion != 1 || ArtifactSubmissionIdentity.ContentHash(input.PackageJson) != input.PackageSha256 ||
            Encoding.UTF8.GetByteCount(input.PackageJson) != input.PackageBytes || input.PackageBytes > 524288)
            throw new ArgumentException("Invalid input package identity.");
        var package = JsonSerializer.Deserialize<HandoffPackage>(input.PackageJson, HandoffJson.Options)
            ?? throw new ArgumentException("Missing package.");
        if (result is null || result.SchemaVersion != 1 || result.EpisodeId != package.Spec.Id ||
            result.PackageSha256 != input.PackageSha256 ||
            result.Status is not ("reported_complete" or "partial" or "blocked" or "unknown"))
            throw new ArgumentException("Expected bounded v1 authored result tied to the supplied package.");
        HandoffValidation.Text(result.Summary);
        if (result.RecordedOutputs is null || result.RecordedOutputs.Count > 32 ||
            result.RecordedOutputs.Any(o => o is null || !ArtifactSubmissionIdentity.IsHash(o.ContentSha256)) ||
            result.HostExecutionReceipt is not null)
            throw new ArgumentException("Task 11 cannot produce host execution receipts; output links are existing task-8 metadata only.");
        if (result.Checks is null || result.Checks.Count != package.Spec.AcceptanceChecks.Count ||
            !result.Checks.Select(c => c?.Check).Order().SequenceEqual(package.Spec.AcceptanceChecks.Order()))
            throw new ArgumentException("Report every acceptance check exactly once, including unknown/unperformed checks.");
        foreach (var check in result.Checks)
        {
            if (check.Status is not ("observed_pass" or "observed_fail" or "not_checked" or "unknown"))
                throw new ArgumentException("Invalid check status.");
            HandoffValidation.Text(check.Evidence); HandoffValidation.Text(check.Limitation);
        }
        Strings(result.Uncertainty); Strings(result.StopReasons);
        if (result.AdditionalReads is { } reads)
        {
            if (reads.Count > package.Spec.Budget.MaximumAdditionalReads) throw new ArgumentException("Additional-read budget exceeded.");
            foreach (var read in reads)
            {
                if (read is null || !ArtifactSubmissionIdentity.IsHash(read.Sha256) || read.Bytes < 0)
                    throw new ArgumentException("Invalid additional-read observation.");
                HandoffValidation.Text(read.InputKey); HandoffValidation.Text(read.Reason);
            }
        }
        if (JsonSerializer.SerializeToUtf8Bytes(result, HandoffJson.Options).Length > 128 * 1024)
            throw new ArgumentException("Authored result exceeds 128 KiB.");
    }
    private static void Strings(IReadOnlyList<string> values)
    {
        if (values is null || values.Count > 32) throw new ArgumentException("Expected at most 32 report entries.");
        foreach (var value in values) HandoffValidation.Text(value);
    }
}
