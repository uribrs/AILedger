using System.Text.Json;
using AILedger.Tests.Support;
using AILedger.Cli.Assurance;
using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
using AILedger.Core.Handoffs;
using AILedger.Storage;
using AILedger.Storage.Episodes;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Tests.Orchestration;

[Collection(StandardInput.Collection)]
public sealed class AssuranceImplementerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryDeclaredImplementerMustMatchItsOwnWorkingRun(bool incorrect)
    {
        using var f = await CompletionFixture.OpenAsync();
        var policy = f.Policy with
        {
            Governed = new(1, "T1", ["A", "B"]),
            WorkImplementers = new Dictionary<string, string> { ["A"] = "worker", ["B"] = incorrect ? "worker" : "other" }
        };
        await f.SavePolicyAsync(policy);
        var host = (FileGovernedTaskService)f.Driver.Bundle.Service;
        if (incorrect)
        {
            var failure = await Assert.ThrowsAsync<AssuranceRefusal>(() => Open(f, host));
            Assert.Equal("wrong_governed_binding", failure.Code);
            Assert.Null((await host.InspectCoordinationAsync(new("T1"), default))?.AssuranceAnchor);
            return;
        }
        var service = await Open(f, host);
        var result = await service.InvokeAsync("inspect_assurance", Json(new InspectAssuranceRequest(1, "a")), default);
        Assert.Null(result.Error);
        var snapshot = result.Data!.Value.Deserialize<AssuranceInspection>(HandoffJson.Options)!.Snapshot;
        Assert.Equal("worker", snapshot.WorkImplementers!["A"]);
        Assert.Equal("other", snapshot.WorkImplementers["B"]);
        var anchor = (await host.InspectCoordinationAsync(new("T1"), default))!.AssuranceAnchor!;
        Assert.Equal(policy.CaseId, anchor.CaseId);
        Assert.Equal(new[] { "A", "B" }, anchor.WorkItemIds);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("team")]
    [InlineData("ungoverned")]
    public async Task IncompleteOrUnattributedMapsAreRejected(string invalid)
    {
        using var f = await CompletionFixture.OpenAsync();
        var actors = new Dictionary<string, string> { ["A"] = "worker" };
        if (invalid == "missing") actors.Clear();
        if (invalid == "extra") actors.Add("B", "other");
        var policy = f.Policy with { WorkImplementers = actors };
        if (invalid == "team") policy = policy with { Implementer = "team" };
        if (invalid == "ungoverned") policy = policy with { Governed = null };
        Assert.Throws<AssuranceRefusal>(() => Policy(policy));
    }

    [Fact]
    public async Task AttributionChangesInvalidateContentIdentitiesWithoutChangingLegacyIdentity()
    {
        using var f = await CompletionFixture.OpenAsync();
        Assert.Equal(f.Policy.Implementer, ImplementationIdentity(f.Policy));
        var mapped = f.Policy with { WorkImplementers = new Dictionary<string, string> { ["A"] = "worker" } };
        Assert.NotEqual(ScopePolicyIdentity(f.Policy, "a"), ScopePolicyIdentity(mapped, "a"));
        Assert.NotEqual(ImplementationIdentity(mapped), ImplementationIdentity(mapped with
        { WorkImplementers = new Dictionary<string, string> { ["A"] = "other" } }));
        Assert.True(IsImplementer(mapped, "worker"));
        Assert.False(IsImplementer(mapped, "operator"));
    }

    [Theory]
    [InlineData("review", "planning-lead", "record_assurance")]
    [InlineData("verification", "verifier", "record_assurance")]
    [InlineData("verification", "verifier", "run_assurance_checks")]
    [InlineData("synthesis", "planning-lead", "record_assurance")]
    [InlineData("acceptance", "operator", "accept_assurance")]
    public async Task SecondaryImplementerCannotProvideIndependentAssurance(string role, string ledgerRole, string operation)
    {
        using var f = await CompletionFixture.OpenAsync();
        await f.Driver.Bundle.Ok("actor", "attach", "--target", "other", "--role", ledgerRole);
        var policy = f.Policy with
        {
            Governed = new(1, "T1", ["A", "B"]),
            WorkImplementers = new Dictionary<string, string> { ["A"] = "worker", ["B"] = "other" },
            Principals = [.. f.Policy.Principals, new("other", role, true, DateTimeOffset.UtcNow.AddHours(1), ["a"])]
        };
        await f.SavePolicyAsync(policy);
        var service = await AssuranceHost.OpenAsync(f.Driver.Authority, f.Driver.Options.Dispatch.AssuranceStore!,
            new("other", "self-approval-probe", "external-client", null), null, default, (FileGovernedTaskService)f.Driver.Bundle.Service);
        var inspection = await Inspect(service);
        var binding = inspection.Snapshot.BindingSha256;
        var request = operation switch
        {
            "run_assurance_checks" => Json(new RunAssuranceChecksRequest(1, "self-check", "a", binding, ["unit"])),
            "accept_assurance" => Json(new AcceptAssuranceRequest(1, "self-accept", "a", binding, [], "Self acceptance", [])),
            _ => Json(new RecordAssuranceRequest(1, "self-report", "a", binding, "partial", "Self inspection", [], [],
                [new("behavior", "unknown", "Not checked", [])], [], []))
        };
        var response = await service.InvokeAsync(operation, request, default);
        Assert.Equal("self_approval", response.Error?.Code);
    }

    [Fact]
    public async Task AttributionCorrectionPreservesPinnedCaseEvidenceAndCheckBudget()
    {
        using var f = await CompletionFixture.OpenAsync();
        await f.RunAsync();
        Assert.NotNull((await f.AcceptAsync(withFinding: true)).Error);
        var host = (FileGovernedTaskService)f.Driver.Bundle.Service;
        var anchor = (await host.InspectCoordinationAsync(new("T1"), default))!.AssuranceAnchor!;
        var store = new FileEpisodeStore(f.Driver.Options.Dispatch.AssuranceStore!);
        var id = Hash(new { Namespace = "assurance-v1", f.Policy.CaseId });
        var before = await store.ReadAsync(id, default);
        Assert.NotEmpty(before.Where(r => r.Kind == "assurance_check_started"));
        await f.SavePolicyAsync(f.Policy with { WorkImplementers = new Dictionary<string, string> { ["A"] = "worker" } });
        var inspection = await Inspect(await Open(f, host));
        Assert.Contains(inspection.History, h => h.Operation == "record_assurance" && h.Status == "reassessment_required" &&
            h.Reasons.Contains("assurance scope policy changed"));
        Assert.Equal("not_accepted", inspection.Acceptance);
        Assert.Contains(f.FindingId!, inspection.UnresolvedFindings);
        var after = await store.ReadAsync(id, default);
        Assert.Equal(before.Select(Hash), after.Take(before.Count).Select(Hash));
        Assert.Equal(before.Count(r => r.Kind == "assurance_check_started"), after.Count(r => r.Kind == "assurance_check_started"));
        Assert.Equal(Hash(anchor), Hash((await host.InspectCoordinationAsync(new("T1"), default))!.AssuranceAnchor));
    }

    [Fact]
    public async Task LegacyInvalidMultiMemberPinCanBeCorrectedWithoutChangingItsIdentity()
    {
        using var f = await CompletionFixture.OpenAsync();
        var anchor = new GovernedAssuranceAnchor(1, f.Policy.CaseId, Path.GetFullPath(f.Driver.Options.Dispatch.AssuranceStore!), ["A", "B"]);
        var coordination = new CoordinationSnapshot(1, 1, 1, "assurance-configuration", DateTimeOffset.MinValue, null) { AssuranceAnchor = anchor };
        // Reproduce the pre-validation anchor persisted by older hosts; never edit live coordination.
        await File.WriteAllTextAsync(Path.Combine(f.Driver.Bundle.Root, "T1", "coordination-v1.json"), JsonSerializer.Serialize(coordination, LedgerJson.CreateOptions()));
        await f.SavePolicyAsync(f.Policy with
        {
            Governed = new(1, "T1", ["A", "B"]),
            WorkImplementers = new Dictionary<string, string> { ["A"] = "worker", ["B"] = "other" }
        });
        var host = (FileGovernedTaskService)f.Driver.Bundle.Service;
        await Inspect(await Open(f, host));
        Assert.Equal(Hash(anchor), Hash((await host.InspectCoordinationAsync(new("T1"), default))!.AssuranceAnchor));
    }

    private static async Task<AssuranceInspection> Inspect(AILedger.Providers.Assurance.AssuranceService service)
    {
        var response = await service.InvokeAsync("inspect_assurance", Json(new InspectAssuranceRequest(1, "a")), default);
        Assert.Null(response.Error);
        return response.Data!.Value.Deserialize<AssuranceInspection>(HandoffJson.Options)!;
    }

    private static Task<AILedger.Providers.Assurance.AssuranceService> Open(CompletionFixture f, FileGovernedTaskService host) =>
        AssuranceHost.OpenAsync(f.Driver.Authority, f.Driver.Options.Dispatch.AssuranceStore!,
            new("operator", "mapped-setup", "external-client", null), null, default, host);
}
