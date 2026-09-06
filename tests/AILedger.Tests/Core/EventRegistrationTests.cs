using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class EventRegistrationTests
{
    // R2 (unregistered-event-bricks-task): the replay validator's default arm throws, and
    // TaskReducer.Apply calls it before applying. An event type nobody registered there fails
    // at write time, and if one ever reached disk the whole task would stop replaying.
    [Fact]
    public void R2_EveryDerivedEventTypeIsAcceptedByTheTransitionValidator()
    {
        var task = new TestTask();
        var reducer = new TaskReducer();
        var derivedTypes = typeof(LedgerEventData)
            .GetCustomAttributes(typeof(JsonDerivedTypeAttribute), inherit: false)
            .Cast<JsonDerivedTypeAttribute>()
            .Select(attribute => attribute.DerivedType)
            .ToArray();

        Assert.NotEmpty(derivedTypes);
        var unregistered = new List<string>();

        foreach (var derivedType in derivedTypes)
        {
            // The payload is deliberately blank: this asserts the type is *routed*, not that a
            // blank payload is valid. Every registered type rejects it with its own message.
            var data = (LedgerEventData)RuntimeHelpers.GetUninitializedObject(derivedType);
            var @event = new LedgerEvent(
                GovernedTaskState.CurrentSchemaVersion,
                new EventId($"{task.TaskId.Value}:0000000099"),
                task.TaskId,
                task.OperatorId,
                new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero),
                null,
                "correlation-r2",
                data);

            try
            {
                reducer.Apply(task.State, @event);
            }
            catch (Exception exception)
                when (exception.Message.Contains("Unsupported event data", StringComparison.Ordinal))
            {
                unregistered.Add(derivedType.Name);
            }
            catch
            {
                // Any other rejection means the switch routed it, which is what this guards.
            }
        }

        Assert.Empty(unregistered);
    }
}
