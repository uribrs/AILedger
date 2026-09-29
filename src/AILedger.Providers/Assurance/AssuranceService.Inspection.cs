using System.Text.Json;
using AILedger.Core.Assurance;
using AILedger.Core.Episodes;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Providers.Assurance;

public sealed partial class AssuranceService
{
    private async Task<AssuranceResponse> InspectAsync(InspectAssuranceRequest request, AssurancePolicy policy,
        IReadOnlyList<EpisodeRecord> records, List<AssuranceEntry> entries, string attempt, CancellationToken token)
    {
        Request(request.SchemaVersion); var snapshot = await CurrentAsync(policy, request.AreaId, null, token).ConfigureAwait(false);
        Require(request.Offset >= 0, "invalid_request", "offset cannot be negative.");
        if (request.Offset > 0) Require(request.ExpectedVersion == records.Count, "stale_snapshot", "Journal changed; restart inspection pagination.");
        var visible = entries.Where(e => Snapshot(e).AreaId == request.AreaId && Visible(e, Principal(policy).Role)).ToArray();
        if (request.ReceiptId is not null)
        {
            var entry = visible.SingleOrDefault(e => e.Receipt.Id == request.ReceiptId)
                ?? throw new AssuranceRefusal("invalid_reference", "Receipt is absent or outside this session's visibility.");
            return new("ok", attempt, entry.Receipt, false, Json(Applicability(entry, snapshot, policy, entries, includePayload: true)), null);
        }
        var currentReports = CurrentReports(visible, snapshot, policy);
        var inspected = currentReports.SelectMany(e => Payload<AssuranceReport>(e).Report.InspectedPaths).ToHashSet(StringComparer.Ordinal);
        var verified = currentReports.Where(e => Payload<AssuranceReport>(e).Role == "verification")
            .SelectMany(e => Payload<AssuranceReport>(e).Report.Checks.Where(c => c.Status == "pass").Select(c => c.CriterionId)).ToHashSet(StringComparer.Ordinal);
        var acceptances = visible.Where(e => e.Receipt.Operation == "accept_assurance" && Reasons(e, snapshot, policy, entries).Count == 0).ToArray();
        var accepted = acceptances.Length > 0;
        var disposed = acceptances.SelectMany(e => Payload<AssuranceAcceptance>(e).Decision.Dispositions.Select(d => d.FindingId)).ToHashSet(StringComparer.Ordinal);
        var pending = PendingChecks(records, entries).Where(r => r.GetProperty("area_id").GetString() == request.AreaId &&
            (Principal(policy).Role is "acceptance" or "synthesis" || r.GetProperty("principal").GetString() == _session.Principal))
            .Select(r => r.GetProperty("request_id").GetString()!).ToArray();
        var page = visible.AsEnumerable().Reverse().Skip(request.Offset).Take(16).Select(e => Applicability(e, snapshot, policy, entries, false)).ToArray();
        var inspection = new AssuranceInspection(snapshot with { Inputs = snapshot.Inputs.Select(i => i with { Content = "" }).ToArray() }, page,
            snapshot.Inputs.Select(i => i.Path).Where(p => !inspected.Contains(p)).ToArray(),
            snapshot.Criteria.Select(c => c.Id).Where(c => !verified.Contains(c)).ToArray(),
            CurrentFindings(visible, snapshot, policy).SelectMany(FindingIds).Where(id => !disposed.Contains(id)).ToArray(), accepted ? "accepted" : "not_accepted",
            "host observations and authored judgments are distinct; provider usage not collected", await _store.CountUncommittedAsync(_id, token).ConfigureAwait(false),
            pending, records.Count, visible.Length, request.Offset + page.Length < visible.Length ? request.Offset + page.Length : null,
            CheckAttempts(records, entries, request.AreaId, Principal(policy).Role));
        return new("ok", attempt, null, false, Json(inspection), null);
    }

    private AssuranceApplicability Applicability(AssuranceEntry entry, AssuranceSnapshot snapshot, AssurancePolicy policy,
        List<AssuranceEntry> entries, bool includePayload)
    {
        var reasons = Reasons(entry, snapshot, policy, entries);
        var superseded = entry.Receipt.Operation == "record_assurance" && entries.Any(e => e.Receipt.Operation == "record_assurance" && Payload<AssuranceReport>(e).Report.Supersedes == entry.Receipt.Id);
        return new(entry.Receipt.Id, entry.Receipt.Operation, entry.Receipt.Principal,
            reasons.Count > 0 ? "reassessment_required" : superseded ? "superseded" : "current", reasons, includePayload ? entry.Payload : null);
    }

    private List<string> Reasons(AssuranceEntry entry, AssuranceSnapshot snapshot, AssurancePolicy policy, List<AssuranceEntry> entries)
    {
        var old = Snapshot(entry); var reasons = new List<string>();
        if (entry.Receipt.ScopePolicySha256 != ScopePolicyIdentity(policy, snapshot.AreaId)) reasons.Add("assurance scope policy changed");
        var principal = policy.Principals.SingleOrDefault(p => p.Id == entry.Receipt.Principal);
        if (principal is null || !principal.Enabled || principal.ExpiresAt <= DateTimeOffset.UtcNow) reasons.Add("producer authority revoked or expired");
        if (principal is not null && (!principal.Areas.Contains(snapshot.AreaId) || entry.Receipt.Operation == "record_assurance" && principal.Role != Payload<AssuranceReport>(entry).Role)) reasons.Add("producer authority scope or role changed");
        foreach (var input in old.Inputs)
            if (!snapshot.Inputs.Any(i => i.Kind == input.Kind && i.Path == input.Path && i.Sha256 == input.Sha256)) reasons.Add(input.Kind + " changed: " + input.Path);
        if (old.RequirementSha256 != snapshot.RequirementSha256 && !reasons.Any(r => r.StartsWith("requirement", StringComparison.Ordinal))) reasons.Add("requirement criteria changed");
        foreach (var dependency in old.Dependencies)
            if (!snapshot.Dependencies.TryGetValue(dependency.Key, out var digest) || digest != dependency.Value) reasons.Add("relied-on area changed: " + dependency.Key);
        if (old.BindingSha256 != snapshot.BindingSha256 && reasons.Count == 0) reasons.Add("candidate binding changed");
        if (entry.Receipt.Operation == "accept_assurance" && reasons.Count == 0)
        {
            var decision = Payload<AssuranceAcceptance>(entry).Decision;
            try { ValidateAcceptance(decision, snapshot, policy, entries, entry.Receipt.Principal); }
            catch (AssuranceRefusal e) { reasons.Add(e.Message); }
        }
        return reasons;
    }
    private IEnumerable<AssuranceEntry> CurrentReports(IEnumerable<AssuranceEntry> entries, AssuranceSnapshot snapshot, AssurancePolicy policy) =>
        entries.Where(e => e.Receipt.Operation == "record_assurance" && Snapshot(e).BindingSha256 == snapshot.BindingSha256 &&
            e.Receipt.ScopePolicySha256 == ScopePolicyIdentity(policy, snapshot.AreaId) && policy.Principals.Any(p => p.Id == e.Receipt.Principal && p.Enabled && p.ExpiresAt > DateTimeOffset.UtcNow && p.Areas.Contains(snapshot.AreaId) && p.Role == Payload<AssuranceReport>(e).Role))
            .GroupBy(e => e.Receipt.Principal, StringComparer.Ordinal).Select(g => g.Last());
    // Supersession never deletes an unresolved defect/contradiction on the same candidate.
    private IEnumerable<AssuranceEntry> CurrentFindings(IEnumerable<AssuranceEntry> entries, AssuranceSnapshot snapshot, AssurancePolicy policy) =>
        entries.Where(e => e.Receipt.Operation == "record_assurance" && Snapshot(e).BindingSha256 == snapshot.BindingSha256);
    private IReadOnlyList<JsonElement> CheckAttempts(IReadOnlyList<EpisodeRecord> records, List<AssuranceEntry> entries, string area, string role) =>
        records.Where(r => r.Kind == "assurance_check_started" && r.Data.GetProperty("area_id").GetString() == area &&
            (role is "acceptance" or "synthesis" || r.Data.GetProperty("principal").GetString() == _session.Principal))
            .Select(r => Json(new { start = r.Data, observations = records.Where(o => o.Kind is "assurance_test_observed" or "assurance_check_reconciled" &&
                o.Data.GetProperty("fingerprint").GetString() == r.Data.GetProperty("fingerprint").GetString()).Select(o => new { o.Kind, o.Data }).ToArray(),
                receipt_id = entries.FirstOrDefault(e => e.Receipt.Operation == "run_assurance_checks" && e.Receipt.RequestSha256 == r.Data.GetProperty("fingerprint").GetString())?.Receipt.Id })).ToArray();
    private static IEnumerable<JsonElement> PendingChecks(IReadOnlyList<EpisodeRecord> records, List<AssuranceEntry> entries) =>
        records.Where(r => r.Kind == "assurance_check_started" && !entries.Any(e => e.Receipt.Operation == "run_assurance_checks" &&
            e.Receipt.RequestSha256 == r.Data.GetProperty("fingerprint").GetString()) &&
            !records.Any(end => end.Kind == "assurance_check_reconciled" && end.Data.GetProperty("fingerprint").GetString() == r.Data.GetProperty("fingerprint").GetString())).Select(r => r.Data);
}
