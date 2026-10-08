using System.Text.Json;
using AILedger.Core.Domain;
using AILedger.Storage;

namespace AILedger.Cli.Cognitive;

internal sealed class ReconSourceAccess(IReadOnlyList<string> directories, string ledgerRoot)
{
    internal IReadOnlyList<string> Resolve(IReadOnlyList<string> paths)
    {
        var roots = directories.Select(path => ReconSourceFiles.Canonicalize(path)).ToArray();
        var hidden = ReconSourceFiles.Canonicalize(ledgerRoot);
        return paths.Select(path =>
        {
            if (!Path.IsPathFullyQualified(path)) throw new GovernanceException("Recon source paths must be absolute.");
            var canonical = ReconSourceFiles.Canonicalize(path);
            var root = roots.OrderByDescending(root => root.Length).FirstOrDefault(root => Within(canonical, root));
            if (root is null || Within(canonical, hidden) ||
                Path.GetRelativePath(root, canonical).Split(Path.DirectorySeparatorChar).Any(p => p is ".git" or ".ailedger" or ".codex" or ".claude" or ".agents"))
                throw new GovernanceException("Recon source path is outside the supplied source directories or names protected state. For missing source scope, the operator must supply the required --add-dir on a fresh launch; do not omit needed sources to pass.");
            return canonical;
        }).Distinct(StringComparer.Ordinal).ToArray();
    }

    internal void EnsureAllowed(string content)
    {
        try
        {
            using var json = JsonDocument.Parse(content);
            if (json.RootElement.TryGetProperty("sourceReview", out var review) &&
                review.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
                _ = Resolve(files.EnumerateArray().Select(file => file.GetProperty("path").GetString()!).ToArray());
        }
        catch (Exception error) when (error is not GovernanceException && error is (JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException))
        {
            throw new GovernanceException("Invalid recon source review: " + error.Message);
        }
    }

    private static bool Within(string path, string root) => path == root || path.StartsWith(
        root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
