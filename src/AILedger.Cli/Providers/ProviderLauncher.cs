using System.Runtime.ExceptionServices;
using AILedger.Cli.ContextBriefing;
using AILedger.Cli.Dispatch;
using AILedger.Cli.Routing;
using AILedger.Cli.Verification;
using AILedger.Core.Contracts;
using static AILedger.Cli.Routing.CliInput;
namespace AILedger.Cli.Providers;

// CLI transport only. Both this facade and routine hosts invoke ProviderDispatchService.
internal sealed class ProviderLauncher(Func<string, IAgentAdapter> adapters, IContextAssembler context,
    CliCommandExecutor executor, TextWriter error, Func<VerificationHost> verification)
{
    public async Task LaunchAsync(IGovernedTaskService service, CommandLine input, AgentLaunchMode mode,
        string ledgerRoot, string lessonRoot, CancellationToken cancellationToken)
    {
        var request = Parse(input, mode);
        var options = new DispatchHostOptions(ledgerRoot, lessonRoot)
        {
            CognitiveRoot = input.Optional("cognitive-root"), Executable = input.Optional("executable"),
            OutputSchema = input.Optional("output-schema"), AssuranceAuthority = input.Optional("assurance-authority"),
            AssuranceStore = input.Optional("assurance-store")
        };
        var dispatch = ProviderDispatchHost.Create(service, service, options, adapters, context, null, verification);
        var result = await dispatch.LaunchAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.Retention.Status == ResultRetentionStatus.Failed)
            await error.WriteLineAsync(result.Retention.Diagnostic).ConfigureAwait(false);
        if (result.Started is not null && (result.Completed is not null || result.ProviderResult is not null))
            await executor.WriteProviderReturnAsync(service, request.TaskId, request.ActorId, request.RunId,
                result.ProviderResult, mode != AgentLaunchMode.Resume).ConfigureAwait(false);
        if (result.Failure?.Kind == DispatchFailureKind.CompletionRecordingFailed && result.ProviderResult is not null)
            throw new IOException($"Provider run '{request.RunId}' returned a terminal result, but its Ledger run could not be closed. " +
                "The provider result was written to standard output for recovery.", result.Error);
        if (result.Error is not null) ExceptionDispatchInfo.Capture(result.Error).Throw();
    }

    private static ProviderDispatchRequest Parse(CommandLine input, AgentLaunchMode mode) =>
        new(Task(input), Actor(input), new(input.Required("run")), input.Required("provider"))
        {
            SubjectActorId = Subject(input), WorkItemId = OptionalId(input.Optional("work"), value => new WorkItemId(value)),
            Mode = mode, SessionId = input.Optional("session"), Model = input.Optional("model"),
            WorkingDirectory = input.Optional("working-directory"), AdditionalDirectories = input.Many("add-dir"),
            AdditionalWork = input.Many("also-work").Select(value => new WorkItemId(value)).ToArray(),
            Candidate = input.Optional("candidate"), VerifierRun = OptionalId(input.Optional("verifier-run"), value => new RunId(value)),
            // Invalid timeout remains a typed invalid value until request construction, preserving
            // the established cleanup path and absence of delivered-manifest metadata.
            TimeoutSeconds = input.Optional("timeout-seconds") is not { } timeout ? 1800 : int.TryParse(timeout, out var seconds) ? seconds : 0,
            MaximumContextBytes = ContextManifestBudget.ReadMaximumBytes(input),
            Cause = Cause(input), Correlation = Correlation(input),
            CoordinatorSession = OptionalId(input.Optional("coordinator-session"), value => new CoordinatorSessionId(value)),
            WithoutBriefReason = input.Optional("without-brief"),
            StaleBriefEvidence = OptionalId(input.Optional("with-stale-brief"), value => new EvidenceId(value))
        };
}

internal sealed class ProviderRunFailedException(string message) : Exception(message);
