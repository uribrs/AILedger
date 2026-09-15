using AILedger.Core.Contracts;

namespace AILedger.Core.Artifacts;

internal static class ArtifactStateProjector
{
    internal static GovernedTaskState Add(GovernedTaskState state, ArtifactRecorded recorded) =>
        state with
        {
            Artifacts = Set(state.Artifacts, recorded.Artifact.ArtifactId, recorded.Artifact)
        };

    private static IReadOnlyDictionary<TKey, TValue> Set<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> source,
        TKey key,
        TValue value)
        where TKey : notnull
    {
        var copy = new Dictionary<TKey, TValue>(source)
        {
            [key] = value
        };
        return copy;
    }
}
