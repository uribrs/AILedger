using AILedger.Core.Application;
using AILedger.Core.Domain;
using AILedger.Core.Contracts;
namespace AILedger.Cli.Orchestration;

// The driver holds only this narrow surface, backed by the Part 2 restricted service.
internal sealed class DriverKernel(IGovernedTaskService restricted, DriverHostOptions options, IContextAssembler context)
{
    internal Task<GovernedTaskState?> ReadAsync(CancellationToken token) => restricted.GetStateAsync(options.TaskId, token);
    internal async Task BriefAsync(CancellationToken token)
    {
        var state = await ReadAsync(token).ConfigureAwait(false) ?? throw new InvalidDataException("Prepared task not found.");
        var artifacts = await new CognitiveArtifactLoader().LoadAsync(options.Dispatch.CognitiveRoot, token).ConfigureAwait(false);
        var manifest = context.Build(state, options.ActorId, null, artifacts, DateTimeOffset.UtcNow);
        try
        {
            await restricted.ExecuteAsync(options.TaskId, new RecordContextBuiltCommand(options.ActorId, null,
                "driver-brief", null, ContextSkills.From(manifest.Artifacts)), token).ConfigureAwait(false);
        }
        catch (ContextAlreadyBriefedException) { }
    }
    internal async Task ValidateRequiredChecksAsync(string principal, RunId verifier, CancellationToken token)
    {
        if (restricted is not AILedger.Storage.FileGovernedTaskService host)
            throw new AILedger.Core.Assurance.AssuranceRefusal("missing_governed_host", "Check scheduling requires the trusted file host.");
        var service = await AILedger.Cli.Assurance.AssuranceHost.OpenAsync(options.Dispatch.AssuranceAuthority!, options.Dispatch.AssuranceStore!,
            new(principal, "driver-check-observer", "trusted-host", null), null, token, host).ConfigureAwait(false);
        await service.ValidateRequiredChecksAsync(verifier.Value, token).ConfigureAwait(false);
    }
    internal Task ObserveCompletedWorkAsync(WorkItemId work, CancellationToken token) =>
        restricted is AILedger.Storage.FileGovernedTaskService host ? host.ObserveCompletedWorkAsync(options.TaskId, work, token) :
            throw new AILedger.Core.Assurance.AssuranceRefusal("missing_governed_host", "Closeout needs trusted current admission.");

    internal Task<CommandOutcome> CompleteAsync(WorkItemId work, long version, CancellationToken token) =>
        restricted.ExecuteAsync(options.TaskId, new CompleteWorkItemCommand(options.ActorId, null,
            "driver-completion-" + Guid.NewGuid().ToString("N"), work) { ExpectedVersion = version }, token);

    internal Task<CommandOutcome> TransitionAsync(TaskStage target, string? reason, AlternativeId? serial, long version, CancellationToken token, WorkItemId? completed = null) =>
        restricted.ExecuteAsync(options.TaskId, new RequestStageTransitionCommand(options.ActorId, null,
            "driver-transition-" + Guid.NewGuid().ToString("N"), target, Reason: reason,
            SerialJustification: target == TaskStage.Verification ? serial : null) { ExpectedVersion = version, CompletedWorkItemId = completed }, token);
}
