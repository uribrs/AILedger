using AILedger.Core.Assurance;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Providers.Assurance;

public sealed partial class AssuranceService
{
    private void ValidateRepairImpact(RecordAssuranceRequest request, AssuranceSnapshot snapshot,
        AssurancePolicy policy, List<AssuranceEntry> entries)
    {
        if (request.RepairImpact is not { } impact) return;
        Require(Principal(policy).Role == "synthesis" && _session.Principal != policy.Implementer,
            "authorization_denied", "Repair impact requires independent scoped synthesis authority.");
        Request(impact.SchemaVersion);
        Text(impact.DependencyRationale, "dependency rationale");
        Unique(impact.AffectedAreas, "affected areas"); Unique(impact.RequirementIds, "requirements");
        Unique(impact.DependencyAreas, "dependencies"); Unique(impact.RequiredChecks, "required checks");
        Unique(impact.AddressedFindings, "addressed findings"); Unique(impact.ReuseReceipts, "reuse receipts");
        Unique(impact.InvalidateReceipts, "invalidated receipts"); Items(impact.Uncertainty, "impact uncertainty");
        foreach (var uncertainty in impact.Uncertainty) Text(uncertainty, "impact uncertainty");
        Require(snapshot.Governed is not null, "wrong_governed_binding", "Repair reassessment requires an explicit governed association.");
        var before = entries.SingleOrDefault(e => e.Receipt.Id == impact.BeforeReceipt && Snapshot(e).AreaId == snapshot.AreaId);
        Require(before is not null, "invalid_reference", "Name an original receipt for this area as the before basis.");
        Require(request.InspectedPaths.Count == snapshot.Inputs.Count, "unobserved_inspection", "Impact assessment must inspect all current area inputs.");
        Require(impact.AffectedAreas.All(a => policy.Areas.Any(p => p.Id == a)), "invalid_reference", "Unknown affected area.");
        foreach (var area in impact.AffectedAreas) Scope(policy, area);
        Require(impact.RequirementIds.Order().SequenceEqual(snapshot.Criteria.Select(c => c.Id).Order()),
            "incomplete_coverage", "Assess every applicable requirement, including unaffected requirements.");
        Require(impact.RequiredChecks.Order().SequenceEqual(snapshot.Criteria.Select(c => c.CheckId).Distinct().Order()),
            "incomplete_coverage", "Required checks derive from all applicable criteria; impact cannot waive tests.");
        Require(impact.DependencyAreas.Order().SequenceEqual(DependencyClosure(policy, snapshot.AreaId).Order()),
            "dependency_reassessment", "Assess the complete declared transitive dependency closure.");
        Unique(impact.ImpactReadReceipts, "impact read receipts");
        foreach (var area in impact.DependencyAreas.Concat(impact.AffectedAreas).Distinct().Where(a => a != snapshot.AreaId))
        {
            var reads = entries.Where(e => impact.ImpactReadReceipts.Contains(e.Receipt.Id) && e.Receipt.Operation == "read_assurance" &&
                e.Receipt.Principal == _session.Principal && e.Receipt.SessionId == _session.SessionId && e.Receipt.PolicySha256 == _policyHash &&
                Snapshot(e).AreaId == area && Hash(Snapshot(e).Governed) == Hash(snapshot.Governed)).Select(Payload<AssuranceRead>).ToArray();
            var configured = policy.Areas.Single(a => a.Id == area);
            Require(configured.CandidatePaths.Concat(configured.RequirementPaths).Concat(configured.SourcePaths)
                .All(path => reads.Any(r => r.Inputs.Any(i => i.Path == path))), "unobserved_inspection",
                "Semantic impact needs own current host reads of affected and transitively depended-on areas, not file equality alone.");
        }
        var changed = !SamePhysicalBasis(Snapshot(before!), snapshot);
        Require(!changed || impact.AffectedAreas.Contains(snapshot.AreaId), "unsupported_impact", "Host-observed physical or dependency change must be reported as affected.");
        var findings = entries.Where(e => Snapshot(e).AreaId == snapshot.AreaId).SelectMany(FindingIds).ToArray();
        Require(impact.AddressedFindings.All(findings.Contains), "invalid_reference", "Addressed findings retain original identities; impact does not dispose them.");
        Require(!impact.ReuseReceipts.Intersect(impact.InvalidateReceipts).Any(), "invalid_request", "Evidence cannot be both reused and invalidated.");
        foreach (var id in impact.InvalidateReceipts)
            Require(entries.Any(e => e.Receipt.Id == id && Snapshot(e).AreaId == snapshot.AreaId), "invalid_reference", "Invalidation must name original area evidence.");
        foreach (var id in impact.ReuseReceipts)
        {
            var original = entries.SingleOrDefault(e => e.Receipt.Id == id);
            Require(original is not null && CanAssociateReview(original, snapshot, policy) &&
                original.Receipt.Principal != _session.Principal && impact.Uncertainty.Count == 0 &&
                !impact.AffectedAreas.Contains(snapshot.AreaId), "unsupported_reuse",
                "Reuse requires independent impact judgment, unchanged physical/dependency/context/authority basis and an unaffected complete review. Verification and acceptance never transfer.");
        }
    }

    private static IReadOnlyList<string> DependencyClosure(AssurancePolicy policy, string area)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            foreach (var dependency in policy.Areas.Single(a => a.Id == id).DependsOnAreas)
                if (result.Add(dependency)) Visit(dependency);
        }
        Visit(area); return result.ToArray();
    }

    private static bool SamePhysicalBasis(AssuranceSnapshot before, AssuranceSnapshot after) =>
        before.PhysicalBindingSha256 is not null && before.PhysicalBindingSha256 == after.PhysicalBindingSha256;

    private static bool SameGoverningBasis(AssuranceSnapshot before, AssuranceSnapshot after) =>
        before.Governed is { } old && after.Governed is { } current && old.TaskId == current.TaskId &&
        old.GoverningSha256 == current.GoverningSha256 && old.AuthoritySha256 == current.AuthoritySha256 &&
        old.WorkVersions.Select(v => v.WorkItemId).SequenceEqual(current.WorkVersions.Select(v => v.WorkItemId));

    private bool CanAssociateReview(AssuranceEntry entry, AssuranceSnapshot snapshot, AssurancePolicy policy)
    {
        if (entry.Receipt.Operation != "record_assurance" || Snapshot(entry).AreaId != snapshot.AreaId ||
            entry.Receipt.ScopePolicySha256 != ScopePolicyIdentity(policy, snapshot.AreaId) ||
            !SamePhysicalBasis(Snapshot(entry), snapshot) || !SameGoverningBasis(Snapshot(entry), snapshot)) return false;
        var report = Payload<AssuranceReport>(entry);
        return report.Role == "review" && report.Report.Status == "complete" && report.UninspectedPaths.Count == 0 &&
            report.Report.Checks.All(c => c.Status == "pass") && report.Report.Uncertainty.Count == 0 &&
            report.Report.Findings.Count == 0 && (report.Report.FindingJudgments?.Count ?? 0) == 0 &&
            policy.Principals.Any(p => p.Id == entry.Receipt.Principal && p.Role == "review" && p.Enabled &&
                p.ExpiresAt > DateTimeOffset.UtcNow && p.Areas.Contains(snapshot.AreaId));
    }

    private bool IsImpactInvalidated(string receipt, AssuranceSnapshot snapshot, AssurancePolicy policy, IEnumerable<AssuranceEntry> entries) =>
        entries.Any(e => e.Receipt.Operation == "record_assurance" && Snapshot(e).BindingSha256 == snapshot.BindingSha256 &&
            e.Receipt.ScopePolicySha256 == ScopePolicyIdentity(policy, snapshot.AreaId) &&
            Payload<AssuranceReport>(e).Report.RepairImpact?.InvalidateReceipts.Contains(receipt) == true &&
            policy.Principals.Any(p => p.Id == e.Receipt.Principal && p.Role == "synthesis" && p.Enabled && p.ExpiresAt > DateTimeOffset.UtcNow));

    private bool HasCurrentReuse(AssuranceEntry original, AssuranceSnapshot snapshot, AssurancePolicy policy, List<AssuranceEntry> entries)
    {
        if (!CanAssociateReview(original, snapshot, policy) || IsImpactInvalidated(original.Receipt.Id, snapshot, policy, entries)) return false;
        var impacts = entries.Where(e => e.Receipt.Operation == "record_assurance" && Snapshot(e).AreaId == snapshot.AreaId &&
            Snapshot(e).BindingSha256 == snapshot.BindingSha256 && e.Receipt.ScopePolicySha256 == ScopePolicyIdentity(policy, snapshot.AreaId))
            .GroupBy(e => e.Receipt.Principal).Select(g => g.Last()).ToArray();
        if (impacts.Any(e => Payload<AssuranceReport>(e).Report.RepairImpact?.InvalidateReceipts.Contains(original.Receipt.Id) == true)) return false;
        return impacts.Any(e =>
        {
            var report = Payload<AssuranceReport>(e);
            var impact = report.Report.RepairImpact;
            return report.Role == "synthesis" && report.Report.Status == "complete" && report.Report.Uncertainty.Count == 0 &&
                report.Report.Checks.All(c => c.Status == "pass") && impact is not null && impact.Uncertainty.Count == 0 &&
                impact.ReuseReceipts.Contains(original.Receipt.Id) && e.Receipt.Principal != original.Receipt.Principal &&
                policy.Principals.Any(p => p.Id == e.Receipt.Principal && p.Role == "synthesis" && p.Enabled &&
                    p.ExpiresAt > DateTimeOffset.UtcNow && p.Areas.Contains(snapshot.AreaId));
        });
    }

    // Trusted scheduling observation. Execution remains exclusively on the existing verifier tool.
    public async Task ValidateRequiredChecksAsync(string verifierRun, CancellationToken token)
    {
        Require(_governedContext is not null, "missing_governed_host", "Required checks need governed host admission.");
        await using var governed = await _governedContext!.AcquireAsync(_initial, _session, token).ConfigureAwait(false);
        _governedLease.Value = governed;
        await using var lease = await _store.AcquireAsync(_id, token).ConfigureAwait(false);
        var policy = await AuthorizeAsync(token).ConfigureAwait(false);
        var records = await _store.ReadAsync(_id, token).ConfigureAwait(false);
        var entries = Entries(records);
        Require(!PendingChecks(records, entries).Any(), "inspection_interrupted", "Unknown checks require owning reconciliation.");
        foreach (var area in policy.Areas)
        {
            var snapshot = await CurrentAsync(policy, area.Id, null, token).ConfigureAwait(false);
            var selected = entries.Where(e => e.Receipt.SessionId == verifierRun &&
                policy.Principals.Any(p => p.Id == e.Receipt.Principal && p.Role == "verification" && p.Enabled && p.ExpiresAt > DateTimeOffset.UtcNow)).ToList();
            EnsureRequiredChecks(snapshot, selected);
            // Also consider a later negative observation from another currently admitted verifier.
            EnsureRequiredChecks(snapshot, entries);
        }
        await AuthorizeAsync(token).ConfigureAwait(false);
    }

    private static void EnsureRequiredChecks(AssuranceSnapshot snapshot, List<AssuranceEntry> entries)
    {
        foreach (var check in snapshot.Criteria.Select(c => c.CheckId).Distinct())
        {
            var latest = entries.Where(e => e.Receipt.Operation == "run_assurance_checks" &&
                Snapshot(e).BindingSha256 == snapshot.BindingSha256).SelectMany(e => Payload<AssuranceTestBatch>(e).Tests)
                .LastOrDefault(t => t.CheckId == check);
            Require(latest is { Outcome: "succeeded", ExitCode: 0, Limitation: null }, "required_checks_pending",
                $"Required check '{check}' needs a current successful host outcome before dependent review.");
        }
    }
}
