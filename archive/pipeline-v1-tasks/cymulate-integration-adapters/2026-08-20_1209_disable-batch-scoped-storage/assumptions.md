# Assumptions

- A1 — VALIDATED — Actor: verifier. Citation: the base-ref production scan at `0c61fad68ea4add59784cb9294737c880d8676fc` found exactly the InsightVM Cloud default, Qualys default, and TenableIo correlated emitter call (plus TenableIo's comment quoting that call); `research/internal-recon.md` records the same three paths.
- A2 — VALIDATED — Actor: verifier. Citation: current production source sets InsightVM Cloud and Qualys defaults and the TenableIo emitter argument to `false`; repair-cycle verifier runs passed InsightVM Cloud 21/21, Qualys 28/28, and TenableIo 154/154, including scoped-only legacy resume normalization to flat page-8 paths with stale identity removal for all four publishing lanes.
- A3 — VALIDATED — Actor: verifier. Citation: `InsightVmCloudBatchScopedStorageTests.cs` and `QualysBatchScopedStorageTests.cs` assert disabled configuration defaults while retaining explicit `true` capability tests; repair-cycle verifier project runs passed (21/21 and 28/28), and TenableIo passed 154/154.

## Prior Art

No prior art found for tags: `cymulate-integration-adapters`, `batch-scoped-storage`, `collector-emission`, `storage-layout`.
