# Constraints

- Subfolder naming is deterministic: `batch_{page:D6}` derived from the page number. Never random (crash-resume must overwrite the same keys; bucket is never purged).
- The pristine base StorageUrl is preserved; every subfolder path derives from base. `base/uuidA/uuidB`-style stacking is a defect.
- `progressContext.Metadata["storageUrl"]` is the only vehicle: Egress sessions read it live per flush, and the host echoes it into progress events. Do not invent a second channel.
- Ordering invariant: set subfolder → publish page → `AdvancePage`/checkpoint → only then may the next page's subfolder be set. Must be locked by a test.
- Dud pages (0 records): no upload, no subfolder; `Metadata["storageUrl"]` holds the bare base URL when the page's event fires.
- DONE/failure/partial-success events carry the bare base URL — restore base before completion/failure publication, including `AdapterFailureDecisionExecutor`'s `AdvancePage(0,0)` snapshot path.
- Checkpoint format and `AdapterCheckpoint` contents unchanged.
- Opt-in mechanism only; default behavior for all collectors must be byte-identical to today. No collector is wired in this pass.
- YamlCollector path (`context.PublishAsync`, bus-side prefixing) untouched.
- Shared/README boundary rules: Egress owns target naming/upload; do not move sequencing logic into Egress or record logic into Orchestration.
- Repo conventions: net8.0 (no `-f` override), central package versions, xUnit + Moq + FluentAssertions, `InternalsVisibleTo` for internals, small methods mirroring neighboring style.
- Update `ai/skills/` collector docs if the mechanism changes documented egress behavior.
