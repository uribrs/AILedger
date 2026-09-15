using System.Reflection;
using System.Text.Json.Serialization;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Tests.ContextBriefing;

public sealed class ContextContractCompatibilityTests
{
    // R1: physical ownership may change; public source and persisted-event contracts may not.
    [Fact]
    public void PublicContextContractsKeepNamespacesShapesAndDiscriminators()
    {
        AssertPublicNamespace(
            typeof(ContextArtifactKind), typeof(ContextArtifact), typeof(ContextManifest),
            typeof(ContextSkill), typeof(ContextBuild), typeof(ContextBriefWaiver),
            typeof(ContextBuilt), typeof(ContextBriefWaived), typeof(RecordContextBuiltCommand),
            typeof(IContextAssembler));
        Assert.Equal("AILedger.Core.Application", typeof(ContextAssembler).Namespace);
        Assert.Equal("AILedger.Core.Application", typeof(ContextSkills).Namespace);
        Assert.Equal("AILedger.Core.Application", typeof(BriefWaiver).Namespace);
        Assert.Equal("AILedger.Core.Domain", typeof(ContextAlreadyBriefedException).Namespace);

        Assert.Equal(
            Enumerable.Range(0, 18),
            Enum.GetValues<ContextArtifactKind>().Select(value => (int)value));
        AssertConstructor<ContextSkill>(typeof(string), typeof(string));
        AssertConstructor<ContextBuilt>(
            typeof(RoleKind), typeof(WorkItemId?), typeof(IReadOnlyList<ContextSkill>));
        AssertConstructor<ContextBriefWaived>(
            typeof(string), typeof(string), typeof(EvidenceId?), typeof(WaiverProvenance));
        AssertConstructor<RecordContextBuiltCommand>(
            typeof(ActorId), typeof(EventId?), typeof(string), typeof(WorkItemId?),
            typeof(IReadOnlyList<ContextSkill>));

        AssertDiscriminator<LedgerCommand, RecordContextBuiltCommand>("context.build");
        AssertDiscriminator<LedgerEventData, ContextBuilt>("context.built");
        AssertDiscriminator<LedgerEventData, ContextBriefWaived>("context.brief-waived");
    }

    private static void AssertPublicNamespace(params Type[] types) =>
        Assert.All(types, type => Assert.Equal("AILedger.Core.Contracts", type.Namespace));

    private static void AssertConstructor<T>(params Type[] parameterTypes) =>
        Assert.NotNull(typeof(T).GetConstructor(parameterTypes));

    private static void AssertDiscriminator<TBase, TDerived>(string expected)
    {
        var attribute = typeof(TBase).GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Single(candidate => candidate.DerivedType == typeof(TDerived));

        Assert.Equal(expected, attribute.TypeDiscriminator);
    }
}
