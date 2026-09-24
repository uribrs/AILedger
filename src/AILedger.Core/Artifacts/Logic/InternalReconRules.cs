using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.WorkItems;

namespace AILedger.Core.Artifacts;

internal static class InternalReconRules
{
    internal static void EnsureFilingStage(GovernedTaskState state)
    {
        if (state.Stage is not (TaskStage.Research or TaskStage.Design))
            throw Invalid("may only be filed at Research or Design.");
    }

    internal static void EnsureProducer(AgentRun run, ActorId owner)
    {
        if (run.ActorId != owner || run.WorkItemId is not null || run.Assurance is not null ||
            run.SubjectRole is not (RoleKind.Operator or RoleKind.PlanningLead or RoleKind.ImplementationLead))
            throw Invalid("requires its owner lead's task-wide producer without assurance binding.");
    }

    internal static InternalReconDocument Validate(GovernedTaskState state, string content)
    {
        try
        {
            using var json = JsonDocument.Parse(content);
            var root = json.RootElement;
            ExactProperties(root, "schemaVersion", "taskId", "claimSetHash", "assessments", "report");
            var version = root.GetProperty("schemaVersion");
            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
                throw Invalid("schemaVersion must be integer 1.");
            var taskId = Text(root, "taskId");
            if (taskId != state.TaskId.Value)
                throw Invalid("taskId must match the task.");
            var hash = Text(root, "claimSetHash");
            if (hash.Length != 64 || hash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
                hash != InternalReconDocuments.ComputeClaimSetHash(state))
                throw Invalid("claimSetHash does not match the current claim set; refresh and supersede recon.");
            var report = Text(root, "report");
            var assessments = root.GetProperty("assessments");
            if (assessments.ValueKind != JsonValueKind.Array)
                throw Invalid("assessments must be an array.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var rows = new List<InternalReconAssessment>();
            foreach (var row in assessments.EnumerateArray())
            {
                ExactProperties(row, "claimId", "domain");
                var id = Text(row, "claimId");
                var domain = Text(row, "domain");
                if (domain is not ("internal" or "external"))
                    throw Invalid("domain must be exactly internal or external.");
                if (!seen.Add(id) || !state.Claims.ContainsKey(new ClaimId(id)))
                    throw Invalid("assessments contain a duplicate or unknown claimId.");
                rows.Add(new(id, domain));
            }
            if (seen.Count != state.Claims.Count)
                throw Invalid("assessments must cover every claim, including resolved and superseded claims.");
            return new(1, taskId, hash, rows, report);
        }
        catch (JsonException)
        {
            throw Invalid("content must be strict schema-version-1 JSON.");
        }
    }

    // R3 (producer-currentness): never fall back to an earlier successful revision. Returns the
    // claims the current recon assesses as external, which the research consultation arm binds to.
    internal static IReadOnlyList<ClaimId> EnsureDesign(GovernedTaskState state)
    {
        var current = ArtifactApplicability.Current(state)
            .Where(artifact => artifact.Kind == GovernedArtifactKind.InternalRecon).ToArray();
        if (current.Length != 1)
            throw Invalid("Design requires one current InternalRecon artifact.");
        var artifact = current[0];
        if (artifact.WorkItemId is not null || artifact.Assurance is not null ||
            artifact.ProducerRunId is not { } producer ||
            !state.Runs.TryGetValue(producer, out var run))
            throw Invalid("Design requires task-wide recon with a producer.");
        EnsureProducer(run, artifact.Provenance.ActorId);
        if (!WorkItemVerificationRules.DidWork(run))
            throw Invalid("Design requires a completed InternalRecon producer with real cognition.");
        var document = Validate(state, artifact.Content);
        var external = document.Assessments.Where(row => row.Domain == "external").ToArray();
        if (external.Any(row => state.Claims[new ClaimId(row.ClaimId)].Status == ClaimStatus.Open))
            throw Invalid("Design cannot admit an open external claim.");
        return external.Select(row => new ClaimId(row.ClaimId)).ToArray();
    }

    // A1 (recon-consultation-arm). Command time only: called from ArtifactRules.Record, never from
    // the replay-shared authority or document paths (PC6), so historical recon still replays.
    // Document validation has already bound the document's claimSetHash to the current claim set,
    // so matching the current hash is matching the document's.
    internal static void EnsureConsulted(GovernedTaskState state, RunId? producerRunId)
    {
        if (producerRunId is not { } producer)
            throw Invalid("requires a producer run before its recon lesson consultation can be checked.");

        var hash = InternalReconDocuments.ComputeClaimSetHash(state);
        if (!state.LessonConsultations.Any(consultation =>
                consultation.Purpose == LessonConsultationPurpose.Recon &&
                consultation.RunId == producer &&
                string.Equals(consultation.ClaimSetHash, hash, StringComparison.Ordinal)))
            throw Invalid(
                $"requires a recon lesson consultation by producer run '{producerRunId}' against the current claim set.");
    }

    private static void ExactProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw Invalid("document and assessments must be objects.");
        var remaining = names.ToHashSet(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!remaining.Remove(property.Name))
                throw Invalid($"unknown or duplicate property '{property.Name}'.");
        if (remaining.Count != 0)
            throw Invalid("required properties are missing.");
    }

    private static string Text(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw Invalid($"'{name}' must be a nonblank string.");
        return value.GetString()!;
    }

    private static GovernanceException Invalid(string message) => new($"InternalRecon: {message}");
}
