using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;

// A frozen-input report probe, not a task launcher or a new measurement implementation.
// Verification is the default. Capturing baselines requires an explicit switch.
if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--capture"))
{
    Console.Error.WriteLine("Usage: FindingsBaseline <fixture-directory> [--capture]");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
var token = cancellation.Token;
var root = Path.GetFullPath(args[0]);
var capture = args.Length == 2;
var json = LedgerJson.CreateOptions(indented: true);
var cases = JsonSerializer.Deserialize<BaselineCase[]>(
    await File.ReadAllTextAsync(Path.Combine(root, "cases.json"), token), json)
    ?? throw new InvalidDataException("Missing baseline cases.");

foreach (var item in cases)
{
    var history = await ReadHistoryAsync(Path.Combine(root, item.Events), json, token);
    GovernedTaskState? state = null;
    var reducer = new TaskReducer();
    foreach (var entry in history)
    {
        token.ThrowIfCancellationRequested();
        state = reducer.Apply(state, entry);
    }
    if (state is null) throw new InvalidDataException("Empty history.");
    var refusals = item.Refusals is null ? null :
        await ReadRefusalsAsync(Path.Combine(root, item.Refusals), json, token);
    var result = new
    {
        retrospective = TaskRetrospective.Build(state, history, refusals),
        assurance = TaskCloseoutEvidence.Build(state, history)
    };
    await CompareOrCaptureAsync(root, item.Expected, result, capture, json, token);
    Console.WriteLine($"{item.Name}: {history.Count} events; {(capture ? "captured" : "matched")}");
}

var samples = JsonSerializer.Deserialize<TelemetrySample[]>(
    await File.ReadAllTextAsync(Path.Combine(root, "provider-samples.json"), token), json)
    ?? throw new InvalidDataException("Missing provider samples.");
var costs = samples.Select(sample => new
{
    sample.Name,
    Cost = RunCostReader.Read(sample.Events.Select((entry, index) =>
        new ProviderEvent(index + 1, entry.Type, entry.RawJson, null, true, false)).ToArray())
}).ToArray();
await CompareOrCaptureAsync(root, "expected/provider-costs.json", costs, capture, json, token);
Console.WriteLine($"provider-costs: {samples.Length} samples; {(capture ? "captured" : "matched")}");
return 0;

static async Task<List<LedgerEvent>> ReadHistoryAsync(
    string path, JsonSerializerOptions json, CancellationToken token)
{
    var history = new List<LedgerEvent>();
    await foreach (var line in File.ReadLinesAsync(path, token))
    {
        history.Add(JsonSerializer.Deserialize<LedgerEvent>(line, json)
            ?? throw new InvalidDataException($"Null event in {path}."));
    }
    // Only complete, frozen histories belong here. Torn-write recovery is exercised by the
    // storage tests, not reimplemented in this report probe.
    return history;
}

static async Task<RetrospectiveRefusalJournal> ReadRefusalsAsync(
    string path, JsonSerializerOptions json, CancellationToken token)
{
    var rows = new List<RetrospectiveRefusal>();
    var unreadable = 0;
    await foreach (var line in File.ReadLinesAsync(path, token))
    {
        try
        {
            var row = JsonSerializer.Deserialize<RefusalRecord>(line, json);
            if (row is null) { unreadable++; continue; }
            rows.Add(new RetrospectiveRefusal(
                row.ActorId, row.Command, row.Site, row.Message, row.KernelIdentity));
        }
        catch (JsonException) { unreadable++; }
    }
    return new RetrospectiveRefusalJournal(rows, unreadable);
}

static async Task CompareOrCaptureAsync<T>(
    string root, string expected, T result, bool capture,
    JsonSerializerOptions json, CancellationToken token)
{
    var path = Path.Combine(root, expected);
    var actual = JsonSerializer.Serialize(result, json);
    if (capture)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, actual + "\n", token);
        return;
    }
    var frozen = await File.ReadAllTextAsync(path, token);
    if (!JsonNode.DeepEquals(JsonNode.Parse(frozen), JsonNode.Parse(actual)))
        throw new InvalidDataException($"Baseline changed: {expected}. Inspect the difference; do not recapture to make it pass.");
}

internal sealed record BaselineCase(string Name, string Events, string? Refusals, string Expected);
internal sealed record TelemetrySample(string Name, TelemetryEvent[] Events);
internal sealed record TelemetryEvent(string Type, string RawJson);
