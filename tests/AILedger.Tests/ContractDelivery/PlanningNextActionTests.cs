using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Tests.Support;

namespace AILedger.Tests.ContractDelivery;

[Collection(StandardInput.Collection)]
public sealed class PlanningNextActionTests
{
    [Fact]
    public async Task ContractFilingChangesNextActionToTheMissingPlan()
    {
        using var f = new DeliveryFixture();
        await f.InitializeAsync();
        await f.Context();
        await f.ReconAsync();
        await f.Stage(TaskStage.Design);
        f.Adapter.Act = request => f.FileAsync(request, "PROMPT", GovernedArtifactKind.PromptContract, "Contract");
        Assert.Equal(0, await f.Launch("RP", "planner"));

        AssertPlanAction(await f.Context());
        await f.Stage(TaskStage.Scope);
        AssertPlanAction(await f.Context());
    }

    [Fact]
    public async Task RefreshedReconReadinessMatchesStageAdmissionWhileOlderBriefStaysHistorical()
    {
        using var f = new DeliveryFixture();
        await f.InitializeAsync();
        await f.Context();
        await f.ReconAsync();
        await f.PlanAsync();
        await f.Ledger.ExecuteAsync(new AddClaimCommand(f.Ledger.Actor, null, "new-claim", new("C2"), "New premise", null));
        var old = await f.Context();
        Assert.Equal("missing", Scope(old).GetProperty("status").GetString());
        f.Adapter.Act = async request =>
        {
            await f.Ledger.ExecuteAsync(new ConsultLessonsCommand(request.ActorId, null, request.RunId.Value,
                request.RunId, LessonConsultationPurpose.Recon, "Assess changed premises", ["contract-delivery"], []));
            var template = InternalReconDocuments.CreateTemplate(await f.Ledger.StateAsync());
            var content = JsonSerializer.Serialize(template with
            {
                Assessments = template.Assessments.Select(a => a with { Domain = "internal" }).ToArray(),
                Report = "Refreshed assessment"
            });
            await f.Ledger.ExecuteAsync(new RecordArtifactCommand(request.ActorId, null, request.RunId.Value,
                new("RECON2"), GovernedArtifactKind.InternalRecon, "Refreshed", content, null, request.RunId, new("RECON")));
        };
        Assert.Equal(0, await f.Launch("RN2", "planner"));
        var current = await f.Context();
        Assert.True(current.GetProperty("taskVersion").GetInt64() > old.GetProperty("taskVersion").GetInt64());
        Assert.Equal("satisfied", Scope(current).GetProperty("status").GetString());
        await f.Stage(TaskStage.Scope);
        Assert.Equal("missing", Scope(old).GetProperty("status").GetString());
    }

    private static JsonElement Scope(JsonElement context) => context.GetProperty("nextActionContract")
        .GetProperty("actions").EnumerateArray().SelectMany(action => action.GetProperty("requirements").EnumerateArray())
        .First(requirement => requirement.GetProperty("id").GetString() == "stage-Scope");

    private static void AssertPlanAction(JsonElement context) => Assert.Contains(
        context.GetProperty("nextActionContract").GetProperty("actions").EnumerateArray(),
        action => action.GetProperty("action").GetString() == "author orchestration plan from the current prompt contract");
}
