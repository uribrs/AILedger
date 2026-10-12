using System.Text.Json;
using AILedger.Core.Assurance;
using AILedger.Core.Episodes;
using AILedger.Core.Handoffs;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Providers.Assurance;

public sealed partial class AssuranceService
{
    private const string BudgetGrantKind = "assurance_check_budget_extended";

    // Like reconciliation, only the local trusted operator adapter exposes this method.
    // A grant adds capacity, never evidence, acceptance or a replacement case.
    public async Task<AssuranceCheckBudgetExtension> ExtendCheckBudgetAsync(
        ExtendAssuranceCheckBudgetRequest request, bool confirmed, CancellationToken token)
    {
        Require(confirmed, "confirmation_required", "Explicitly confirm the bounded check-budget extension.");
        ValidateBudgetRequest(request);
        await _authorizeSession(token).ConfigureAwait(false);
        Require(_initial.Governed is null || _governedContext is not null,
            "missing_governed_host", "Governed budget renewal requires the associated trusted host.");
        await using var governed = _governedContext is null ? null :
            await _governedContext.AcquireAsync(_initial, _session, token).ConfigureAwait(false);
        _governedLease.Value = governed;
        await using var lease = await _store.AcquireAsync(_id, token).ConfigureAwait(false);
        var policy = await AuthorizeAsync(token).ConfigureAwait(false);
        Require(Principal(policy).Role == "acceptance", "authorization_denied",
            "Only the explicit accepting authority can extend the check budget.");
        Require(!IsImplementer(policy, _session.Principal), "self_approval",
            "An implementer cannot grant independent check capacity.");
        Require(policy.Areas.All(a => Principal(policy).Areas.Contains(a.Id)), "authorization_denied",
            "Case-wide budget renewal requires authority over the complete declared area closure.");

        var records = await _store.ReadAsync(_id, token).ConfigureAwait(false);
        var fingerprint = Fingerprint("extend_check_budget", request);
        var previous = BudgetGrants(records).SingleOrDefault(g =>
            g.Session.Principal == _session.Principal && g.Request.RequestId == request.RequestId);
        if (previous is not null)
        {
            Require(previous.RequestSha256 == fingerprint, "request_conflict",
                "This renewal key already names another request, session or policy. Retry the original binding.");
            return new(previous, true);
        }
        Require(request.ExpectedPolicySha256 == _policyHash, "authority_changed",
            "Inspect the current policy identity before authorizing additional checks.");
        Require(!PendingChecks(records, Entries(records)).Any(), "inspection_interrupted",
            "Reconcile checks with unknown endings before extending the budget.");
        var budget = CheckBudget(records);
        Require(request.ExpectedConsumedBatches == budget.ConsumedBatches, "stale_snapshot",
            "Consumed check batches changed; inspect the current budget before renewal.");
        if (request.ExpectedBatchLimit is { } expectedLimit)
            Require(expectedLimit == budget.BatchLimit, "stale_snapshot",
                "Authorized check limit changed; inspect the current budget before revision.");
        else
            Require(budget.RemainingBatches == 0, "budget_not_exhausted",
                "To revise before exhaustion, include expected_batch_limit from a fresh inspection.");
        var basis = Math.Max(budget.BatchLimit, budget.ConsumedBatches);
        Require(request.AdditionalBatches <= int.MaxValue - basis, "invalid_request",
            "The resulting check allowance exceeds the supported counter range.");
        await AssuranceConfiguration.ValidateInputsAsync(policy, _session.Principal, token).ConfigureAwait(false);
        await AuthorizeAsync(token).ConfigureAwait(false);
        await RecordPlanAsync(policy, records, token).ConfigureAwait(false);
        var grant = new AssuranceCheckBudgetGrant("ABG_" + Guid.NewGuid().ToString("N"), fingerprint,
            request, _session, _policyHash, checked(basis + request.AdditionalBatches), DateTimeOffset.UtcNow);
        await _store.AppendAsync(_id, BudgetGrantKind, grant, token).ConfigureAwait(false);
        return new(grant, false);
    }

    private static void ValidateBudgetRequest(ExtendAssuranceCheckBudgetRequest request)
    {
        Request(request.SchemaVersion, request.RequestId);
        Text(request.RequestId, "request_id", 128);
        Text(request.Reason, "reason");
        Require(AILedger.Core.Artifacts.ArtifactSubmissionIdentity.IsHash(request.ExpectedPolicySha256),
            "invalid_request", "expected_policy_sha256 must be the exact hash returned by inspection.");
        Require(request.ExpectedConsumedBatches >= 0, "invalid_request", "Consumed batches cannot be negative.");
        Require(request.ExpectedBatchLimit is null or > 0, "invalid_request", "Expected batch limit must be positive.");
        Require(request.AdditionalBatches >= 1, "invalid_request",
            "Authorize a positive number of additional check batches based on the remaining work.");
    }

    private AssuranceCheckBudget CheckBudget(IReadOnlyList<EpisodeRecord> records)
    {
        var used = records.Count(r => r.Kind == "assurance_check_started");
        var limit = BudgetGrants(records).Where(g => g.PolicySha256 == _policyHash)
            .Select(g => g.BatchLimit).Append(AssurancePlanning.InitialBatchLimit(_initial)).Max();
        return new(_policyHash, used, limit, Math.Max(0, limit - used), int.MaxValue - Math.Max(limit, used));
    }

    private static IEnumerable<AssuranceCheckBudgetGrant> BudgetGrants(IEnumerable<EpisodeRecord> records) =>
        records.Where(r => r.Kind == BudgetGrantKind).Select(r =>
            r.Data.Deserialize<AssuranceCheckBudgetGrant>(HandoffJson.Options)
            ?? throw new InvalidDataException("Missing check-budget grant."));
}
