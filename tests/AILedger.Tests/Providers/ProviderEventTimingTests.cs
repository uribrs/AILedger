using AILedger.Core.Contracts;

namespace AILedger.Tests.Providers;

// Sequence orders the stream and says nothing about duration. Without a read time a retained stream
// shows what an agent did and never when — and for codex the provider's own timing store is deleted
// with the governed CODEX_HOME at run end (C32), so the sidecar is the only place it can live.
//
// These pin the contract rather than the adapter loop: the field is nullable and trailing so every
// sidecar written before it existed still deserialises, and that absence must stay distinguishable
// from a zero.
public sealed class ProviderEventTimingTests
{
    [Fact]
    public void AStreamEventCarriesNoReadTimeUnlessOneWasGiven()
    {
        var stored = new ProviderEvent(1, "turn.completed", "{}", null, true, false);

        Assert.Null(stored.RecordedAt);
    }

    [Fact]
    public void AStreamEventKeepsTheReadTimeItWasStampedWith()
    {
        var read = new DateTimeOffset(2026, 9, 9, 8, 30, 0, TimeSpan.Zero);

        var stamped = new ProviderEvent(1, "turn.completed", "{}", null, true, false) with
        {
            RecordedAt = read
        };

        Assert.Equal(read, stamped.RecordedAt);
    }

    // The distinction the field exists to keep: a stream whose first line was read at the epoch is
    // not the same as one nobody timed, and a non-nullable field could not tell them apart.
    [Fact]
    public void AZeroReadTimeIsNotTheSameAsNoReadTime()
    {
        var zero = new ProviderEvent(1, "system", "{}", null, false, false) with
        {
            RecordedAt = DateTimeOffset.UnixEpoch
        };
        var untimed = new ProviderEvent(1, "system", "{}", null, false, false);

        Assert.NotNull(zero.RecordedAt);
        Assert.Null(untimed.RecordedAt);
        Assert.NotEqual(zero, untimed);
    }
}
