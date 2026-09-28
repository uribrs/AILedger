using System.Text;
using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Findings;

namespace AILedger.Tests.Findings;

public sealed class FindingsContractTests
{
    private static readonly JsonSerializerOptions Wire = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact]
    public void FingerprintMatchesFrozenAsciiVector()
    {
        var binding = new FindingsBinding(new("demo"), new("researcher"), new RunId("R1"), "R1");
        var request = new FindingsRequest(1, "research-0001",
            [new("f1", "The failed run retained its checkpoint.", "A retry may repeat work.")],
            [new("e1", "test-run", "fixture://checkpoint/failed-run",
                "Checkpoint survived. Literal data: $HOME | `probe`\nSecond line.", [new(Finding: "f1")], [])]);
        Assert.Equal("e5fd2c4239111e6f5eb9c38b64ffa23042a4c9dbcbcb5a5c556ebe576a8d5fd3",
            FindingsFingerprint.Compute(binding, FindingsValidation.Snapshot(request)));
    }

    [Fact]
    public void CanonicalUnicodeEscapingAndNullVectorIsExplicit()
    {
        var binding = new FindingsBinding(new("t"), new("a"), null, "stable");
        var request = new FindingsRequest(1, "ignored-key", [new("f", " café שלום 😀 \"$`|\"\nline\r\ntwo ")], []);
        const string expected = """
        {"operation":"record_findings","schema_version":1,"binding":{"task_id":"t","actor_id":"a","run_id":null,"correlation_id":"stable","causation_id":null},"findings":[{"key":"f","statement":" café שלום \uD83D\uDE00 \"$`|\"\nline\r\ntwo ","consequence_if_wrong":null,"from_lesson":null}],"evidence":[]}
        """;
        Assert.Equal(expected, Encoding.UTF8.GetString(FindingsFingerprint.CanonicalBytes(binding, request)));
        Assert.Equal("dfccd2b3877d9fa09d173f61ed25c64284a57fc5608e96ccd29bbdadfde14f59", FindingsFingerprint.Compute(binding, request));
        Assert.Equal(FindingsFingerprint.Compute(binding, request),
            FindingsFingerprint.Compute(binding, request with { RequestId = "different-key" }));
    }

    [Fact]
    public async Task EquivalentEscapesPropertyOrderAndOptionalNullsProduceTheSameReceipt()
    {
        var a = JsonSerializer.Deserialize<FindingsRequest>("""
        {"schema_version":1,"request_id":"x","findings":[{"key":"f","statement":"café"}],"evidence":[]}
        """, Wire)!;
        var b = JsonSerializer.Deserialize<FindingsRequest>("""
        {"evidence":[],"findings":[{"from_lesson":null,"consequence_if_wrong":null,"statement":"caf\u00e9","key":"f"}],"request_id":"x","schema_version":1}
        """, Wire)!;
        var binding = new FindingsBinding(new("t"), new("a"), null, "s");
        Assert.Equal(FindingsFingerprint.Compute(binding, FindingsValidation.Snapshot(a)),
            FindingsFingerprint.Compute(binding, FindingsValidation.Snapshot(b)));
        using var fixture = new FindingsFixture();
        await fixture.OpenAsync();
        var committed = await fixture.RecordAsync(a);
        var retry = await fixture.RecordAsync(b);
        Assert.Null(committed.Error);
        Assert.True(retry.Replayed);
        Assert.Equal(committed.Receipt!.TransactionId, retry.Receipt!.TransactionId);
        var composed = b with { Findings = [new("f", "cafe\u0301")] };
        Assert.NotEqual(FindingsFingerprint.Compute(binding, b), FindingsFingerprint.Compute(binding, composed));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("schema")]
    [InlineData("request-id")]
    [InlineData("request-newline")]
    [InlineData("key-newline")]
    [InlineData("key")]
    [InlineData("duplicate-key")]
    [InlineData("null-array")]
    [InlineData("null-item")]
    [InlineData("null-text")]
    [InlineData("blank-optional")]
    [InlineData("surrogate")]
    [InlineData("too-long")]
    [InlineData("too-many-findings")]
    [InlineData("too-many-evidence")]
    [InlineData("too-many-direction")]
    [InlineData("too-many-references")]
    [InlineData("body-cap")]
    [InlineData("null-direction")]
    [InlineData("null-reference")]
    [InlineData("double-reference")]
    [InlineData("empty-reference")]
    public async Task InvalidTypedRequestsNeverMutateCanonicalState(string defect)
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var r = FindingsFixture.Request();
        var e = r.Evidence[0];
        r = defect switch
        {
            "empty" => r with { Findings = [], Evidence = [] },
            "schema" => r with { SchemaVersion = 2 },
            "request-id" => r with { RequestId = "bad id" },
            "request-newline" => r with { RequestId = "key\n" },
            "key-newline" => r with { Findings = [new("f\n", "Text")] },
            "key" => r with { Findings = [new("9bad", "Text")] },
            "duplicate-key" => r with { Evidence = [e with { Key = "f1" }] },
            "null-array" => r with { Findings = null! },
            "null-item" => r with { Findings = [null!] },
            "null-text" => r with { Findings = [new("f1", null!)] },
            "blank-optional" => r with { Findings = [new("f1", "Text", " ")] },
            "surrogate" => r with { Findings = [new("f1", "bad\ud800")] },
            "too-long" => r with { Findings = [new("f1", new string('x', 8193))] },
            "too-many-findings" => r with { Findings = Enumerable.Range(0, 33).Select(i => new FindingInput($"f{i}", "Text")).ToArray() },
            "too-many-evidence" => r with { Evidence = Enumerable.Range(0, 65).Select(i => e with { Key = $"e{i}" }).ToArray() },
            "too-many-direction" => r with { Evidence = [e with { Supports = Enumerable.Repeat(new FindingReference(Finding: "f1"), 65).ToArray() }] },
            "too-many-references" => r with { Evidence = Enumerable.Range(0, 17).Select(i => e with { Key = $"e{i}",
                Supports = Enumerable.Repeat(new FindingReference(Finding: "f1"), 64).ToArray() }).ToArray() },
            "body-cap" => r with { Evidence = Enumerable.Range(0, 9).Select(i => e with { Key = $"e{i}", Summary = new string('x', 32768) }).ToArray() },
            "null-direction" => r with { Evidence = [e with { Supports = null! }] },
            "null-reference" => r with { Evidence = [e with { Supports = [null!] }] },
            "double-reference" => r with { Evidence = [e with { Supports = [new("f1", "C1")] }] },
            _ => r with { Evidence = [e with { Supports = [new()] }] }
        };
        var before = await File.ReadAllBytesAsync(f.EventsPath);
        var result = await f.RecordAsync(r);
        Assert.Equal("invalid_request", result.Error?.Code);
        Assert.Equal("unavailable", result.CollectionStatus);
        Assert.Equal(before, await File.ReadAllBytesAsync(f.EventsPath));
        Assert.False(File.Exists(Path.Combine(f.Directory, "refusals.jsonl")));
    }

    [Fact]
    public async Task UnknownLocalReferenceIsSeparateFromKernelReferenceRefusal()
    {
        using var f = new FindingsFixture();
        await f.OpenAsync();
        var r = FindingsFixture.Request();
        r = r with { Evidence = [r.Evidence[0] with { Supports = [new(Finding: "missing")] }] };
        var result = await f.RecordAsync(r);
        Assert.Equal("invalid_reference", result.Error?.Code);
        Assert.Equal("evidence[0].supports[0]", result.Error?.ItemPath);
        Assert.Empty((await f.StateAsync()).Claims);
    }

    [Fact]
    public void LimitsCountUnicodeScalarsAndSnapshotDoesNotRetainCallerArrays()
    {
        var array = new[] { new FindingInput("f", string.Concat(Enumerable.Repeat("😀", 8192))) };
        var request = new FindingsRequest(1, "key", array, []);
        var snapshot = FindingsValidation.Snapshot(request);
        array[0] = new("other", "Changed");
        Assert.Equal("f", snapshot.Findings[0].Key);
        Assert.Equal(16384, snapshot.Findings[0].Statement.Length);
    }
}
