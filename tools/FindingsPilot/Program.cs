using FindingsPilot;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: FindingsPilot <RN1-dataset-directory> <new-external-output-directory>");
    return 2;
}
using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var token = cancellation.Token;
var dataset = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
// Refuse outputs in any Git checkout, not just this tool's source directory.
for (var parent = new DirectoryInfo(output); parent is not null; parent = parent.Parent)
    if (File.Exists(Path.Combine(parent.FullName, ".git")) || Directory.Exists(Path.Combine(parent.FullName, ".git")))
        throw new ArgumentException("Pilot output must be outside a Git checkout.");
if (Directory.Exists(output) || File.Exists(output)) throw new ArgumentException("Output directory must be new.");
var prepared = await PilotJson.ReadDatasetAsync(dataset, "prepared", token);
var recorded = await PilotJson.ReadDatasetAsync(dataset, "recorded", token);
Directory.CreateDirectory(output);
var trials = new List<Trial>();
foreach (var size in new[] { 5, 20 })
    for (var repetition = 1; repetition <= 7; repetition++)
    {
        var trial = await PilotScenarios.RunAsync(Path.Combine(output, $"prepared-{size}-{repetition}"),
            prepared, "prepared", "normal", size, repetition, token);
        trials.Add(trial);
        Console.WriteLine($"prepared size={size} repetition={repetition}: {trial.RecordingWallMs:F3} ms; fidelity checked");
    }
trials.Add(await PilotScenarios.RunAsync(Path.Combine(output, "recorded-5"), recorded, "recorded", "normal", 5, 1, token));
foreach (var scenario in new[] { "lost-response", "missing-reference", "invalid-local-reference", "revoked-capability", "changed-key-content", "journals-unavailable" })
{
    trials.Add(await PilotScenarios.RunAsync(Path.Combine(output, scenario), prepared, "prepared", scenario, 5, 1, token));
    Console.WriteLine(scenario + ": passed");
}
await PilotJson.SaveAsync(Path.Combine(output, "summary.json"), new
{
    SchemaVersion = 1,
    Kind = "controlled-prepared-data-local-mcp",
    StartedProviders = 0,
    DatasetManifest = Path.Combine(dataset, "manifest.json"),
    TimingBoundary = "RecordingWallMs spans first call through final call including harness observations/output between calls. ExchangeTotalMs sums sequential local MCP exchanges including handshake, recorder, durable flush and journals. Both exclude fixture setup/final verification/process/model/network scheduling. Nested telemetry intervals are not added.",
    Trials = trials,
    Limitations = new[] { "No live provider trial; no causal historical speedup or cognitive improvement estimate.",
        "First repetition includes cold/JIT effects; retain individual repetitions.",
        "Response loss is an injected output-stream failure. Reconstruction uses new server/recorder objects, not an OS crash.",
        "Harness calls are counted independently; journal observations remain lower bounds without a completeness watermark.",
        "Prepared observations are historical input, not revalidated claims about present repositories." }
}, token);
Console.WriteLine($"Passed {trials.Count} trials. Results: {Path.Combine(output, "summary.json")}");
return 0;
