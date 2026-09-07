- Umbrella name is `DataPipeline` (literal, capital D and P, single word).
- Three sub-folders under `DataPipeline/`: `Ingress/`, `Json/`, `Egress/`.
- `Publishing` is **retired**, not relocated. `Egress` is a new identity that takes its place under the `DataPipeline` umbrella. `Json` is rehomed but keeps its name.
- `Session/`, `Recovery/`, `Orchestration/` are control-plane and remain top-level in `Shared/`. Not in scope.
- One PR, two commits: Commit 1 = retire `Publishing/` and rehome `Json/`, propagate namespace renames + identity-prefix type renames; Commit 2 = `Ingress/` placeholder + `DataPipeline/README.md`.
- Boundary rule for `DataPipeline/`: it contains code that processes data records themselves; control or metadata about records belongs elsewhere. Written into the README on day one to forestall future scope creep.

## Namespace renames (complete)

- `Cymulate.Integration.Adapters.Shared.Publishing` → `Cymulate.Integration.Adapters.Shared.DataPipeline.Egress`
- Every sub-namespace under `Publishing.*` (e.g., `Publishing.Ndjson`, `Publishing.Multipart`, `Publishing.Telemetry`) becomes the matching `DataPipeline.Egress.*` namespace.
- `Cymulate.Integration.Adapters.Shared.Json` → `Cymulate.Integration.Adapters.Shared.DataPipeline.Json`.

## Type-name harmonization

The rule: rename types whose name carries the **dying identity prefix** `Publish*` as a structural marker for the old folder. Keep names where `Publish` is a verb or `Publisher` is the role-noun describing what the type does.

**Renamed (drop the `Publish` prefix — namespace provides context):**
- `PublishThrottlingOptions` → `ThrottlingOptions`
- `PublishBufferingOptions` → `BufferingOptions`
- `PublishMemoryPressureOptions` → `MemoryPressureOptions`

**Kept as-is (verb forms and role nouns remain accurate under Egress):**
- `PublishResult` (result of a publish action — verb form)
- `CollectorNdjsonPublisher` (role noun — the thing that publishes)
- `ResultsBatchPublisher` (role noun)
- `NdjsonBatchSession`, `NdjsonUtf8BatchSession`, `Utf8ResultsStreamWriter`, `Utf8ResultsRecordFormatter`, etc. (no Publishing-identity in the name)
- All `Publish*Async` method names (verb forms describing the action: `PublishFindingsUtf8PageAsync`, `PublishAssetsUtf8PageAsync`, `PublishPageAsync`, `PublishUtf8Async`, `PublishAsync`)

## Out-of-scope renames

- SDK-owned `IAdapterDataPublisher` interface and its members (`PublishStreamAsync`, `InitiateMultipartUploadAsync`, etc.) live under `Cymulate.Integration.Sdk.Contracts` and are external to this repo's data plane. Do not touch.
- No new abstractions, helpers, or public types introduced in this task. Signage and restructure only.

## Workstream sequencing

- Cortex XDR XQL streaming work and `va_endpoints` work are deferred to separate, sequential tasks. They will land their first artifacts inside `DataPipeline/Ingress/` once this restructure is merged.
- Existing READMEs inside the moved folders move with their folders. Each gets a one-line cross-link added pointing at the new `DataPipeline/README.md`.
- The two-commit structure is non-negotiable: Commit 1 stands alone as a no-op rename so reviewers can verify "no behavior change" without README content in the diff. Commit 2 introduces the new README and the empty `Ingress/` folder.
