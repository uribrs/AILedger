using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Artifacts.InternalReconRules;

namespace AILedger.Core.Artifacts;

// Version 2 only. Structural provenance belongs in replay; live filesystem checks never do.
internal static class ReconSourceReviewRules
{
    internal static ReconSourceReview Validate(GovernedTaskState state, JsonElement review)
    {
        ExactProperties(review, "scope", "files", "reuseReason", "uninspected");
        var scope = Text(review, "scope");
        var reuse = review.GetProperty("reuseReason").GetString()
            ?? throw Invalid("reuseReason must be a string.");
        var files = Array(review.GetProperty("files"));
        var rows = files.Select(file => File(state, file)).ToArray();
        if (rows.Select(row => row.Path).Distinct(StringComparer.Ordinal).Count() != rows.Length)
            throw Invalid("source files must have unique paths.");
        if (rows.Length == 0 && string.IsNullOrWhiteSpace(reuse))
            throw Invalid("no source files were inspected; explain reuse or why source inspection is inapplicable in reuseReason.");
        var uninspected = Strings(review.GetProperty("uninspected"));
        return new(scope, rows, reuse, uninspected);
    }

    private static ReconSourceFile File(GovernedTaskState state, JsonElement file)
    {
        ExactProperties(file, "path", "sha256", "claimIds", "evidenceIds", "assessment");
        var path = Text(file, "path");
        if (!Path.IsPathFullyQualified(path)) throw Invalid("source paths must be absolute.");
        var hash = Text(file, "sha256");
        if (hash != "missing" && (hash.Length != 64 || hash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))))
            throw Invalid("source sha256 must be lowercase SHA-256 or missing for an absent file.");
        var claims = Strings(file.GetProperty("claimIds"));
        var evidence = Strings(file.GetProperty("evidenceIds"));
        if (claims.Length == 0 || evidence.Length == 0)
            throw Invalid("each inspected source file needs claimIds and directional evidenceIds; record findings first.");
        foreach (var id in evidence)
            if (!state.Evidence.ContainsKey(new(id))) throw Invalid($"unknown source evidence '{id}'.");
        foreach (var id in claims)
        {
            var claim = new ClaimId(id);
            if (!state.Claims.ContainsKey(claim)) throw Invalid($"unknown source claim '{id}'.");
            if (!evidence.Select(id => state.Evidence[new(id)]).Any(e => e.Supports.Contains(claim) || e.Refutes.Contains(claim)))
                throw Invalid($"source claim '{id}' has no directional link from the named evidence; record a supported correction, not an invented result.");
        }
        return new(path, hash, claims, evidence, Text(file, "assessment"));
    }

    private static JsonElement[] Array(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 256)
            throw Invalid("source review arrays must contain at most 256 entries.");
        return value.EnumerateArray().ToArray();
    }

    private static string[] Strings(JsonElement value)
    {
        var strings = Array(value).Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null).ToArray();
        if (strings.Any(string.IsNullOrWhiteSpace) || strings.Distinct(StringComparer.Ordinal).Count() != strings.Length)
            throw Invalid("source review lists require unique nonblank strings.");
        return strings.Select(s => s!).ToArray();
    }

    private static GovernanceException Invalid(string message) => new("InternalRecon sourceReview: " + message);
}
