using AILedger.Cli.Dispatch;
using AILedger.Core.Contracts;
namespace AILedger.Cli.Orchestration;

public enum DriverStatus { Blocked, Unsupported, AwaitingHumanDecision, AwaitingAcceptance, UnknownOutcome, Cancelled, Archived }
public sealed record DriverCoverage(TaskStage Stage, string Supported, string Boundary);
public sealed record DriverResult(DriverStatus Status, string Code, string Diagnostic,
    TaskStage? Stage, long? ObservedVersion, IReadOnlyList<ProviderDispatchResult> Dispatches,
    IReadOnlyList<EventId> TransitionEvents, IReadOnlyList<Escalation> Escalations);
public sealed record DriverRequest(WorkItemId? WorkItemId = null, AlternativeId? SerialJustification = null);
public sealed record DriverAgentProfile(ActorId Subject, string Provider, string? Model = null);

// Trusted host configuration, never an MCP body or model-authored approval.
public sealed record DriverHostOptions(TaskId TaskId, ActorId ActorId, string WorkingDirectory,
    DispatchHostOptions Dispatch, IReadOnlyDictionary<CognitiveWorkKind, DriverAgentProfile> Agents)
{
    public string DeploymentProfile { get; init; } = "macos-confined-cognitive-coordinator-v1";
    public TimeSpan OwnerLifetime { get; init; } = TimeSpan.FromMinutes(2);
    public DriverPreparationPolicy? Preparation { get; init; }
    public string? AcceptancePrincipal { get; init; }
    public int TimeoutSeconds { get; init; } = 1800;
    public DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.UtcNow.AddHours(8);
    // Setting a requirements-aware profile for the blind reviewer is refused by the shared host.
    public DispatchHostOptions? GovernedReviewDispatch { get; init; }
}
public interface IOrchestrationDriver
{
    Task<DriverResult> DriveAsync(DriverRequest request, CancellationToken cancellationToken);
}
public static class OrchestrationCoverage
{
    public const int Version = 3;
    public static IReadOnlyList<DriverCoverage> Stages { get; } = Array.AsReadOnly(new[]
    {
        new DriverCoverage(TaskStage.Discovery, "Bounded lead investigation, findings and escalations", "UserRequest and constraints require trusted intake"),
        new DriverCoverage(TaskStage.Research, "Live recon, role-filtered lessons and external research", "Missing roles or unresolved claims block kernel entry"),
        new DriverCoverage(TaskStage.Design, "Bounded planning/replanning and live PromptContract", "Scope/role/constraint changes require trusted preparation"),
        new DriverCoverage(TaskStage.Scope, "Live OrchestrationPlan", "Only preauthorized profiles selected by admitted planning decisions"),
        new DriverCoverage(TaskStage.Ready, "One explicitly selected prepared item", "New scope must fit trusted preparation policy"),
        new DriverCoverage(TaskStage.Execution, "One selected Worker through shared dispatch", "Task-13 verifier configuration is required"),
        new DriverCoverage(TaskStage.Verification, "Independent configured verifier and authored routing judgment", "No inference of correctness from run status"),
        new DriverCoverage(TaskStage.Repair, "Evidence-backed same-scope repair and fresh verification", "Changed scope routes to planning then trusted preparation"),
        new DriverCoverage(TaskStage.Review, "Blind governed review paired to verification", "Separate authorized assurance contexts and applicable explicit acceptance are required"),
        new DriverCoverage(TaskStage.Learn, "Lead synthesis, lesson marking and governed archive", "All work must be terminal through applicable acceptance and owning completion gates"),
        new DriverCoverage(TaskStage.Archive, "Terminal observation", "No retrospective, retention deletion or reopening")
    });
}
