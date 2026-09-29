using System.Text.Json;
using AILedger.Core.Artifacts;
using AILedger.Core.Episodes;
using AILedger.Core.Findings;
using AILedger.Core.Handoffs;

namespace AILedger.Core.Assurance;

public sealed class AssuranceRefusal(string code, string message, string recovery = "Correct the named input and inspect again.") : Exception(message)
{
    public string Code { get; } = code;
    public string Recovery { get; } = recovery;
}

public static class AssuranceValidation
{
    public static readonly string[] Tools = ["inspect_assurance", "read_assurance", "run_assurance_checks", "record_assurance", "accept_assurance"];
    public static string Hash<T>(T value) => EpisodeExecutionValidation.Hash(value);
    public static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, HandoffJson.Options);
    public static T Parse<T>(JsonElement value) where T : class
    {
        if (System.Text.Encoding.UTF8.GetByteCount(value.GetRawText()) > 128 * 1024)
            throw new AssuranceRefusal("invalid_request", "Assurance requests are limited to 128 KiB.");
        return HandoffJson.ParseDocument<T>(value.GetRawText());
    }
    public static void Require(bool condition, string code, string message)
    {
        if (!condition) throw new AssuranceRefusal(code, message);
    }
    public static void Text(string? value, string name, int maximum = 4096) =>
        Require(!string.IsNullOrWhiteSpace(value) && value.Length <= maximum,
            "invalid_request", $"{name} must contain 1–{maximum} characters.");
    public static void Items<T>(IReadOnlyList<T>? items, string name, int minimum = 0, int maximum = 32) =>
        Require(items is not null && items.Count >= minimum && items.Count <= maximum && items.All(i => i is not null),
            "invalid_request", $"{name} must contain {minimum}–{maximum} non-null entries.");
    public static void Unique(IReadOnlyList<string> items, string name)
    {
        Items(items, name);
        foreach (var item in items) Text(item, name, 256);
        Require(items.Distinct(StringComparer.Ordinal).Count() == items.Count, "invalid_request", $"{name} contains duplicates.");
    }
    public static void Request(int version, string? key = null)
    {
        Require(version == 1, "invalid_request", "Expected assurance schema_version=1.");
        if (key is not null) Require(FindingsValidation.IsRequestId(key), "invalid_request", "Use a stable 1–128 character request_id.");
    }
    public static string PolicyIdentity(AssurancePolicy policy) => Hash(policy with
    {
        Principals = policy.Principals.Select(p => p with { Enabled = true }).ToArray()
    });
    public static string ScopePolicyIdentity(AssurancePolicy policy, string areaId)
    {
        var areas = new SortedDictionary<string, AssuranceArea>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (areas.ContainsKey(id)) return;
            var area = policy.Areas.Single(a => a.Id == id); areas.Add(id, area);
            foreach (var dependency in area.DependsOnAreas) Visit(dependency);
        }
        Visit(areaId);
        var checks = areas.Values.SelectMany(a => a.Criteria.Select(c => c.CheckId)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(id => policy.Checks.Single(c => c.Id == id)).ToArray();
        return Hash(new { policy.CaseId, policy.CandidateRoot, policy.Implementer, Areas = areas.Values.ToArray(), Checks = checks });
    }
    public static void Policy(AssurancePolicy policy)
    {
        Request(policy.SchemaVersion); Text(policy.CaseId, "case_id", 128); Text(policy.Implementer, "implementer", 128);
        Require(Path.IsPathFullyQualified(policy.CandidateRoot), "invalid_policy", "Candidate root must be absolute.");
        Items(policy.Areas, "areas", 1, 8); Items(policy.Principals, "principals", 1); Items(policy.Checks, "checks", 1);
        Unique(policy.Areas.Select(a => a.Id).ToArray(), "area IDs");
        Unique(policy.Principals.Select(p => p.Id).ToArray(), "principal IDs");
        Unique(policy.Checks.Select(c => c.Id).ToArray(), "check IDs");
        foreach (var area in policy.Areas)
        {
            Items(area.CandidatePaths, "candidate_paths", 1); Items(area.RequirementPaths, "requirement_paths", 1);
            Unique(area.CandidatePaths.Concat(area.RequirementPaths).Concat(area.SourcePaths).ToArray(), "area paths");
            Unique(area.DependsOnAreas, "area dependencies"); Items(area.Criteria, "criteria", 1);
            Unique(area.Criteria.Select(c => c.Id).ToArray(), "criterion IDs");
            Require(area.DependsOnAreas.All(id => id != area.Id && policy.Areas.Any(a => a.Id == id)), "invalid_policy", "Unknown or self area dependency.");
            foreach (var criterion in area.Criteria)
            {
                Text(criterion.Description, "criterion description");
                Require(policy.Checks.Any(c => c.Id == criterion.CheckId), "invalid_policy", "Every criterion requires a configured host check.");
            }
        }
        foreach (var principal in policy.Principals)
        {
            Require(principal.Role is "review" or "verification" or "synthesis" or "acceptance", "invalid_policy", "Unknown assurance role.");
            Unique(principal.Areas, "principal areas");
            Require(principal.Areas.Count > 0 && principal.Areas.All(id => policy.Areas.Any(a => a.Id == id)), "invalid_policy", "Principal has unknown or empty area scope.");
        }
        foreach (var check in policy.Checks)
        {
            Require(Path.IsPathFullyQualified(check.Executable) && ArtifactSubmissionIdentity.IsHash(check.ExecutableSha256), "invalid_policy", "Checks require an absolute executable and SHA-256.");
            Items(check.Arguments, "check arguments", 0, 32);
            foreach (var argument in check.Arguments) Require(argument is not null && argument.Length <= 4096, "invalid_policy", "Invalid check argument.");
            Require(check.TimeoutSeconds is >= 1 and <= 300, "invalid_policy", "Checks require a 1–300 second timeout.");
        }
    }
}
