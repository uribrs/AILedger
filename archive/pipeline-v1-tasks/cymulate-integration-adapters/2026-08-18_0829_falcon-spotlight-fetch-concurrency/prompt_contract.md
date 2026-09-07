# Prompt Contract

## Role

You are a senior .NET engineer working on the Cymulate integration adapters, specialising in
concurrent data pipelines with durable resume semantics.

## Goal

Make the Falcon correlated-findings flow scroll Spotlight for N AID batches concurrently, feeding a
bounded buffer that a single consumer drains to publish objects in unchanged order, with N supplied by
configuration.

## Context

- Worktree: `/Users/user/Dev/cymulate-integration-adapters-falcon-concurrency`
- Branch: `falcon-concurrency-and-server-side-retries-with-backoff`, baseRef `1f7a2ba6`
- Primary file: `src/.../Collectors/FalconCollector/Flows/Findings/FalconFindingsFlow.cs`, the
  `await foreach (StagedAidBatch batch in FalconFrozenKeyList.EnumerateAsync(...))` loop (~line 249)
- Per-batch scroll: `Flows/Findings/Correlated/FalconSpotlightBatchScroller.EmitBatchRecordsAsync`
- Config: `Processing/Configuration/FalconCollectorConfiguration.cs` and
  `FalconCollectorConfigurationBuilder.ExtractFields`
- Reference implementation for parallel-collect/serial-publish: the TenableIo correlated flow, in the
  **main checkout's uncommitted working tree** — read-only, never write there.
- The `FalconCollectorConfigurationBuilder` working-tree change (User-Agent `ConfigureClient`) is
  pre-existing context and not this task's work.

The fan-out location is already decided and evidenced in `decisions.md`. Do not re-litigate it; if
implementation contradicts it, stop and report rather than quietly relocating the parallelism.

## Constraints

See `constraints.md` — all of it binds. The load-bearing ones:

- Publish + checkpoint + `AdvancePage` on one consumer, in frozen-key-list order, with nothing awaitable
  between publish and checkpoint.
- Checkpoint format stays v4.
- Concurrency degree from configuration, clamped in `ExtractFields`; degree 1 reproduces today exactly.
- Bounded buffer only.
- Do not parallelise inside the Spotlight scroll — it is a cursor chain.
- Do not modify `Cymulate.IntegrationInfra` or `Cymulate.Http.Package` without written justification.
- `AidBatchSize` stays 4. No rate-limit header work.

## Success Criteria

1. Several Spotlight scrolls are demonstrably in flight at once, bounded by the configured degree.
2. Published object order, content, and object naming are identical to the sequential implementation for
   the same input.
3. Publish → checkpoint adjacency is preserved; a test fails if an await is introduced between them.
4. Concurrency degree is read from configuration, clamped, and documented with its default justified.
5. Degree 1 is proven by test to reproduce sequential behaviour.
6. Cancellation with N in flight is explicitly specified and tested: the run yields via
   `RecordCooperativeYield`, and resume re-does every unpublished in-flight batch, skipping none.
7. Peak in-flight memory is bounded by the configured degree and stated in `execution_notes.md` as a
   formula against observed batch size.
8. `dotnet build` clean with 0 warnings; the FalconCollector test project passes in full.
9. Every assumption A1–A9 in `assumptions.md` is disposed by the verifier with an actor and a citation.

## Execution Rules

- Do not assume missing data. Where a number is needed and not evidenced, state it as an assumption and
  pick the conservative value.
- Respect constraints strictly.
- Prefer the platform's built-in bounded producer/consumer primitives over a hand-rolled buffer.
- Preserve existing log lines that act as operational receipts; add fields rather than reshaping them.
- If measurement against the TenableIo local run is not achievable, say so and choose a conservative
  default rather than reporting an unmeasured number as measured.

## Output Format

- Code changes in the worktree.
- `execution_notes.md`: what changed and why, the memory formula, the chosen default degree and its
  basis, and the cancellation semantics as implemented.
- Test results verbatim — build output and test summary, not a paraphrase.

## Stop Conditions

- Goal achieved and all success criteria met.
- The serial-publish or v4-checkpoint constraint cannot be held without an Infra change — stop and report
  with the specific blocking API.
- Restore fails on `Cymulate.*` with 401/403 — report as CodeArtifact auth, do not work around it.
- Tests that passed at baseRef begin failing and the cause is not understood.
