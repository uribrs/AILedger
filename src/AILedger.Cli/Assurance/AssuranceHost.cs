using AILedger.Core.Assurance;
using AILedger.Core.Handoffs;
using AILedger.Providers.Assurance;
using AILedger.Providers.Process;
using AILedger.Storage.Episodes;
using AILedger.Storage;
using AILedger.Core.Contracts;

namespace AILedger.Cli.Assurance;

public static class AssuranceHost
{
    public static async Task<AssurancePolicy> PrepareAsync(string authorityPath, string storePath,
        string principal, string governedRole, IEnumerable<string> grantedRoots, string ledgerRoot, CancellationToken token)
    {
        var roots = grantedRoots.ToArray();
        EnsureProtectedPaths(authorityPath, storePath, roots.Append(ledgerRoot));
        EnsureStorePath(storePath);
        var policy = await new FilePolicy(authorityPath).ReadAsync(token).ConfigureAwait(false);
        AssuranceValidation.Policy(policy);
        var grant = AssuranceConfiguration.Principal(policy, principal);
        AssuranceConfiguration.EnsureEnabled(grant);
        EnsureGovernedRole(grant.Role, governedRole);
        EnsureGovernedContext(governedRole, AssuranceConfiguration.Areas(policy, principal));
        EnsureInputGrants(AssuranceConfiguration.InputPaths(policy, principal), roots, ledgerRoot);
        await AssuranceConfiguration.ValidateInputsAsync(policy, principal, token).ConfigureAwait(false);
        return policy;
    }

    public static void EnsureGovernedContext(string role, IReadOnlyList<AssuranceArea> areas)
    {
        if (role != "CodeReviewer") return;
        throw new AssuranceRefusal("incompatible_context",
            "The configured requirements-aware assurance profile is incompatible with blind CodeReviewer context " +
            $"(areas: {string.Join(", ", areas.Select(a => a.Id))}; checks: {string.Join(", ", areas.SelectMany(a => a.Criteria).Select(c => c.CheckId).Distinct())}). " +
            "Use an independently authorized requirements-aware context for this profile and keep the governed code review isolated. " +
            "Do not supply requirements, plans, coordinator contracts or verifier narratives through assurance inputs, or silently omit required assurance.");
    }

    private static void EnsureStorePath(string store)
    {
        // A missing directory can be created by execution; an existing file in its ancestry cannot.
        for (var entry = new DirectoryInfo(store); entry is not null; entry = entry.Parent)
            if (File.Exists(entry.FullName))
                throw new ArgumentException("Assurance store must name a directory with directory ancestors.");
    }

    public static async Task<AssuranceService> OpenAsync(string authorityPath, string storePath,
        AssuranceSession session, Func<CancellationToken, Task>? authorizeSession, CancellationToken token,
        FileGovernedTaskService? governedHost = null, bool providerSession = false, DateTimeOffset? expiresAt = null)
    {
        if (!Path.IsPathFullyQualified(storePath)) throw new ArgumentException("Assurance store must be absolute.");
        var source = new FilePolicy(authorityPath);
        var policy = await source.ReadAsync(token).ConfigureAwait(false);
        AssuranceValidation.Policy(policy);
        IGovernedAssuranceContext? context = null;
        if (policy.Governed is { } scope)
        {
            AssuranceValidation.Require(governedHost is not null, "missing_governed_host", "Supply the associated governed ledger host for this policy.");
            var state = await governedHost!.GetStateAsync(new(scope.TaskId), token).ConfigureAwait(false)
                ?? throw new AssuranceRefusal("wrong_governed_binding", "Governed task is missing.");
            AssuranceValidation.Require(scope.WorkItemIds.All(id => state.WorkItems.ContainsKey(new(id))),
                "wrong_governed_binding", "Policy names an unknown governed work member.");
            var roots = scope.WorkItemIds.SelectMany(id => state.WorkItems[new(id)].ResourceScope).ToArray();
            EnsureProtectedPaths(authorityPath, storePath, roots.Append(governedHost.WorkspaceRoot));
            EnsureInputGrants(AssuranceConfiguration.InputPaths(policy, session.Principal), roots, governedHost.WorkspaceRoot);
            AssuranceValidation.Require(policy.Areas.All(a => AssuranceConfiguration.Principal(policy, session.Principal).Areas.Contains(a.Id)),
                "unsupported", "The governed bridge requires the entire declared closure in the existing grant.");
            await governedHost.BindAssuranceCaseAsync(state.TaskId, policy, storePath, token).ConfigureAwait(false);
            context = governedHost.CreateAssuranceContext(state, session, providerSession,
                expiresAt ?? AssuranceConfiguration.Principal(policy, session.Principal).ExpiresAt, storePath);
        }
        return new(new FileEpisodeStore(storePath), source, policy, session, new SystemProcessRunner(), authorizeSession, context);
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
