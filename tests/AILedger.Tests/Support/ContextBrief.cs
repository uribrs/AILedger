using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;

namespace AILedger.Tests.Support;

// Adding work and launching a provider are refused until the acting actor has been briefed, so a
// test that reaches either command brief its actor first — which is what an operator now does
// before decomposing a task.
//
// It goes through the real 'context build' rather than writing the event by hand. The gate
// recomputes the skill digests from the cognitive layer and compares them, so a hand-written
// digest would be refused as stale. That refusal would be the check working; going through the
// production path is what makes the staged brief a current one.
internal static class ContextBrief
{
    public static async Task BuildAsync(
        string ledgerRoot,
        string taskId,
        string actorId = "operator",
        string? cognitiveRoot = null)
    {
        var application = new CliApplication(
            TextWriter.Null,
            TextWriter.Null,
            Service,
            _ => throw new InvalidOperationException("Briefing an actor starts no provider."),
            new ContextAssembler());
        // Written to a file rather than to standard output, so a test asserting on what the
        // command under test printed does not also read a manifest.
        var manifestPath = Path.Combine(Path.GetTempPath(), $"brief-{Guid.NewGuid():N}.json");
        var exit = await application.RunAsync(
            ["context", "build", "--root", ledgerRoot, "--task", taskId, "--actor", actorId,
             "--cognitive-root", cognitiveRoot ?? CognitiveRoot(), "--output", manifestPath],
            CancellationToken.None);
        File.Delete(manifestPath);
        if (exit != 0)
        {
            throw new InvalidOperationException(
                $"Briefing '{actorId}' on task '{taskId}' failed with exit code {exit}.");
        }
    }

    // The brief a test that drives the service directly stands in for the cognitive layer with. The
    // kernel reads no skill's text, only whether the digest a caller found still matches the one the
    // brief recorded, so a fixed pair is a complete stand-in — as long as both the brief and the
    // gated command name the same pair.
    public static readonly IReadOnlyList<ContextSkill> Served =
    [
        new ContextSkill("workflow-coordinator", "hash-of-workflow-coordinator"),
        new ContextSkill("task-orchestrator", "hash-of-task-orchestrator")
    ];

    // For a test that drives the service directly rather than through the CLI.
    public static async Task RecordAsync(
        IGovernedTaskService service,
        string taskId,
        string actorId = "operator")
    {
        await service.ExecuteAsync(
            new TaskId(taskId),
            new RecordContextBuiltCommand(
                new ActorId(actorId), null, $"brief-{actorId}", null, Served),
            CancellationToken.None).ConfigureAwait(false);
    }

    // The digests a brief built through the CLI recorded, for a test that briefs against the real
    // cognitive layer and then drives the service by hand. Restating them as literals would be a
    // second copy of what the layer says, and it would go stale the first time a skill is edited.
    public static async Task<IReadOnlyList<ContextSkill>> RecordedAsync(
        IGovernedTaskService service,
        string taskId,
        string actorId = "operator")
    {
        var state = await service.GetStateAsync(new TaskId(taskId), CancellationToken.None)
            .ConfigureAwait(false);
        return state!.ContextBuilds[new ActorId(actorId)].Skills;
    }

    // What the CLI does before a gated command: read the layer again and hand the kernel what it
    // serves now. A command that names nothing is refused, because a brief nobody can check is not
    // a current brief — so a test driving the service by hand says, through this, that the layer it
    // was briefed from has not changed.
    public static LedgerCommand WithServedSkills(LedgerCommand command) =>
        command switch
        {
            AddWorkItemCommand { SkillsServedNow: null } add => add with { SkillsServedNow = Served },
            StartRunCommand { LaunchTokenHash: not null, SkillsServedNow: null } start =>
                start with { SkillsServedNow = Served },
            _ => command
        };

    public static string CognitiveRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "cognitive");
                if (File.Exists(Path.Combine(candidate, "manifest.json")))
                {
                    return candidate;
                }
            }
        }

        throw new DirectoryNotFoundException("Test could not locate the cognitive root.");
    }

    // No lesson store: briefing reads the task and appends one event, and a shared store would let
    // a test holding a temporary root write into the operator's real one.
    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }
}
