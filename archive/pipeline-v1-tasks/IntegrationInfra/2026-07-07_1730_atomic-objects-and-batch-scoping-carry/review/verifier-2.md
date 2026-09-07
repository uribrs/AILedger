# Verifier-2 — post-repair-round-1 confirm pass

**VERDICT: PASS** (whole task, post-repair). One immaterial doc-nit residual noted; no code impact.

Narrow-scope re-verification of repair round 1 (test-only rework + doc-count fixes) on branch
`carry/atomic-objects-and-batch-scoping` (uncommitted). verifier-1's full parity findings still stand
because no production code changed this round (item 2).

---

| # | Item | Result | Evidence |
|---|------|--------|----------|
| 1 | Six critical facts parameterized over BOTH paths; String routes to the string session; byte-identity on string path | PASS | see below |
| 2 | No production code changed this round (tests + execution_notes only) | PASS | all `src/` .cs + csproj + README mtimes 17:4x–17:57 (orig carry); only `AtomicStreamedObjectsTests.cs` (23:50) + `execution_notes.md` (23:51) touched at repair time |
| 3 | Build + `dotnet test` → 194/194, Emission 49 | PASS | build 0 errors; 37+20+11+15+31+31+**49** = **194**, 0 failed |
| 4 | Two LOW doc-count fixes applied in execution_notes | PASS (1 residual) | breakdown corrected to 15+14+14=43 → superseded to 15+20+14=**49**; exempt-hit count corrected (residual below) |

## Item 1 — parameterization is genuine, not utf8-aliased
The six state-machine facts are now `[Theory]` with `[InlineData(PublishMode.String)]` +
`[InlineData(PublishMode.Utf8)]`:
`GrowingCall_EscalatesToMultipart` (escalation + atomic complete), `MidStreamPartFailure_AbortsExactlyOnce`,
`Cancellation_MidStream`, `DisposeWithoutComplete`, `CompleteFailure_AbortsExactlyOnce`,
`MultipartEndingOnPostAppendFlush` (post-append-flush finalization). (`AtomicStreamedObjectsTests.cs:156,221,250,283,316,350`)

**Routing check (the flagged risk):** the `Publish` dispatcher (`:82-92`) sends
`PublishMode.String → ResultsBatchPublisher.PublishAsync(..., Decode(records), ...)` and
`PublishMode.Utf8 → ResultsBatchPublisher.PublishUtf8Async(...)`. Per verifier-1,
`PublishAsync → PublishCoreAsync → new NdjsonBatchSession` and
`PublishUtf8Async → PublishUtf8CoreAsync → new NdjsonUtf8BatchSession`. So the String variants genuinely
drive the **string session** (its own FinalizeAsync / CompleteStartedMultipartAsync / TryAbortMultipartAsync
/ CommitIncomplete) — not utf8 under an alias. `Decode` (`:94-100`) only adapts the byte source to the
string overload; filler records are ASCII JSON so string-normalize is byte-count-identical to utf8 (same
part boundaries — the shared count/size assertions are valid for both modes).

**Byte-identity on the string path:** `SmallCall_String_...ByteIdenticalContentAndPath` (`:104-126`) drives
`PublishAsync` and asserts `Encoding.UTF8.GetString(SinglePayloads.Single()) == "{\"a\":1}\n{\"a\":2}\n{\"a\":3}\n"`.

Count math: Atomic file = 8 `[Fact]` + 6 `[Theory]`×2 = 20 cases (+6 over the pre-repair 14). Emission
43 → 49; grand total 188 → 194. Confirmed by the run.

---

## ACCEPTED RESIDUALS (consolidated — carried by decision, not defects)
- **CommitIncomplete defensive belt** — the `ResultsBatchPublisher` gate that throws rather than report
  success for a started-but-uncommitted multipart. This is source-faithful (changeset B) and belt-and-
  suspenders over the session's own completion; kept by decision.
- **Three wire-contract `storageUrl` literals** — `AdapterRunMetadata`/`AdapterEventMetadata`
  `[JsonPropertyName("storageUrl")]` and `Job/AdapterRunEnvelopeParser` payload read (plus the
  `AdapterPlatformEventFactory` value-read whose KEY uses the const). Wire contract, deliberately literal;
  matches source.
- **O1 — oversized single record holds in memory** — with the size gate removed, a giant NDJSON record is
  materialized in the in-memory batch before it streams. Inherent to one-line NDJSON + the streamed path;
  surfaced by `SoftRecordWarningBytes`; documented in README/ThrottlingOptions. Accepted.
- **O2 — `StripBatchSegment` false-positive window** — a real path segment named exactly `batch_` + six
  digits would be stripped. The deterministic tail can only originate from `BeginPage`, documented in the
  BSS XML doc, and pinned by tests (`BeginPage_DoesNotStripNonBatchTails` keeps `batch_00000X`;
  `BeginPage_WhenStorageUrlRoundTripsAlreadyScoped_StripsTheBatchTail` strips a real scoped tail). Accepted.

---

## Residual doc-nit (non-blocking, no code impact)
The LOW-2 fix corrects the exempt `_multipartFinalized` count to "4 lines (1 execution_notes + 3
decisions)" for the emission-carry task dir — accurate for that dir — but omits one further exempt line in
`ai/reviews/full-repo-2026-07-01/reviewer-1.md:115` (a dated point-in-time review describing old D7
behavior). True exempt total = 5 lines. Operative criterion is unaffected: **zero `_multipartFinalized`
in any production `.cs`** (re-confirmed) and none in tests. Not worth another round.

## Re-confirmed invariants (production unchanged ⇒ verifier-1 parity holds)
- 0 `_multipartFinalized` in `src/**/*.cs`; build 0 errors; 194/194 green.
- No new Conducting→Emission edge; BSS SDK-only in Envelopes/Common; Version 1.0.0-preview.3.
