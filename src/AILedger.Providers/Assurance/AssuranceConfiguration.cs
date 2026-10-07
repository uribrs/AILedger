using AILedger.Core.Assurance;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Providers.Assurance;

// Read-only configuration selection shared by prospective hosts and active services.
// It grants no session and never opens or writes a canonical store.
public static class AssuranceConfiguration
{
    public static AssurancePrincipal Principal(AssurancePolicy policy, string principal) =>
        policy.Principals.SingleOrDefault(p => p.Id == principal)
        ?? throw new AssuranceRefusal("authorization_denied", "Actual host principal has no assurance grant.");

    public static void EnsureEnabled(AssurancePrincipal principal) =>
        Require(principal.Enabled && principal.ExpiresAt > DateTimeOffset.UtcNow,
            "authorization_denied", "The actual principal's grant is revoked or expired.");

    public static void EnsureAreaGranted(AssurancePolicy policy, string principal, string area)
    {
        Text(area, "area_id", 128);
        Require(Principal(policy, principal).Areas.Contains(area), "authorization_denied",
            $"Principal '{principal}' is not granted area '{area}', including any required dependency area. Supply an explicitly authorized complete scope.");
    }

    public static IReadOnlyList<AssuranceArea> Areas(AssurancePolicy policy, string principal)
    {
        var selected = new HashSet<string>(Principal(policy, principal).Areas, StringComparer.Ordinal);
        var pending = new Queue<string>(selected);
        while (pending.TryDequeue(out var id))
            foreach (var dependency in policy.Areas.Single(area => area.Id == id).DependsOnAreas)
                if (selected.Add(dependency)) pending.Enqueue(dependency);
        return policy.Areas.Where(area => selected.Contains(area.Id)).ToArray();
    }

    public static IReadOnlyList<string> InputPaths(AssurancePolicy policy, string principal) =>
        Areas(policy, principal)
            .SelectMany(area => area.CandidatePaths.Concat(area.RequirementPaths).Concat(area.SourcePaths))
            .Distinct(StringComparer.Ordinal).Select(path => Path.Combine(policy.CandidateRoot, path)).ToArray();

    // Read-only routing identity for the complete configured principal/dependency closure. This
    // neither binds an acceptance receipt to governed work nor certifies scope completeness.
    public static async Task<string> CaptureBindingIdentityAsync(AssurancePolicy policy, string principal, CancellationToken token)
    {
        EnsureEnabled(Principal(policy, principal));
        if (policy.Governed is not null)
            Require(policy.Areas.All(a => Principal(policy, principal).Areas.Contains(a.Id)), "unsupported", "Governed bridge requires the complete declared area closure in each principal's existing scope.");
        var rows = new List<object>();
        foreach (var area in Areas(policy, principal).OrderBy(area => area.Id, StringComparer.Ordinal))
        {
            EnsureAreaGranted(policy, principal, area.Id);
            var snapshot = await AssuranceSnapshotReader.CaptureAsync(policy, area.Id, token).ConfigureAwait(false);
            rows.Add(new { snapshot.AreaId, snapshot.BindingSha256 });
        }
        return Hash(new { policy.CaseId, policy.Implementer, Areas = rows });
    }

    public static async Task<string> CapturePolicyBindingIdentityAsync(AssurancePolicy policy, CancellationToken token)
    {
        var rows = new List<object>();
        foreach (var area in policy.Areas.OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            var snapshot = await AssuranceSnapshotReader.CaptureAsync(policy, area.Id, token).ConfigureAwait(false);
            rows.Add(new { snapshot.AreaId, snapshot.BindingSha256 });
        }
        return Hash(new { policy.CaseId, policy.Implementer, Areas = rows });
    }

    public static async Task ValidateInputsAsync(AssurancePolicy policy, string principal, CancellationToken token)
    {
        var areas = Areas(policy, principal);
        foreach (var area in areas) EnsureAreaGranted(policy, principal, area.Id);
        foreach (var area in areas)
        {
            // Uses execution's canonical path, symlink, UTF-8, size and dependency-cycle checks.
            _ = await AssuranceSnapshotReader.CaptureAsync(policy, area.Id, token).ConfigureAwait(false);
        }
    }
}
