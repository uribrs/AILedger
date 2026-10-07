using AILedger.Core.Assurance;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Providers.Assurance;

public sealed partial class AssuranceService
{
    private async Task<AssuranceResponse> ReadAsync(ReadAssuranceRequest request, AssurancePolicy policy,
        List<AssuranceEntry> entries, string attempt, CancellationToken token)
    {
        Request(request.SchemaVersion, request.RequestId); Scope(policy, request.AreaId);
        if (await ReplayAsync("read_assurance", request.RequestId, request, entries, attempt, token).ConfigureAwait(false) is { } replay) return replay;
        Require(AILedger.Core.Artifacts.ArtifactSubmissionIdentity.IsHash(request.ExpectedBinding), "invalid_request", "expected_binding must be the exact SHA-256 returned by inspection.");
        var snapshot = await CurrentAsync(policy, request.AreaId, request.ExpectedBinding, token).ConfigureAwait(false);
        Unique(request.Paths, "paths"); Require(request.Paths.Count > 0, "invalid_request", "Select at least one input path.");
        Require(request.Paths.All(p => snapshot.Inputs.Any(i => i.Path == p)), "invalid_reference", "Only the exact paths in this area may be read.");
        var read = new AssuranceRead(Identity(snapshot), snapshot.Inputs.Where(i => request.Paths.Contains(i.Path)).ToArray());
        return await CommitAsync("read_assurance", request.RequestId, request, read, snapshot, attempt, token).ConfigureAwait(false);
    }

    private async Task<AssuranceResponse> RecordAsync(RecordAssuranceRequest request, AssurancePolicy policy,
        List<AssuranceEntry> entries, string attempt, CancellationToken token)
    {
        Request(request.SchemaVersion, request.RequestId); Scope(policy, request.AreaId);
        if (await ReplayAsync("record_assurance", request.RequestId, request, entries, attempt, token).ConfigureAwait(false) is { } replay) return replay;
        Require(AILedger.Core.Artifacts.ArtifactSubmissionIdentity.IsHash(request.ExpectedBinding), "invalid_request", "expected_binding must be the exact SHA-256 returned by inspection.");
        var snapshot = await CurrentAsync(policy, request.AreaId, request.ExpectedBinding, token).ConfigureAwait(false);
        Require(_session.Principal != policy.Implementer, "self_approval", "An implementer cannot supply independent assurance for its own candidate.");
        ValidateReport(request, snapshot, entries.Where(e => Principal(policy).Areas.Contains(Snapshot(e).AreaId)).ToList(), Principal(policy).Role);
        ValidateRepairImpact(request, snapshot, policy, entries);
        if (snapshot.Governed is not null && Principal(policy).Role == "review" && request.Checks.Any(c => c.Status == "pass"))
            EnsureRequiredChecks(snapshot, entries);
        var previous = entries.LastOrDefault(e => e.Receipt.Operation == "record_assurance" &&
            e.Receipt.Principal == _session.Principal && Snapshot(e).AreaId == request.AreaId);
        Require(previous?.Receipt.Id == request.Supersedes, "stale_checkpoint", "Name your latest report receipt in supersedes, or null for the first checkpoint.");
        var report = new AssuranceReport(Identity(snapshot), request,
            snapshot.Inputs.Select(i => i.Path).Except(request.InspectedPaths, StringComparer.Ordinal).ToArray(), Principal(policy).Role);
        return await CommitAsync("record_assurance", request.RequestId, request, report, snapshot, attempt, token).ConfigureAwait(false);
    }

    private void ValidateReport(RecordAssuranceRequest request, AssuranceSnapshot snapshot,
        List<AssuranceEntry> entries, string role)
    {
        Require(request.Status is "partial" or "complete" or "blocked" or "unknown", "invalid_request", "Report status is partial, complete, blocked or unknown; it never means acceptance.");
        Text(request.Summary, "summary"); Unique(request.InspectedPaths, "inspected_paths"); Unique(request.ReadReceipts, "read_receipts");
        Items(request.Checks, "checks", 1); Items(request.Findings, "findings"); Items(request.Uncertainty, "uncertainty");
        foreach (var uncertainty in request.Uncertainty) Text(uncertainty, "uncertainty");
        Require(request.InspectedPaths.All(p => snapshot.Inputs.Any(i => i.Path == p)), "invalid_reference", "Inspection names an undeclared path.");
        var reads = request.ReadReceipts.Select(id => OwnEntry(entries, id, "read_assurance", snapshot)).Select(Payload<AssuranceRead>).ToArray();
        Require(request.InspectedPaths.All(path => reads.Any(r => r.Inputs.Any(i => i.Path == path))),
            "unobserved_inspection", "Every inspected path needs an actual host read receipt for this principal, session and binding.");
        Require(request.Checks.Select(c => c.CriterionId).Order(StringComparer.Ordinal).SequenceEqual(snapshot.Criteria.Select(c => c.Id).Order(StringComparer.Ordinal)),
            "incomplete_coverage", "Report every criterion exactly once, including unknown and not_checked results.");
        foreach (var check in request.Checks)
        {
            Require(check.Status is "pass" or "fail" or "unknown" or "not_checked", "invalid_request", "Check status is pass, fail, unknown or not_checked.");
            Text(check.Evidence, "check evidence"); Unique(check.TestReceipts, "test_receipts");
            foreach (var id in check.TestReceipts) _ = OwnEntry(entries, id, "run_assurance_checks", snapshot);
            if (check.Status != "pass") continue;
            Require(request.InspectedPaths.Count > 0, "unsupported_pass", "A pass requires observed input inspection.");
            if (role == "verification")
            {
                var expectedCheck = snapshot.Criteria.Single(c => c.Id == check.CriterionId).CheckId;
                Require(check.TestReceipts.Any(id => Payload<AssuranceTestBatch>(OwnEntry(entries, id, "run_assurance_checks", snapshot))
                    .Tests.Any(t => t.CheckId == expectedCheck && t.Outcome == "succeeded" && t.ExitCode == 0 && t.Limitation is null)),
                    "unsupported_pass", $"Criterion '{check.CriterionId}' requires a successful host receipt for '{expectedCheck}'; an authored assertion is insufficient.");
            }
        }
        ValidateFindingJudgments(request, snapshot, entries, role);
        Unique(request.Findings.Select(f => f.Key).ToArray(), "finding keys");
        foreach (var finding in request.Findings)
        {
            Text(finding.Key, "finding key", 128); Text(finding.Statement, "finding statement");
            Require(finding.Kind is "defect" or "contradiction" or "ambiguity", "invalid_request", "Finding kind is defect, contradiction or ambiguity.");
            Unique(finding.Paths, "finding paths"); Unique(finding.Contradicts, "contradicts");
            Unique(finding.RequirementIds ?? [], "finding requirements");
            Require((finding.RequirementIds ?? []).All(id => snapshot.Criteria.Any(c => c.Id == id)), "invalid_reference", "Unknown affected requirement.");
            Items(finding.Uncertainty ?? [], "finding uncertainty");
            foreach (var uncertainty in finding.Uncertainty ?? []) Text(uncertainty, "finding uncertainty");
            Require(finding.Paths.All(p => snapshot.Inputs.Any(i => i.Path == p)), "invalid_reference", "Finding names an undeclared input.");
            foreach (var id in finding.Contradicts)
                Require(entries.Any(e => Visible(e, role) && FindingIds(e).Contains(id)), "invalid_reference", "Contradiction target must be a visible recorded finding ID.");
        }
    }

    private AssuranceEntry OwnEntry(List<AssuranceEntry> entries, string id, string operation, AssuranceSnapshot snapshot)
    {
        var entry = entries.SingleOrDefault(e => e.Receipt.Id == id && e.Receipt.Operation == operation &&
            e.Receipt.Principal == _session.Principal && e.Receipt.SessionId == _session.SessionId &&
            e.Receipt.PolicySha256 == _policyHash && Snapshot(e).BindingSha256 == snapshot.BindingSha256);
        return entry ?? throw new AssuranceRefusal("invalid_reference", "Evidence receipt is absent, foreign, stale, or from another session/policy.");
    }
    private bool Visible(AssuranceEntry entry, string role) => role is "acceptance" or "synthesis" || entry.Receipt.Principal == _session.Principal;
    private static IReadOnlyList<string> FindingIds(AssuranceEntry entry) => entry.Receipt.Operation == "record_assurance"
        ? Payload<AssuranceReport>(entry).Report.Findings.Select(f => entry.Receipt.Id + ":" + f.Key).ToArray() : [];
}
