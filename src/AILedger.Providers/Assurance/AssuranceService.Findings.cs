using AILedger.Core.Assurance;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Providers.Assurance;

public sealed partial class AssuranceService
{
    private void ValidateFindingJudgments(RecordAssuranceRequest request, AssuranceSnapshot snapshot,
        List<AssuranceEntry> entries, string role)
    {
        var judgments = request.FindingJudgments ?? [];
        Items(judgments, "finding judgments");
        Unique(judgments.Select(j => j.FindingId).ToArray(), "judged finding IDs");
        Require(judgments.Count == 0 || role == "synthesis", "authorization_denied",
            "Finding adjudication requires scoped synthesis authority; inspectors cannot erase findings.");
        foreach (var judgment in judgments)
        {
            Request(judgment.SchemaVersion);
            var original = entries.SingleOrDefault(e => FindingIds(e).Contains(judgment.FindingId))
                ?? throw new AssuranceRefusal("invalid_reference", "Original finding is absent or outside granted scope.");
            Require(Snapshot(original).AreaId == snapshot.AreaId, "invalid_reference", "Adjudication must stay in the affected area.");
            Require(original.Receipt.Principal != _session.Principal, "self_approval", "An independent principal must adjudicate the original finding.");
            Require(judgment.Decision is "upheld" or "refuted" or "not_applicable" or "repaired", "unsupported_disposition",
                "Policy permits evidenced upheld/refuted/not_applicable/repaired judgments; no residual-risk waiver is authorized.");
            Text(judgment.Rationale, "judgment rationale");
            Unique(judgment.RequirementIds, "affected requirements");
            Require(judgment.RequirementIds.Count > 0 && judgment.RequirementIds.All(id => snapshot.Criteria.Any(c => c.Id == id)),
                "invalid_reference", "Name the affected current requirement criteria.");
            var finding = Payload<AssuranceReport>(original).Report.Findings.Single(f => original.Receipt.Id + ":" + f.Key == judgment.FindingId);
            Require((finding.RequirementIds ?? []).All(judgment.RequirementIds.Contains), "incomplete_coverage", "Preserve every originally affected requirement.");
            Items(judgment.Uncertainty, "judgment uncertainty");
            foreach (var uncertainty in judgment.Uncertainty) Text(uncertainty, "judgment uncertainty");
            if (judgment.Decision == "repaired")
                Require(Snapshot(original).CandidateSha256 != snapshot.CandidateSha256, "unsupported_disposition", "A repair needs a changed physical candidate and fresh independent evidence.");
            Items(judgment.Evidence, "judgment evidence", 1);
            foreach (var evidence in judgment.Evidence) ValidateFindingEvidence(evidence, snapshot, entries);
        }
    }

    private void ValidateFindingEvidence(AssuranceFindingEvidence evidence, AssuranceSnapshot snapshot, List<AssuranceEntry> entries)
    {
        Text(evidence.Observation, "observed source or reproduction");
        if (evidence.Kind == "source_trace")
        {
            var read = Payload<AssuranceRead>(OwnEntry(entries, evidence.ReceiptId, "read_assurance", snapshot));
            Require(read.Inputs.Any(i => i.Path == evidence.PathOrCheckId && i.Content.Contains(evidence.Observation, StringComparison.Ordinal)),
                "unsupported_disposition", "Source trace must quote bytes from the adjudicator's own current host read receipt.");
            return;
        }
        if (evidence.Kind == "reproduction")
        {
            var entry = entries.SingleOrDefault(e => e.Receipt.Id == evidence.ReceiptId && e.Receipt.Operation == "run_assurance_checks" &&
                Snapshot(e).BindingSha256 == snapshot.BindingSha256 && e.Receipt.Principal != _session.Principal &&
                e.Receipt.PolicySha256 == _policyHash);
            Require(entry is not null, "unsupported_disposition", "Reproduction needs an independent current host check receipt.");
            Require(Payload<AssuranceTestBatch>(entry!).Tests.Any(t => t.CheckId == evidence.PathOrCheckId &&
                t.Outcome is "succeeded" or "failed" && t.EndedAt is not null && t.Limitation is null &&
                (t.StandardOutput.Contains(evidence.Observation, StringComparison.Ordinal) || t.StandardError.Contains(evidence.Observation, StringComparison.Ordinal))),
                "unsupported_disposition", "Quote an actual complete reproduction observation; unknown checks cannot dispose findings.");
            return;
        }
        throw new AssuranceRefusal("unsupported_disposition", "Supply a supported source_trace or reproduction, not an assertion.");
    }

    private static AssuranceFindingJudgment[] Judgments(AssuranceEntry entry) =>
        entry.Receipt.Operation == "record_assurance" ? (Payload<AssuranceReport>(entry).Report.FindingJudgments ?? []).ToArray() : [];
}
