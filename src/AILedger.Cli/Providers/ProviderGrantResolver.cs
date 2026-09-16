using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Cli.Providers;

internal static class ProviderGrantResolver
{
    public static ProviderGrants Resolve(
        GovernedTaskState state,
        ActorId actorId,
        WorkItemId? workItemId,
        CommandLine input,
        string ledgerRoot,
        AssuranceBinding? assurance = null)
    {
        var requestedWorkingDirectory = input.Optional("working-directory");
        var requestedAdditionalDirectories = input.Many("add-dir");
        if (workItemId is null)
        {
            if (!state.Roles.TryGetValue(actorId, out var assignment) || assignment.Role != RoleKind.Operator)
            {
                throw new GovernanceException("Only an operator may launch a provider without governed work scope.");
            }

            var unscopedGrants = new ProviderGrants(
                ResolveExistingDirectory(requestedWorkingDirectory ?? Environment.CurrentDirectory),
                requestedAdditionalDirectories.Select(ResolveExistingDirectory).ToArray());
            EnsureLedgerIsOutsideProviderDirectories(
                ledgerRoot, unscopedGrants.AdditionalDirectories.Prepend(unscopedGrants.WorkingDirectory));
            return unscopedGrants;
        }

        // Preserve the legacy anchor-existence refusal before looking up the dispatcher's role.
        if (!state.WorkItems.ContainsKey(workItemId.Value))
        {
            throw new GovernanceException($"Unknown work item '{workItemId.Value}'.");
        }

        if (!state.Roles.TryGetValue(actorId, out var actorAssignment))
        {
            throw new GovernanceException($"Actor '{actorId}' has no assigned role.");
        }

        var members = WorkCoverage.Effective(workItemId, assurance);
        var scopesByMember = new Dictionary<WorkItemId, string[]>();
        foreach (var member in members)
        {
            if (!state.WorkItems.TryGetValue(member, out var workItem))
            {
                throw new GovernanceException($"Unknown work item '{member}'.");
            }

            if (workItem.Owner is { } owner && owner != actorId && actorAssignment.Role != RoleKind.Operator)
            {
                throw new GovernanceException($"Actor '{actorId}' does not own work item '{member}'.");
            }

            if (workItem.ResourceScope.Any(scope => !Path.IsPathFullyQualified(scope)))
            {
                throw new GovernanceException(
                    $"Work item '{member}' contains a legacy relative directory scope and must be recreated.");
            }

            var memberScopes = workItem.ResourceScope.Select(ResolveExistingScope).Distinct(PathComparer).ToArray();
            if (memberScopes.Length == 0)
            {
                throw new GovernanceException($"Work item '{member}' has no directory scope for a provider run.");
            }

            scopesByMember.Add(member, memberScopes);
        }

        var scopes = scopesByMember.Values.SelectMany(value => value).Distinct(PathComparer).ToArray();
        EnsureLedgerIsOutsideProviderDirectories(ledgerRoot, scopes);
        var anchorScope = scopesByMember[workItemId.Value][0];
        var workingDirectory = ResolveExistingDirectory(
            requestedWorkingDirectory ?? ProviderDirectoryForScope(anchorScope));
        var additionalDirectories = requestedAdditionalDirectories.Select(ResolveExistingDirectory).ToArray();
        foreach (var grant in additionalDirectories.Prepend(workingDirectory))
        {
            if (scopes.Any(scope => IsContainedPath(ProviderDirectoryForScope(scope), grant)))
            {
                continue;
            }

            // A grant that is not inside any scope is only acceptable as an ancestor, and only up to
            // the repository that holds the scope it is an ancestor of. Each scope it climbs is asked
            // for its own ceiling, because an item may hold directories in more than one repository.
            var ceilings = scopes
                .Where(scope => IsContainedPath(grant, ProviderDirectoryForScope(scope)))
                .Select(scope => ProviderGrantCeiling(ProviderDirectoryForScope(scope)))
                .Distinct(PathComparer)
                .ToArray();
            if (ceilings.Any(ceiling => IsContainedPath(ceiling, grant)))
            {
                continue;
            }

            // Two refusals, because they are two different mistakes. A directory that is neither
            // ancestor nor descendant of any scope is somebody else's work and there is nothing to
            // name. A directory that is an ancestor is the right shape and merely too high, so the
            // refusal names the highest one that would be accepted — the same disclosure
            // StageTransitionPolicy.EnsureAllowed makes when it names the legal targets.
            throw new GovernanceException(ceilings.Length == 0
                ? $"Provider directory '{grant}' is outside work item '{workItemId.Value}' scope."
                : $"Provider directory '{grant}' is above the highest directory work item " +
                  $"'{workItemId.Value}' may be granted: " +
                  $"{string.Join(", ", ceilings.Select(ceiling => $"'{ceiling}'"))}.");
        }

        // R2 (complete-grant-union): include every declared scope, without collapsing sibling
        // directories to an ancestor. File scopes require their parent because providers grant directories.
        var automaticDirectories = scopes.Select(ProviderDirectoryForScope).Distinct(PathComparer).ToArray();
        if (assurance is not null && members.Count > 1)
        {
            foreach (var directory in automaticDirectories)
            {
                var isRepositoryRoot = Directory.Exists(Path.Combine(directory, ".git")) ||
                    File.Exists(Path.Combine(directory, ".git")) ||
                    Directory.Exists(Path.Combine(directory, ".ailedger"));
                var explicitlyRequested = (requestedWorkingDirectory is not null &&
                    PathComparer.Equals(workingDirectory, directory)) ||
                    additionalDirectories.Contains(directory, PathComparer);
                if (isRepositoryRoot && !explicitlyRequested)
                {
                    throw new GovernanceException(
                        $"Assurance coverage: scope requires implicit repository-root grant '{directory}'; " +
                        "use narrower scopes or an explicit authorized '--working-directory' or '--add-dir'.");
                }
            }
        }

        // Ledger writes are a separate governed channel, not a product scope.
        return new ProviderGrants(workingDirectory,
            automaticDirectories.Concat(additionalDirectories)
                .Append(ResolveExistingDirectory(ledgerRoot))
                .Where(directory => !PathComparer.Equals(directory, workingDirectory))
                .Distinct(PathComparer).OrderBy(directory => directory, PathComparer).ToArray());
    }

    public static string ResolveExistingScope(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            return ResolveExistingDirectory(fullPath);
        }

        if (!File.Exists(fullPath))
        {
            // Keep the existing refusal contract for a path that names neither a file nor a directory.
            return ResolveExistingDirectory(fullPath);
        }

        var parent = ResolveExistingDirectory(Path.GetDirectoryName(fullPath)!);
        var file = new FileInfo(Path.Combine(parent, Path.GetFileName(fullPath)));
        if (file.LinkTarget is not null)
        {
            return file.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                ?? throw new GovernanceException($"Could not resolve provider scope link '{file.FullName}'.");
        }

        return file.FullName;
    }

    // The highest directory a provider grant may climb to from one scope. The accepted decision that
    // widened the containment clause is itself bounded: the working directory may be an ancestor of
    // the scope "so a worker owning a disjoint set runs where the solution builds". The repository
    // holding the scope is where the solution builds. Without a ceiling the clause accepted every
    // ancestor up to the filesystem root — and the grant is the agent's write boundary under
    // PermissionProfile.WorkspaceGoverned, not a label.
    //
    // '.git' and '.ailedger' are the markers because they are the two this kernel already walks for:
    // CodexAgentAdapter.EnsurePreconditions walks up for '.git' before it will launch at all, and
    // DiscoverLedgerHome walks up for '.ailedger' to find the ledger in front of the caller. Either
    // one marks a root somebody deliberately made.
    //
    // A scope in no repository has no "where the solution builds", so its ceiling is the scope
    // directory itself and the ancestor clause simply does not apply to it. That degrades to the
    // behaviour that held before the widening, which is the safe direction to fall.
    private static string ProviderGrantCeiling(string scopeDirectory)
    {
        for (var directory = new DirectoryInfo(scopeDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".ailedger")))
            {
                return ResolveExistingDirectory(directory.FullName);
            }
        }

        return scopeDirectory;
    }

    public static string ProviderDirectoryForScope(string scope) =>
        Directory.Exists(scope) ? scope : ResolveExistingDirectory(Path.GetDirectoryName(scope)!);

    private static string ResolveExistingDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new GovernanceException($"Provider directory '{fullPath}' does not exist.");
        }

        var root = Path.GetPathRoot(fullPath)!;
        var current = root;
        foreach (var segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var directory = new DirectoryInfo(current);
            if (directory.LinkTarget is not null)
            {
                current = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new GovernanceException($"Could not resolve provider directory link '{directory.FullName}'.");
            }
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }

    private static bool IsContainedPath(string scope, string candidate)
    {
        if (string.Equals(scope, candidate, PathComparison))
        {
            return true;
        }

        var prefix = Path.TrimEndingDirectorySeparator(scope) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, PathComparison);
    }

    public static void EnsureLedgerIsOutsideProviderDirectories(
        string ledgerRoot,
        IEnumerable<string> providerDirectories)
    {
        var canonicalLedgerRoot = ResolveExistingDirectory(ledgerRoot);
        var canonicalProviderDirectories = providerDirectories
            .Select(ResolveExistingScope)
            .Distinct(PathComparer)
            .ToArray();
        // A provider directory that *contains* the Ledger root is the self-hosting case: a governed
        // agent has to be able to record claims, evidence and escalations while it works, and under
        // a workspace sandbox it can only write inside its own workspace. The protection against a
        // tampered log is not this check — it is replay: FileGovernedTaskService re-validates event
        // sequence and causation, and TaskTransitionValidator re-checks payload provenance, so a
        // forged history fails closed. Truncation is the residual risk, detected separately by
        // comparing the replayed version against the materialised state.
        var containedDirectory = canonicalProviderDirectories.FirstOrDefault(
            directory => IsContainedPath(canonicalLedgerRoot, directory));
        if (containedDirectory is not null)
        {
            throw new GovernanceException(
                $"Provider directory '{containedDirectory}' is inside the authoritative Ledger root '{canonicalLedgerRoot}'.");
        }
    }

    public static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

internal sealed record ProviderGrants(
    string WorkingDirectory,
    IReadOnlyList<string> AdditionalDirectories);
