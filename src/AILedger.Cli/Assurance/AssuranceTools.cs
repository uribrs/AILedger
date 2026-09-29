using System.Text.Json;

namespace AILedger.Cli.Assurance;

internal static class AssuranceTools
{
    internal static object Describe(string name)
    {
        using var stream = typeof(AssuranceTools).Assembly.GetManifestResourceStream("Assurance." + name)
            ?? throw new InvalidOperationException("Missing assurance schema.");
        using var document = JsonDocument.Parse(stream);
        return new { name, description = Description(name), inputSchema = document.RootElement.Clone(),
            annotations = new { readOnlyHint = name == "inspect_assurance", destructiveHint = name == "accept_assurance",
                idempotentHint = true, openWorldHint = false } };
    }
    private static string Description(string name) => name switch
    {
        "inspect_assurance" => "Inspect exact candidate/requirement/source identities, coverage, missing tests, interrupted checks and freshness. Retrieve a returned receipt_id for complete report/content. Follow next_offset with expected_version. This is not a grant or acceptance.",
        "read_assurance" => "Retrieve 1–32 exact captured input paths and a host read receipt on expected_binding. Reading proves delivery, not understanding. Keep original body/key/session for lost-response retry.",
        "run_assurance_checks" => "Run 1–8 operator-configured check IDs on captured bytes; no command or path supplied by the agent. Host captures actual output/environment/outcome. Unknown interrupted checks never rerun automatically.",
        "record_assurance" => "Record one coherent independent review/verification/synthesis checkpoint. Report every criterion, uninspected paths and uncertainty; use current own read/test receipts. Complete is authored status, not acceptance. Supersedes preserves partial findings; retry unchanged.",
        _ => "Explicitly accept one exact current area using independent complete review/verification receipts and evidence-backed dispositions of every finding. Requires actual accepting authority, freshness and coverage; no kernel completion or release grant."
    };
}
