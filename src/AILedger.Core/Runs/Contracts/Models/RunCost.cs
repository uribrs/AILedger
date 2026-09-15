namespace AILedger.Core.Application;

// What a finished run cost, read off the stream the provider already emitted. Nothing here
// instruments anything: both providers state these numbers on their terminal event today and the
// launcher used to write the whole stream to stdout and drop it (C3, E4).
//
// Three input buckets rather than one total, because C7 measured them billed at roughly 1x, 1.25x
// and 0.1x — a single sum is not proportional to what the run cost. The provider's raw usage object
// still goes to the sidecar verbatim, so nothing read here is the only copy (D3, D4).
// Turns is 32-bit and the token counters are 64-bit, deliberately. A turn count is a provider's own
// iteration count — 66 to 125 across the runs E4 sampled — and nothing aggregates it. A token
// counter is already in the millions on one codex run (8796519 in E5), and RC2 is that the runs most
// likely to cross the 32-bit limit are the expensive ones, so a counter that silently records
// nothing above int.MaxValue drops exactly the measurements that matter and biases the record down.
public sealed record RunCost(
    int? Turns,
    long? OutputTokens,
    long? TokensInUncached,
    long? TokensInCacheWrite,
    long? TokensInCacheRead)
{
    public static readonly RunCost Unmeasured = new(null, null, null, null, null);
}
