using AILedger.Core.Assurance;
using AILedger.Core.Handoffs;
using AILedger.Providers.Assurance;
using AILedger.Providers.Process;
using AILedger.Storage.Episodes;

namespace AILedger.Cli.Assurance;

public static class AssuranceHost
{
    public static async Task<AssuranceService> OpenAsync(string authorityPath, string storePath,
        AssuranceSession session, Func<CancellationToken, Task>? authorizeSession, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(storePath)) throw new ArgumentException("Assurance store must be absolute.");
        var source = new FilePolicy(authorityPath);
        var policy = await source.ReadAsync(token).ConfigureAwait(false);
        return new(new FileEpisodeStore(storePath), source, policy, session, new SystemProcessRunner(), authorizeSession);
    }
    public static void EnsureGovernedRole(string assuranceRole, string role)
    {
        var permitted = assuranceRole switch
        {
            "review" => role == "CodeReviewer",
            "verification" => role == "Verifier",
            "acceptance" => role == "Operator",
            "synthesis" => role is "Operator" or "PlanningLead" or "ImplementationLead",
            _ => false
        };
        AssuranceValidation.Require(permitted, "authorization_denied", "The live governed role does not authorize this assurance role; do not infer authority from the retrieved brief.");
    }
    public static void EnsureProtectedPaths(string authority, string store, IEnumerable<string> writableRoots)
    {
        foreach (var path in new[] { authority, store })
        {
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Assurance authority/store paths must be absolute.");
            for (FileSystemInfo? entry = new FileInfo(path); entry is not null;
                entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
                if (entry.LinkTarget is not null && entry.FullName is not ("/tmp" or "/var"))
                    throw new ArgumentException("Resolve symlinked assurance authority/store paths before launch.");
            foreach (var root in writableRoots)
            {
                var relative = Path.GetRelativePath(CanonicalPath(root), CanonicalPath(path));
                if (relative == "." || !Path.IsPathFullyQualified(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new AssuranceRefusal("authorization_denied", "Assurance policy and canonical store must be outside the provider's granted write roots.");
            }
        }
    }
    public static void EnsureInputGrants(IEnumerable<string> inputs, IEnumerable<string> readRoots, string ledgerRoot)
    {
        var roots = readRoots.Select(CanonicalPath).ToArray();
        var ledger = CanonicalPath(ledgerRoot);
        foreach (var path in inputs.Select(CanonicalPath))
            AssuranceValidation.Require(!Contains(ledger, path) && roots.Any(root => Contains(root, path)),
                "authorization_denied", "Assurance inputs must remain inside existing provider resource grants and outside the authoritative ledger.");
    }
    private static bool Contains(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "." || !Path.IsPathFullyQualified(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
    private static string CanonicalPath(string path)
    {
        var absolute = Path.GetFullPath(path);
        for (var iteration = 0; iteration < 32; iteration++)
        {
            FileSystemInfo? link = new DirectoryInfo(absolute);
            while (link is not null && link.LinkTarget is null) link = ((DirectoryInfo)link).Parent;
            if (link is null) return absolute;
            var target = link.ResolveLinkTarget(returnFinalTarget: true)
                ?? throw new ArgumentException("Unresolved authority/write-root alias.");
            absolute = Path.GetFullPath(Path.Combine(target.FullName, Path.GetRelativePath(link.FullName, absolute)));
        }
        throw new ArgumentException("Excessive authority/write-root aliases.");
    }
    private sealed class FilePolicy(string path) : IAssurancePolicySource
    {
        public async Task<AssurancePolicy> ReadAsync(CancellationToken token)
        {
            if (!Path.IsPathFullyQualified(path) || new FileInfo(path).LinkTarget is not null || new FileInfo(path).Length > 65536)
                throw new ArgumentException("Operator policy must be an absolute non-symlink file under 64 KiB, protected from provider writes.");
            return HandoffJson.ParseDocument<AssurancePolicy>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
        }
    }
}
