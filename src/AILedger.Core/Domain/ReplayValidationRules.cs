using AILedger.Core.Contracts;

namespace AILedger.Core.Domain;

// Stable replay primitives shared by concept validators. They contain no event-specific policy;
// each concept remains responsible for deciding which checks apply to its historical events.
internal static class ReplayValidationRules
{
    // Mirrors LessonCitationRules at replay time. Older records do not carry FromLesson, so their
    // null branch remains legal even as new records are required to cite only recalled lessons.
    internal static void EnsureCitedLessonWasRecalled(GovernedTaskState state, LessonId? fromLesson)
    {
        if (fromLesson is { } lessonId)
        {
            _ = Get(state.Lessons, lessonId, "recalled lesson");
        }
    }

    internal static void RequireAuthority(
        GovernedTaskState state,
        ActorId actorId,
        Capability capability,
        bool operatorRequired = false)
    {
        var assignment = Get(state.Roles, actorId, "actor role");
        if (operatorRequired && assignment.Role != RoleKind.Operator)
        {
            throw new GovernanceException("Only an operator can perform this transition.");
        }

        if (!assignment.Capabilities.Contains(capability))
        {
            throw new GovernanceException($"Actor '{actorId}' lacks capability '{capability}'.");
        }
    }

    internal static bool IsOperator(GovernedTaskState state, ActorId actorId) =>
        state.Roles.TryGetValue(actorId, out var assignment) && assignment.Role == RoleKind.Operator;

    internal static void ValidateProvenance(LedgerEvent @event, Provenance provenance, string expectedSource)
    {
        if (provenance.ActorId != @event.ActorId ||
            provenance.RecordedAt != @event.RecordedAt ||
            !string.Equals(provenance.Source, expectedSource, StringComparison.Ordinal))
        {
            throw new GovernanceException(
                "Payload provenance must match the event actor, timestamp, and transition source.");
        }
    }

    internal static TValue Get<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> values,
        TKey id,
        string kind)
        where TKey : notnull
    {
        if (!values.TryGetValue(id, out var value))
        {
            throw new GovernanceException($"Unknown {kind} '{id}'.");
        }

        return value;
    }

    internal static void EnsureNew<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> values,
        TKey id,
        string kind)
        where TKey : notnull
    {
        if (values.ContainsKey(id))
        {
            throw new GovernanceException($"A {kind} with ID '{id}' already exists.");
        }
    }

    internal static void EnsureReferencesExist<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> values,
        IEnumerable<TKey> ids,
        string kind)
        where TKey : notnull
    {
        foreach (var id in ids)
        {
            _ = Get(values, id, kind);
        }
    }

    internal static void EnsureUnique<T>(
        IReadOnlyList<T> values,
        string label,
        IEqualityComparer<T>? comparer = null)
    {
        if (values.Count != values.Distinct(comparer).Count())
        {
            throw new GovernanceException($"{label} cannot contain duplicates.");
        }
    }

    internal static void RequireDefined<TEnum>(TEnum value, string field) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new GovernanceException($"'{value}' is not a defined {field} value.");
        }
    }

    internal static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new GovernanceException($"{name} is required.");
        }
    }

    internal static void RequireId(string value, string name) => RequireText(value, name);
}
