# Verifier Report — instance-batch-id-uuid

Verifier: verifier-1 (read-only; no dotnet commands run per instruction — test evidence taken from execution_notes.md and cross-checked for plausibility against source)
Date: 2026-07-08
Branch: batchful-uploads-uuid, diffed against origin/dev

## Verdict: PASS

All contract Success Criteria verified. One non-blocking bookkeeping note (state.json top-level status stale — see Drift section). No code gaps.

## Per-Criterion Table

| # | Criterion | Result | Evidence |
|---|-----------|--------|----------|
| 1 | Diff scope: only BatchScopedStorage.cs, Egress/README.md, 3 test files (+ task files) | PASS | `git diff origin/dev --name-only` returns exactly the 5 allowed source files. `ai/active/` is gitignored (`.gitignore:9`), so task files are correctly outside the tracked diff. No Directory.Build.props / csproj / checkpoint / folder-name changes anywhere in the diff. |
| 2a | Path logic unchanged vs origin/dev | PASS | Diff hunks in `BatchScopedStorage.cs` touch only: usings, doc comments, the new `BatchIdNamespace` constant, the new `BuildBatchInstanceId` method, and the single `InstanceBatchIdMetadataKey` write in `BeginPage` (line 94). `BuildBatchSegment`, `StripBatchSegment`, `ResolveBaseUrl`, `RestoreBase`, and the `storageUrl`/`baseStorageUrl` writes (lines 89, 93) are byte-identical. |
| 2b | RFC 4122 v5 correctness | PASS | SHA-1 over big-endian namespace (`TryWriteBytes(input, bigEndian: true, out _)`) + UTF-8 name; `hash[6] = (hash[6] & 0x0F) \| 0x50` (version 5); `hash[8] = (hash[8] & 0x3F) \| 0x80` (variant 10x); `new Guid(hash[..16], bigEndian: true)`. Both .NET 8 APIs exist: `Guid.TryWriteBytes(Span<byte>, bool bigEndian, out int)` and `Guid(ReadOnlySpan<byte>, bool bigEndian)` were added in .NET 8 and are used with correct arguments. `Guid.ToString()` yields lowercase "D" format. End-to-end correctness independently confirmed by golden-vector recompute (2c) and the recorded green build/tests. |
| 2c | Derivation input is pristine base URL | PASS | `BeginPage` line 94 calls `BuildBatchInstanceId(baseUrl, pageNumber)` where `baseUrl` is the `ResolveBaseUrl` result (line 83, post-strip/post-trim) — never the live scoped `Metadata["storageUrl"]`. |
| 3 | Frozen namespace doc + golden-vector test | PASS | `BatchIdNamespace` (line 63) doc-commented "Frozen forever: changing it re-mints the id of every batch ever announced". Test `BuildBatchInstanceId_MatchesRfc4122V5GoldenVector` pins `a9a3292a-43fe-56d8-8626-fcbd67059ccd` for (BaseUrl `s3://cybi-data/stg/raw-data/tenant/setting/run`, page 3). Independently recomputed with `python3 uuid.uuid5(UUID('b584b489-7c3d-4caf-97eb-49d7ff6d78fb'), 's3://cybi-data/stg/raw-data/tenant/setting/run/batch_000003')` → exact match. |
| 4 | Determinism pins in Shared suite | PASS | Same-inputs equality + Guid parseability + lowercase: `BuildBatchInstanceId_IsDeterministic_AndParseable` (lines 181–189). Resume round-trip with scoped URL as incoming storageUrl: `BeginPage_SamePageAfterResume_AnnouncesTheSameBatchId` (lines 201–213, afterResume seeded with `{BaseUrl}/batch_000007`). Cross-page + cross-base inequality: `BuildBatchInstanceId_DiffersAcrossPagesAndBases` (lines 192–198). RestoreBase key removal: pre-existing coverage located — Shared `RestoreBase_AfterDudPage_...` line 142/146 (`batchIdAtEventTime.Should().BeFalse`), plus Qualys line 77 and InsightVmCloud line 112 (`NotContainKey`). |
| 5 | Test evidence covers 4 suites, counts plausible | PASS | execution_notes.md records: Shared BatchScopedStorageTests 18/18 (file has exactly 18 `[Fact]`s — counted), Qualys 3/3 (3 `[Fact]`s), InsightVmCloud 4/4 (4 `[Fact]`s), AdapterFailureDecisionExecutorTests 19/19 (19 `[Fact]/[Theory]`, 0 InlineData — located in DummyCollector.Test; notes correctly flag class-filtered run per no-sweep policy). All counts match the files as written. Build recorded 0 warnings / 0 errors. |
| 6 | Missed consumers of InstanceBatchIdMetadataKey | PASS | Repo-wide grep (`instanceBatchId\|InstanceBatchId`, excluding .git/artifacts/bin/obj and the task dir) hits only the 5 changed files. No stale literal `batch_NNNNNN` assertions on the key remain anywhere. ISB/consumer repos are external and out of scope (constraints.md), covered by the OPEN assumption. |

## Gaps / Risks

- **No code gaps.**
- Residual risk (pre-existing, flagged, non-blocking): OPEN assumption in assumptions.md — ISB end-to-end delivery of the `instanceBatchId` metadata key (local ISB clone's `MessageMetadata.FromDictionary` appeared to drop unknown keys). Explicitly out of scope per constraints.md; correctly escalated to ISB/consumer owners rather than worked around.
- Test evidence is recorded, not re-run (per verifier instruction). Plausibility fully cross-checked; the golden vector — the strongest single pin — was independently recomputed and matches.

## Drift from Contract

- **state.json top-level fields stale** (bookkeeping only): `status: "contract_ready"`, `currentPhase: "contract_design"`, `requiredFiles["execution_notes.md"]: "pending"`, `lastUpdated: 17:35` — all predate execution, while `steps` S1–S6 are correctly marked complete and `skillsRun` records contract-driven-execution at 17:48. The contract's execution rule ("update state.json step statuses") was satisfied; the phase/status fields are orchestration-owned and should be advanced when verification is recorded.
- constraints.md phrased the endianness requirement as "Guid mixed-endianness swapped before hashing and after"; the implementation instead uses .NET 8's `bigEndian: true` APIs, which produce the identical network-byte-order layout without manual swapping. Equivalent in effect, explicitly noted in execution_notes.md — not a violation of constraint intent (golden vector proves the byte order is right).
- No scope creep: no version bumps, no checkpoint changes, no folder-name changes, no extra files.
