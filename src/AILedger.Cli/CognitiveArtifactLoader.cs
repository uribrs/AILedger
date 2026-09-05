using System.Text.Json;
using System.Security.Cryptography;
using AILedger.Core.Contracts;

namespace AILedger.Cli;

internal sealed class CognitiveArtifactLoader
{
    public async Task<IReadOnlyList<ContextArtifact>> LoadAsync(
        string? configuredRoot,
        CancellationToken cancellationToken)
    {
        var root = ResolveRoot(configuredRoot);
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("The cognitive manifest was not found.", manifestPath);
        }

        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<CognitiveManifest>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The cognitive manifest is empty.");

        var artifacts = new List<ContextArtifact>();

        foreach (var entry in manifest.Files.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = ResolveManifestPath(root, entry.Path);
            await VerifyHashAsync(path, entry.Sha256, cancellationToken).ConfigureAwait(false);
            var isRules = entry.Path.Equals("RULES.md", StringComparison.OrdinalIgnoreCase);
            var skillName = entry.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
            artifacts.Add(new ContextArtifact(
                isRules ? ContextArtifactKind.Rules : ContextArtifactKind.Skill,
                isRules
                    ? "governing-rules"
                    : entry.Path.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase)
                        ? skillName
                        : $"{entry.Path}:{skillName}",
                await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
                []));
        }

        artifacts.Add(new ContextArtifact(
            ContextArtifactKind.Constraint,
            "operator-authority",
            "Only the operator may assign roles or change governed resource scope.",
            []));
        artifacts.Add(new ContextArtifact(
            ContextArtifactKind.StopCondition,
            "governance-stop",
            "Stop when proceeding would exceed assigned capabilities, alter scope, or bypass required independent verification.",
            []));
        return artifacts;
    }

    private static string ResolveRoot(string? configuredRoot)
    {
        var explicitRoot = configuredRoot ?? Environment.GetEnvironmentVariable("AILEDGER_COGNITIVE_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return Path.GetFullPath(explicitRoot);
        }

        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "cognitive");
                if (File.Exists(Path.Combine(candidate, "manifest.json")))
                {
                    return candidate;
                }
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the cognitive layer. Use '--cognitive-root PATH' or AILEDGER_COGNITIVE_ROOT.");
    }

    private static string ResolveManifestPath(string root, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!File.Exists(path))
        {
            path = Path.GetFullPath(Path.Combine(root, "skills", relativePath));
        }

        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Cognitive manifest path '{relativePath}' escapes its root.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Cognitive artifact '{relativePath}' was not found.", path);
        }

        return path;
    }

    private static async Task VerifyHashAsync(
        string path,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var actualHash = Convert.ToHexString(hash);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Cognitive artifact '{path}' does not match its manifest hash.");
        }
    }

    private sealed record CognitiveManifest(IReadOnlyList<CognitiveFile> Files);
    private sealed record CognitiveFile(string Path, string Sha256);
}
