# Prompt Contract — cybi-batch-scoped-upload

Role:
You are a senior .NET 8 engineer working in the Cymulate AgentService repo
(clean architecture, xUnit/FakeItEasy/FluentAssertions, coding standards in
`ReadMEs/coding-standards.md` — read it before writing code).

Goal:
Implement batch-scoped storage + deterministic `instanceBatchId` in the CYBI
batch upload path (`CybiBatchUploader`), mirroring the adapters repo's proven
mechanism, dormant by default, on branch `colelctors-patch_id-upload`.

Context:
- `task.md` (what/where/references), `constraints.md`, `decisions.md`,
  `assumptions.md` in this directory are binding.
- Adapters reference: `BatchScopedStorage.cs` in the adapters repo (path in
  task.md) — port `BuildBatchInstanceId` exactly; mirror the doc-comment
  discipline around ordering and determinism.
- Current uploader behavior: `CybiBatchUploader.SaveUploadAndDeleteBatchAsync`
  writes NDJSON to `{baseDirectory}/{actionId}/{fileName}`, POSTs
  `{action:"progress", stage:"batch_file", data, fileName, fileContent}` to
  `cybi/{actionId}`, deletes on success. `BuildBatchRequestPrefix` builds the
  JSON prefix. `SendCompletionAsync` sends
  `{action:"stop", stage:"collection_complete", fileName: actionId,
  isBatchUpload: true, hasData}`.

Deliverables:
1. UUID port: static helper in Infrastructure.Common with
   `BuildBatchInstanceId(string name)` / segment builder, frozen namespace
   `b584b489-7c3d-4caf-97eb-49d7ff6d78fb`, exact adapters algorithm.
2. Scoping state in `CybiBatchUploader` (implementation only): per-actionId
   run-global counter; when armed, fileName → `batch_{N:D6}/{fileName}`;
   local subdirectory created; folder-close semantics (k flushes or byte
   threshold, parameterized; k=1 degenerate case supported); batch metadata
   announced exactly once per folder (on close), including at run completion
   for the final partial folder, before the stop message.
3. Wire extension (armed path only): `metadata` object with exact consumer
   field names ({instanceOid, instanceId, clientID, integrationSettingId,
   integrationSettingFlowId, clientIntegrationFlowId, clientIntegrationId,
   storageUrl (scoped batch path), instanceBatchId}) + top-level `sequenceId`
   (monotonic) and `itemCount`.
4. Arming hook: `CybiActionManager` (executor) reads the server-driven opt-in
   from the action payload (lenient JObject peek; placeholder key name per
   A1) plus the metadata source values from `IntegrationInstanceJson`, and
   arms the uploader for the current actionId. No shared-DTO change.
5. Tests mirroring touched sources: UUID vectors (independently recomputed
   v5 values), run-global counter cross-flow non-collision, disk subfolder
   creation, armed wire shape, dormant byte-identity (exact payload
   comparison against pre-change format), folder-close-once semantics
   including completion-time close.

Constraints:
- All items in `constraints.md` apply verbatim. Highlights: interface and
  dynamically-loaded binary contracts frozen; dormant = byte-identical;
  announce-once-per-folder is mandatory (A5); thread-safe.

Success Criteria:
- `dotnet build AgentService.sln` clean; existing tests green; new tests
  green (`dotnet test`).
- Dormant-path byte-identity proven by test, not by inspection.
- UUID test vectors verified against an independent v5 computation (e.g.
  Python `uuid.uuid5` with the frozen namespace) recorded in the test as
  constants.
- Diff surface audit: only Infrastructure.Common internals, executor hook,
  DI (if needed), tests. Zero collector/Actions/Common-shared-type changes.
- `dotnet format` / coding standards conformance on touched files.

Execution Rules:
- Do not assume missing data; A1/A6 use named placeholders and are recorded
  in execution notes.
- Respect constraints strictly; on conflict STOP and surface.
- Update `state.json` step statuses and `execution_notes.md` as you go.

Output Format:
- Code + tests on the current branch (no commits unless instructed).
- `execution_notes.md` appended with: files touched, placeholder keys chosen,
  deviations (should be none), and remaining risks.

Stop Conditions:
- Goal achieved (all success criteria demonstrably met), or
- A constraint cannot be satisfied without violating the frozen contracts, or
- Required data missing beyond A1/A6 placeholders.
