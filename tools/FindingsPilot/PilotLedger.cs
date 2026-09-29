using AILedger.Cli;
using AILedger.Cli.Findings;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Findings;
using AILedger.Storage;

namespace FindingsPilot;

internal sealed class PilotLedger(string root)
{
    internal string Root { get; } = root;
    internal static TaskId TaskId { get; } = new("pilot");
    internal static ActorId Actor { get; } = new("researcher");
    internal string DirectoryPath => Path.Combine(Root, TaskId.Value);
    internal string EventsPath => Path.Combine(DirectoryPath, "events.jsonl");
    internal string Diagnostics => Path.Combine(DirectoryPath, "telemetry");
    internal FindingsHostConfiguration Configuration => new(Root, TaskId.Value, Actor.Value,
        "prepared-pilot", Diagnostics, AllowRecordFindings: true, AllowRunless: true);
    internal FileGovernedTaskService Service() => new(Root, new CommandHandler(), new TaskReducer());

    internal async Task OpenAsync(CancellationToken token)
    {
        Directory.CreateDirectory(Root);
        await Service().ExecuteAsync(TaskId, new OpenTaskCommand(new("operator"), null, "open", TaskId,
            "Disposable prepared RN1 pilot", "Measure prepared recording; no provider execution"), token);
        await SetEvidenceCapabilityAsync(true, token);
    }

    internal Task<CommandOutcome> SetEvidenceCapabilityAsync(bool allowed, CancellationToken token) =>
        Service().ExecuteAsync(TaskId, new AssignRoleCommand(new("operator"), null, "fixture-grants", Actor,
            RoleKind.Researcher, allowed ? [Capability.AddClaim, Capability.AddEvidence] : [Capability.AddClaim]), token);

    internal async Task VerifyUnchangedAsync(byte[] before, CancellationToken token)
    {
        var after = await File.ReadAllBytesAsync(EventsPath, token);
        PilotJson.Require(before.SequenceEqual(after), "Refusal or replay changed canonical bytes.");
    }

    internal async Task<GovernedTaskState> StateAsync(CancellationToken token) =>
        await Service().GetStateAsync(TaskId, token) ?? throw new InvalidDataException("Missing pilot state.");

    internal async Task<FindingsMeasurementReport> ReportAsync(CancellationToken token)
    {
        var service = Service();
        var state = await StateAsync(token);
        var history = new List<LedgerEvent>();
        await foreach (var entry in service.GetHistoryAsync(TaskId, token)) history.Add(entry);
        var report = TaskRetrospective.Build(state, history, null);
        var findings = await service.ReadFindingsMeasurementAsync(state, history, Diagnostics, token);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var cli = new CliApplication(output, error, _ => Service(),
            _ => throw new InvalidOperationException("Prepared probe launches no provider."), new ContextAssembler());
        var exit = await cli.RunAsync(["retrospective", "build", "--root", Root, "--task", TaskId.Value,
            "--findings", "--findings-telemetry", Diagnostics], token);
        PilotJson.Require(exit == 0, "Existing retrospective CLI failed: " + error);
        await File.WriteAllTextAsync(Path.Combine(Root, "retrospective.json"), output.ToString(), token);
        PilotJson.Require(report.Cost.OutputTokens.RunsMeasured == 0 && findings.Runs.Count == 0,
            "Prepared-data probe must not manufacture model usage or provider runs.");
        return findings;
    }

    internal async Task VerifyAsync(FindingsRequest source, FindingsMeasurementReport report, int transactions, CancellationToken token)
    {
        var state = await StateAsync(token);
        PilotJson.Require(state.Claims.Count == source.Findings.Count && state.Evidence.Count == source.Evidence.Count,
            "Duplicate or missing canonical records.");
        PilotJson.Require(report.CommittedTransactions == transactions && report.GranularEvents == 40,
            "Incorrect committed transaction/event population.");
        var claims = report.Transactions.SelectMany(t => t.Findings).ToDictionary(m => m.Key, m => m.ClaimId);
        var evidence = report.Transactions.SelectMany(t => t.Evidence).ToDictionary(m => m.Key, m => m.EvidenceId);
        foreach (var input in source.Findings)
        {
            var actual = state.Claims[new(claims[input.Key])];
            PilotJson.Require(actual.Statement == input.Statement && actual.ConsequenceIfWrong == input.ConsequenceIfWrong &&
                actual.Status == ClaimStatus.Open && actual.Provenance.ActorId == Actor, "Finding fidelity/attribution: " + input.Key);
        }
        foreach (var input in source.Evidence)
        {
            var actual = state.Evidence[new(evidence[input.Key])];
            PilotJson.Require(actual.SourceType == input.SourceType && actual.Citation == input.Citation && actual.Summary == input.Summary &&
                actual.Provenance.ActorId == Actor &&
                actual.Supports.Select(id => id.Value).SequenceEqual(input.Supports.Select(r => claims[r.Finding!])) &&
                actual.Refutes.Select(id => id.Value).SequenceEqual(input.Refutes.Select(r => claims[r.Finding!])),
                "Evidence fidelity/direction/attribution: " + input.Key);
        }
        PilotJson.Require(report.Transactions.All(t => t.ActorId == Actor.Value && t.RunId is null && t.CorrelationId == "prepared-pilot"),
            "Incorrect trusted binding.");
    }
}
