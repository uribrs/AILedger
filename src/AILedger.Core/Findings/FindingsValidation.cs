using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;

namespace AILedger.Core.Findings;

public sealed class FindingsRequestException(string code, string message, string? path = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public string? ItemPath { get; } = path;
}

public static partial class FindingsValidation
{
    public const int MaximumBodyBytes = 256 * 1024;
    [GeneratedRegex("\\A[A-Za-z][A-Za-z0-9_-]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
    [GeneratedRegex("\\A[A-Za-z0-9._:-]{1,128}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex RequestPattern();
    public static bool IsKey(string? value) => value is not null && KeyPattern().IsMatch(value);
    public static bool IsRequestId(string? value) => value is not null && RequestPattern().IsMatch(value);

    // Copy bounded collections before awaiting: caller-owned arrays must not change the content
    // between fingerprinting and command translation.
    public static FindingsRequest Snapshot(FindingsRequest request)
    {
        if (request is null || request.SchemaVersion != 1 || !IsRequestId(request.RequestId))
            Fail("Expected schema version 1 and a valid request ID.");
        if (request!.Findings is null || request.Evidence is null || request.Findings.Count > 32 ||
            request.Evidence.Count > 64 || request.Findings.Count + request.Evidence.Count == 0)
            Fail("Expected findings/evidence arrays with 1–96 total items (32 findings, 64 evidence maximum).");
        var findings = request.Findings!.ToArray();
        var evidence = request.Evidence!.ToArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < findings.Length; i++)
        {
            var path = $"findings[{i}]";
            var item = findings[i];
            if (item is null) Fail("Finding is required.", path);
            AddKey(keys, item!.Key, path);
            Text(item.Statement, 8192, path + ".statement");
            OptionalText(item.ConsequenceIfWrong, 8192, path + ".consequence_if_wrong");
            OptionalText(item.FromLesson, 256, path + ".from_lesson");
        }
        var local = findings.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var references = 0;
        for (var i = 0; i < evidence.Length; i++)
        {
            var path = $"evidence[{i}]";
            var item = evidence[i];
            if (item is null) Fail("Evidence is required.", path);
            AddKey(keys, item!.Key, path);
            Text(item.SourceType, 128, path + ".source_type");
            Text(item.Citation, 16384, path + ".citation");
            Text(item.Summary, 32768, path + ".summary");
            var supports = References(item.Supports, local, path + ".supports");
            var refutes = References(item.Refutes, local, path + ".refutes");
            references += supports.Length + refutes.Length;
            evidence[i] = item with { Supports = supports, Refutes = refutes };
        }
        if (references > 1024) Fail("A request permits at most 1024 references.");
        var snapshot = request with { Findings = findings, Evidence = evidence };
        if (FindingsFingerprint.RequestBytes(snapshot).Length > MaximumBodyBytes)
            Fail("The normalized request exceeds 256 KiB.");
        return snapshot;
    }

    private static FindingReference[] References(IReadOnlyList<FindingReference>? items,
        HashSet<string> local, string path)
    {
        if (items is null || items.Count > 64) Fail("Expected at most 64 references per direction.", path);
        var copy = items!.ToArray();
        for (var i = 0; i < copy.Length; i++)
        {
            var item = copy[i];
            var location = $"{path}[{i}]";
            if (item is null || (item.Finding is null) == (item.ClaimId is null))
                Fail("A reference must name exactly one finding or claim_id.", location);
            if (item!.Finding is { } key)
            {
                if (!IsKey(key)) Fail("Invalid local finding key.", location);
                if (!local.Contains(key))
                    throw new FindingsRequestException("invalid_reference", $"Unknown local finding '{key}'.", location);
            }
            else Text(item.ClaimId, 256, location + ".claim_id");
        }
        return copy;
    }

    private static void AddKey(HashSet<string> keys, string? key, string path)
    {
        if (!IsKey(key) || !keys.Add(key!)) Fail("Local keys must be valid and unique across both arrays.", path + ".key");
    }
    internal static void OptionalText(string? value, int limit, string path)
    {
        if (value is not null) Text(value, limit, path);
    }
    internal static void Text(string? value, int limit, string path)
    {
        if (string.IsNullOrWhiteSpace(value)) Fail("Nonblank text is required.", path);
        var remaining = value.AsSpan();
        var count = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) != OperationStatus.Done || ++count > limit)
                Fail($"Expected valid Unicode with at most {limit} scalar values.", path);
            remaining = remaining[consumed..];
        }
    }
    private static void Fail(string message, string? path = null) =>
        throw new FindingsRequestException("invalid_request", message, path);
}
