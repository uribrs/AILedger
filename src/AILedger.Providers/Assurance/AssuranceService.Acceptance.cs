using AILedger.Core.Assurance;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Providers.Assurance;

public sealed partial class AssuranceService
{
    private async Task<AssuranceResponse> AcceptAsync(AcceptAssuranceRequest request, AssurancePolicy policy,
        List<AssuranceEntry> entries, string attempt, CancellationToken token)
    {
        Request(request.SchemaVersion, request.RequestId); Scope(policy, request.AreaId);
        if (await ReplayAsync("accept_assurance", request.RequestId, request, entries, attempt, token).ConfigureAwait(false) is { } replay) return replay;
        Require(AILedger.Core.Artifacts.ArtifactSubmissionIdentity.IsHash(request.ExpectedBinding), "invalid_request", "expected_binding must be the exact SHA-256 returned by inspection.");
        var snapshot = await CurrentAsync(policy, request.AreaId, request.ExpectedBinding, token).ConfigureAwait(false);
        ValidateAcceptance(request, snapshot, policy, entries, _session.Principal);
        return await CommitAsync("accept_assurance", request.RequestId, request, new AssuranceAcceptance(Identity(snapshot), request), snapshot, attempt, token).ConfigureAwait(false);
    }

    private void ValidateAcceptance(AcceptAssuranceRequest request, AssuranceSnapshot snapshot, AssurancePolicy policy,
        List<AssuranceEntry> entries, string acceptor)
    {
        Require(policy.Principals.Any(p => p.Id == acceptor && p.Role == "acceptance" && p.Enabled && p.ExpiresAt > DateTimeOffset.UtcNow && p.Areas.Contains(snapshot.AreaId)),
            "authorization_denied", "Explicit acceptance requires current scoped acceptance authority.");
        Require(acceptor != policy.Implementer, "self_approval", "Implementer cannot accept its own candidate.");
        Text(request.Rationale, "acceptance rationale"); Unique(request.Reports, "reports"); Items(request.Dispositions, "dispositions");
        ValidateDependencyAcceptance(snapshot, policy, entries);
        var current = CurrentReports(entries.Where(e => Snapshot(e).AreaId == snapshot.AreaId), snapshot, policy).ToArray();
        var chosen = request.Reports.Select(id => current.SingleOrDefault(e => e.Receipt.Id == id)
            ?? throw new AssuranceRefusal("stale_assurance", "Acceptance references missing, superseded, revoked or stale assurance.")).ToArray();
        Require(chosen.All(e => e.Receipt.Principal != acceptor && e.Receipt.Principal != policy.Implementer), "self_approval", "Acceptance, implementation and independent inspection identities must differ.");
        var reviews = chosen.Where(e => Payload<AssuranceReport>(e).Role == "review").ToArray();
        var verifications = chosen.Where(e => Payload<AssuranceReport>(e).Role == "verification").ToArray();
        Require(reviews.Length > 0 && verifications.Length > 0, "missing_assurance", "Acceptance requires an independent review and verification report on this exact binding.");
        Require(reviews.All(r => verifications.All(v => r.Receipt.Principal != v.Receipt.Principal && r.Receipt.SessionId != v.Receipt.SessionId)),
            "self_approval", "Review and verification require distinct actual principals and sessions.");
        foreach (var entry in chosen)
        {
            var report = Payload<AssuranceReport>(entry);
            Require(report.Report.Status == "complete" && report.UninspectedPaths.Count == 0 && report.Report.Checks.All(c => c.Status == "pass") && report.Report.Uncertainty.Count == 0,
                "incomplete_assurance", "A selected report is partial, uninspected, untested, failed or uncertain. Preserve it and obtain applicable evidence; recovery cannot make it pass.");
        }
        // A contradictory/uncertain *other* current inspector cannot be hidden by selecting convenient reports.
        foreach (var entry in current.Except(chosen))
        {
            var report = Payload<AssuranceReport>(entry).Report;
            Require(report.Status == "complete" && report.Uncertainty.Count == 0 && report.Checks.All(c => c.Status == "pass"),
                "unresolved_disagreement", "Another current report is incomplete, failed or uncertain; obtain a corrected inspection or targeted synthesis before acceptance.");
        }
        var findings = CurrentFindings(entries.Where(e => Snapshot(e).AreaId == snapshot.AreaId), snapshot, policy).SelectMany(FindingIds).ToArray();
        Unique(request.Dispositions.Select(d => d.FindingId).ToArray(), "finding dispositions");
        Require(request.Dispositions.Select(d => d.FindingId).Order(StringComparer.Ordinal).SequenceEqual(findings.Order(StringComparer.Ordinal)),
            "unresolved_disagreement", "Every current candidate finding needs an explicit attributable disposition; no finding may be silently omitted.");
        foreach (var disposition in request.Dispositions)
        {
            Require(disposition.Decision == "not_applicable", "unresolved_disagreement", "Only an evidenced not_applicable judgment is supported. Real defects require a changed candidate and fresh assurance.");
            Text(disposition.Rationale, "disposition rationale"); Unique(disposition.EvidenceReports, "disposition evidence");
            Require(disposition.EvidenceReports.Count > 0 && disposition.EvidenceReports.All(id => chosen.Any(e => e.Receipt.Id == id)),
                "unsupported_disposition", "Dispositions need applicable independent evidence from the selected reports.");
        }
    }
    private void ValidateDependencyAcceptance(AssuranceSnapshot snapshot, AssurancePolicy policy, List<AssuranceEntry> entries)
    {
        foreach (var dependency in snapshot.Dependencies)
        {
            var candidates = entries.Where(e => e.Receipt.Operation == "accept_assurance" &&
                Snapshot(e).AreaId == dependency.Key && Snapshot(e).BindingSha256 == dependency.Value &&
                e.Receipt.ScopePolicySha256 == ScopePolicyIdentity(policy, dependency.Key));
            var valid = false;
            foreach (var entry in candidates)
            {
                try
                {
                    var acceptance = Payload<AssuranceAcceptance>(entry);
                    ValidateAcceptance(acceptance.Decision, acceptance.Snapshot, policy, entries, entry.Receipt.Principal);
                    valid = true; break;
                }
                catch (AssuranceRefusal) { }
            }
            Require(valid, "dependency_reassessment", $"Relied-on area '{dependency.Key}' requires current explicit acceptance on binding '{dependency.Value}'. Its missing evidence or unresolved disagreement cannot be hidden by this consumer.");
        }
    }

}
