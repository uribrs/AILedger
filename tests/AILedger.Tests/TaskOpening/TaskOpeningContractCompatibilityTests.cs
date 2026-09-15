using System.Reflection;
using System.Text.Json.Serialization;
using AILedger.Core.Contracts;

namespace AILedger.Tests.TaskOpening;

public sealed class TaskOpeningContractCompatibilityTests
{
    [Fact]
    public void PublicTaskOpeningContractsKeepNamespacesShapesAndDiscriminators()
    {
        Assert.Equal("AILedger.Core.Contracts", typeof(OpenTaskCommand).Namespace);
        Assert.Equal("AILedger.Core.Contracts", typeof(TaskOpened).Namespace);

        AssertConstructor<OpenTaskCommand>(
            typeof(ActorId), typeof(EventId?), typeof(string), typeof(TaskId), typeof(string),
            typeof(string), typeof(IReadOnlyList<Lesson>), typeof(IReadOnlyList<string>));
        AssertConstructor<TaskOpened>(typeof(string), typeof(string), typeof(IReadOnlyList<string>));

        AssertDiscriminator<LedgerCommand, OpenTaskCommand>("task.open");
        AssertDiscriminator<LedgerEventData, TaskOpened>("task.opened");
    }

    private static void AssertConstructor<T>(params Type[] parameterTypes) =>
        Assert.NotNull(typeof(T).GetConstructor(parameterTypes));

    private static void AssertDiscriminator<TBase, TDerived>(string expected)
    {
        var attribute = typeof(TBase).GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Single(candidate => candidate.DerivedType == typeof(TDerived));

        Assert.Equal(expected, attribute.TypeDiscriminator);
    }
}
