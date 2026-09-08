using System.Text.Json;
using AILedger.Core.Contracts;

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

public static class RunCostReader
{
    // The two terminal event types, duplicated from ProviderProtocol rather than shared with it.
    // Core cannot reference Providers — the dependency runs the other way — and the alternative,
    // having each adapter fill the numbers into AgentRunResult, puts the reader in a project this
    // work item does not hold. The duplication is one string per provider and the tests pin both.
    private const string ClaudeTerminalEventType = "result";
    private const string CodexTerminalEventType = "turn.completed";

    // A run whose stream carried no terminal event measured nothing, which is what a launch that
    // died mid-flight looks like. That absence is the measurement and must not read as zero.
    //
    // The invariant this reader keeps, which C15 records as never having been stated or tested:
    // every value Read returns must satisfy RunRules.EnsureCostRecordIsWellFormed. Read is called
    // where the launcher closes a run (C13), so a value that rule refuses throws inside the
    // completion path, aborts it over optional telemetry, and leaves a finished run active — which
    // blocks the next run on its work item and blocks Archive. The reader's accepted set must
    // therefore be a subset of the rule's, and it is the reader that gives way: the rule refuses a
    // negative count, so nothing below returns one.
    //
    // That is one invariant and not the three symptoms it was found as — a non-object payload
    // (RC1), an inverted subtraction (IC6), a directly reported negative (RC3). The rule is not
    // relaxed to match: a caller that does its own arithmetic is still refused there.
    public static RunCost Read(IReadOnlyList<ProviderEvent> events)
    {
        var terminal = events.LastOrDefault(providerEvent => providerEvent.IsTerminal);
        if (terminal is null)
        {
            return RunCost.Unmeasured;
        }

        try
        {
            return terminal.Type switch
            {
                ClaudeTerminalEventType => ReadClaude(terminal),
                CodexTerminalEventType => ReadCodex(terminal),
                _ => RunCost.Unmeasured
            };
        }
        catch (JsonException)
        {
            // A stream this kernel cannot parse is a stream it did not measure. The raw text is kept
            // beside the log either way, so nothing is lost by declining to guess at it here.
            //
            // Narrowed to JsonException on purpose: it covers text that is not JSON. Text that is
            // valid JSON but not an object is handled by Property below rather than by widening this
            // catch, which would swallow real faults (RC1, RE1).
            return RunCost.Unmeasured;
        }
    }

    // Claude states the turn count itself, on the result event, alongside a usage object.
    private static RunCost ReadClaude(ProviderEvent terminal)
    {
        using var document = JsonDocument.Parse(terminal.RawJson);
        var root = document.RootElement;
        var usage = Property(root, "usage");
        var (uncached, cacheWrite, cacheRead) = ReadInputBuckets(
            usage, "cache_creation_input_tokens", "cache_read_input_tokens", inputTotalIncludesCacheReads: false);
        return new RunCost(
            ReadTurnCount(root, "num_turns"), ReadTokenCount(usage, "output_tokens"),
            uncached, cacheWrite, cacheRead);
    }

    // Codex states no turn count of its own, so Turns stays null and that absence is the
    // measurement, exactly as ManifestHash's comment says of an unbriefed run (D4, IC1).
    //
    // Never count turn.completed events to fill it. AgentAdapterBase refuses any stream carrying
    // more than one terminal event and turn.completed is codex's, so the count is one for every
    // codex run that finished, against claude's 66 to 125 across the runs E4 sampled. Ranking those
    // two numbers against each other is the two-populations defect ALT1 and ALT3 were rejected for.
    private static RunCost ReadCodex(ProviderEvent terminal)
    {
        using var document = JsonDocument.Parse(terminal.RawJson);
        var usage = Property(document.RootElement, "usage");
        var (uncached, cacheWrite, cacheRead) = ReadInputBuckets(
            usage, "cache_write_input_tokens", "cached_input_tokens", inputTotalIncludesCacheReads: true);
        // output_tokens is taken exactly as reported. reasoning_output_tokens is a subset breakdown
        // of it and not an addend (C9, E13, E14); adding the two is the double count langfuse
        // shipped, and it is the output-side twin of the input-side one guarded below.
        return new RunCost(
            null, ReadTokenCount(usage, "output_tokens"), uncached, cacheWrite, cacheRead);
    }

    // The whole per-provider input mapping, in one place, because E10 records a real project
    // shipping a double count of this exact field.
    //
    // C6 is the documented difference and the only one: Anthropic reports input_tokens as uncached
    // tokens only, with both cache figures additive on top, while codex reports input_tokens as
    // every input token with cached_input_tokens already inside it. So the two providers need the
    // same three buckets read from differently-named properties, and only one of them needs
    // arithmetic:
    //
    //   claude   uncached = input_tokens                       write = cache_creation_input_tokens   read = cache_read_input_tokens
    //   codex    uncached = input_tokens - cached_input_tokens  write = cache_write_input_tokens      read = cached_input_tokens
    //
    // The two mistakes to avoid are inverses of each other. Adding a cache figure into claude's
    // input_tokens counts the cache twice on the input side; failing to subtract codex's
    // cached_input_tokens bills every cache read at the uncached rate. Neither provider's total is
    // ever summed with its own subsets.
    private static (long? Uncached, long? CacheWrite, long? CacheRead) ReadInputBuckets(
        JsonElement? usage,
        string cacheWriteProperty,
        string cacheReadProperty,
        bool inputTotalIncludesCacheReads)
    {
        var cacheWrite = ReadTokenCount(usage, cacheWriteProperty);
        var statedCacheRead = Property(usage, cacheReadProperty);
        var cacheRead = TokenCount(statedCacheRead);
        var input = ReadTokenCount(usage, "input_tokens");
        return inputTotalIncludesCacheReads
            ? (Uncached(input, cacheRead, statedCacheRead is not null), cacheWrite, cacheRead)
            : (input, cacheWrite, cacheRead);
    }

    // Subtracting the cache reads out of a total that contains them. A total the provider did not
    // report yields no bucket. A total reported with no cache figure beside it at all was entirely
    // uncached, and that is a real measurement.
    //
    // The two remaining cases record nothing. A cached count larger than the total it is reported
    // inside describes no run this kernel can measure, and the difference would be negative, which
    // the invariant on Read forbids. A cache figure the provider stated and this reader refused —
    // negative, or outside 64 bits — leaves the size of the subtraction unknown, so treating the
    // whole total as uncached would bill every cache read at the uncached rate: the promptfoo
    // defect in E10, arrived at through a guard rather than through a missing one. An unknown
    // bucket is readable as unknown; a bucket 56 times too large is not.
    private static long? Uncached(long? inputTotal, long? cacheRead, bool cacheReadStated) =>
        (inputTotal, cacheRead) switch
        {
            (null, _) => null,
            ({ } total, null) => cacheReadStated ? null : total,
            ({ } total, { } cached) when total >= cached => total - cached,
            _ => null
        };

    // JsonElement.TryGetProperty throws unless the element it is asked of is an object, and a
    // terminal event is a JSON object only by the provider's convention. Valid JSON that is not one
    // — an array, a null, a bare string — arrives on a stream this kernel does not control, and the
    // honest answer to it is no measurement, not an exception out of the completion path. Every
    // property read in this file goes through here, so the check exists once (RC1, RE1).
    private static JsonElement? Property(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } owner &&
        owner.TryGetProperty(name, out var value)
            ? value
            : null;

    // Two ways a number the provider states is not a measurement, and both record nothing rather
    // than a value. One is a range this field cannot hold, because an absent measurement is
    // readable as absent and a truncated one is not. The other is a negative count, which the
    // completion rule refuses by name — and per the invariant on Read that refusal is this reader's
    // problem to keep away from the launcher, not the launcher's to survive (RC3, C15).
    //
    // A turn count and a token count are both counts of things that happened, so neither has a
    // meaningful negative value to preserve. Zero stays legal in both: a turn that generated no
    // output tokens reported zero of them, which is not the same fact as a provider reporting none.
    private static int? ReadTurnCount(JsonElement? element, string property) =>
        Property(element, property) is { ValueKind: JsonValueKind.Number } value &&
        value.TryGetInt32(out var number) && number >= 0
            ? number
            : null;

    private static long? ReadTokenCount(JsonElement? element, string property) =>
        TokenCount(Property(element, property));

    private static long? TokenCount(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.Number } number &&
        number.TryGetInt64(out var count) && count >= 0
            ? count
            : null;
}
