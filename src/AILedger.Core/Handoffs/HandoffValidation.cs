using System.Text.Json;
using AILedger.Core.Artifacts;
using AILedger.Core.Findings;

namespace AILedger.Core.Handoffs;

public static class HandoffValidation
{
    internal static readonly string[] Categories = ["decisions", "alternatives", "constraints", "contradictions",
        "lessons", "corrections", "uncertainty", "provenance"];
    internal const int MaximumRecords = 512;

    public static void Validate(HandoffRequest request)
    {
        if (request is null || request.SchemaVersion != 1 || request.ExpectedVersion < 1)
            throw new ArgumentException("Expected handoff v1 and a positive ledger version.");
        Text(request.LedgerIdentity); Selection(request.Selection);
        Spec(request.Spec);
        if (request.Inputs is null || request.Inputs.Count > MaximumRecords || request.Sources is null || request.Sources.Count > 32)
            throw new ArgumentException("At most 512 selected records and 32 pinned sources are supported.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in request.Inputs)
        {
            if (input is null) throw new ArgumentException("Null selection.");
            Text(input.Kind); Text(input.Id); Hash(input.Sha256);
            if (!keys.Add(Key(input.Kind, input.Id))) throw new ArgumentException("Duplicate input selection.");
            SelectionReason(input.Include, input.Material, input.Reason, input.OmissionRisk, input.RetrieveWhen);
        }
        foreach (var source in request.Sources)
        {
            if (source is null) throw new ArgumentException("Null source.");
            Text(source.Key); Text(source.Location); Text(source.Version); Hash(source.Sha256);
            if (!keys.Add("source:" + source.Key)) throw new ArgumentException("Duplicate source key.");
            SelectionReason(source.Content is not null, source.Material, source.Reason, source.OmissionRisk, source.RetrieveWhen);
            if (source.Content is { } content)
            {
                ValidateText(content, ArtifactSubmissionIdentity.MaximumContentBytes);
                if (System.Text.Encoding.UTF8.GetByteCount(content) > ArtifactSubmissionIdentity.MaximumContentBytes ||
                    ArtifactSubmissionIdentity.ContentHash(content) != source.Sha256)
                    throw new ArgumentException("Source content exceeds 128 KiB or differs from its SHA-256.");
            }
        }
        Preservation(request, keys);
        if (JsonSerializer.SerializeToUtf8Bytes(request, HandoffJson.Options).Length > HandoffJson.MaximumRequestBytes)
            throw new ArgumentException("Handoff request exceeds 512 KiB.");
    }

    internal static string Key(string kind, string id) => kind + ":" + id;
    internal static void Selection(string selection)
    {
        if (selection is not ("relevant" or "task")) throw new ArgumentException("Selection must be relevant or task.");
    }
    internal static void Text(string value) => ValidateText(value, 4096);
    private static void ValidateText(string value, int limit)
    {
        try { FindingsValidation.Text(value, limit, "handoff text"); }
        catch (FindingsRequestException e) { throw new ArgumentException(e.Message, e); }
    }
    private static void Hash(string value)
    {
        if (!ArtifactSubmissionIdentity.IsHash(value)) throw new ArgumentException("Expected lowercase SHA-256.");
    }
    private static void Spec(EpisodeSpec spec)
    {
        if (spec is null || spec.SchemaVersion != 1 || !FindingsValidation.IsRequestId(spec.Id) ||
            spec.Profile is not ("verification-preparation" or "research-to-design" or "read-only-audit"))
            throw new ArgumentException("Expected episode v1, an ID and a supported preparation profile.");
        Text(spec.Objective); Texts(spec.NonGoals); Texts(spec.ExpectedOutputs);
        Texts(spec.AcceptanceChecks); Texts(spec.StopConditions);
        if (spec.RequestedGrants is null || spec.RequestedGrants.Count is < 1 or > 32)
            throw new ArgumentException("Declare 1–32 requested read grants.");
        foreach (var grant in spec.RequestedGrants)
        {
            if (grant is null || grant.Action is not ("read_context" or "read_source"))
                throw new ArgumentException("Task 11 profiles describe read-only handoffs; requested grants confer no authority.");
            Text(grant.Scope);
        }
        var b = spec.Budget;
        if (b is null || b.MaximumPackageBytes is < 1024 or > 524288 || b.MaximumPreparationReads is < 1 or > 1024 ||
            b.MaximumAdditionalReads is < 0 or > 128 || b.MaximumElapsedMinutes is < 1 or > 240 || b.MaximumCostUsd != 0)
            throw new ArgumentException("Preparation budget: 1–512 KiB, 1–1024 reads; trial: 0–128 additional reads, 1–240 minutes, no authorized model spend.");
    }
    private static void Texts(IReadOnlyList<string> texts)
    {
        if (texts is null || texts.Count is < 1 or > 32) throw new ArgumentException("Declare 1–32 nonblank entries.");
        foreach (var text in texts) Text(text);
    }
    private static void SelectionReason(bool include, bool material, string reason, string risk, string trigger)
    {
        Text(reason); Text(risk); Text(trigger);
        if (material && !include) throw new ArgumentException("A declared material input cannot be omitted.");
    }
    private static void Preservation(HandoffRequest request, HashSet<string> keys)
    {
        if (request.Preservation is null || request.Preservation.Count != Categories.Length ||
            !request.Preservation.Select(c => c?.Category).Order().SequenceEqual(Categories.Order()))
            throw new ArgumentException("Assess all eight preservation categories exactly once.");
        var included = request.Inputs.Where(i => i.Include).Select(i => Key(i.Kind, i.Id))
            .Concat(request.Sources.Where(s => s.Content is not null).Select(s => "source:" + s.Key)).ToHashSet(StringComparer.Ordinal);
        foreach (var check in request.Preservation)
        {
            Text(check.Assessment);
            if (check.InputKeys is null || check.InputKeys.Count > 64 || check.InputKeys.Distinct().Count() != check.InputKeys.Count ||
                check.InputKeys.Any(key => !keys.Contains(key) || !included.Contains(key)))
                throw new ArgumentException("Preservation checks must cite distinct included inputs; an empty list needs an explicit absence/unknown assessment.");
        }
    }
}
