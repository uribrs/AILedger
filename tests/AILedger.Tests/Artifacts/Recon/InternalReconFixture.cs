using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Tests.Support;

namespace AILedger.Tests.Artifacts.Recon;

// Real command admission throughout: no stage placement or prerequisite waivers.
internal sealed class InternalReconFixture
{
    public TestTask Task { get; } = new(placeEntryStages: false);
    public RunId Run { get; private set; } = new("recon-run");
    public ActorId Lead => Task.GoverningLead();

    public InternalReconFixture(bool approach = true)
    {
        Task.RecordResearchTopic();
        Task.Transition(TaskStage.Research);
        if (approach) Task.RecordDiscardedAlternative();
    }

    public string Body(string domain = "internal")
    {
        var template = InternalReconDocuments.CreateTemplate(Task.State);
        return JsonSerializer.Serialize(template with
        {
            Assessments = template.Assessments.Select(row => row with { Domain = domain }).ToArray(),
            Report = "# Recon\nRECON-PRIVATE-NARRATIVE: inspected local contracts."
        });
    }

    public void Start(string id = "recon-run", string provider = "codex")
    {
        Run = new RunId(id);
        Task.Apply(new StartRunCommand(Task.OperatorId, null, Task.NextCorrelation(), Run,
            null, provider, null, null, null, null, Lead));
    }

    public void File(string? body = null, string id = "recon", string? supersedes = null) =>
        Task.Apply(ArtifactCommands.Record(Task, Lead, id, GovernedArtifactKind.InternalRecon,
            body ?? Body(), producerRun: Run, supersedes: supersedes));

    public void Complete(AgentRunStatus status = AgentRunStatus.Completed) =>
        Task.Apply(new CompleteRunCommand(Task.OperatorId, null, Task.NextCorrelation(), Run, status, "recon-session"));

    public void Eligible(string domain = "internal") { Start(); File(Body(domain)); Complete(); }

    public void Resolve()
    {
        var claim = new ClaimId("C-topic");
        var evidence = new EvidenceId("proof");
        Task.Apply(new AddEvidenceCommand(Task.OperatorId, null, Task.NextCorrelation(), evidence,
            "test-run", "ExternalProbe", "Directional proof", [claim], []));
        Task.Apply(new ResolveClaimCommand(Task.OperatorId, null, Task.NextCorrelation(), claim,
            ClaimStatus.Validated, [evidence]));
    }
}
