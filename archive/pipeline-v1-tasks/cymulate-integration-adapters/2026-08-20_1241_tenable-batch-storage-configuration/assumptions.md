# Assumptions

- A1 — VALIDATED — TenableIo has one collector configuration record and the existing correlated-flow construction seam carries its flag to the publisher. actor: verifier; citation: `TenableIoCollectorConfiguration.cs:89`, `TenableIoCorrelatedFindingsFlow.cs:85-90`, `TenableIoCorrelatedBatchPublisher.cs:41-55`, focused verification 3/3.
- A2 — VALIDATED — The default-false property and existing record/builder conventions require no parser or mapper change for the requested configuration-owned default/override behavior. actor: verifier; citation: `TenableIoCollectorConfigurationBuilderTests.cs:10-23`, `TenableIoCorrelatedFlowTests.cs:117-145`, focused verification 3/3.
- A3 — VALIDATED — Default execution remains flat while explicit `true` remains scoped, without recovery or storage-mechanism redesign. actor: verifier; citation: `TenableIoCollectorTests.cs:521-526`, `TenableIoCorrelatedFlowTests.cs:599-609,793-824`, full TenableIo verification 153/153, single-node solution build 0 warnings/errors.

## Prior Art

No prior art found for tags: `cymulate-integration-adapters`, `tenable-io`, `batch-scoped-storage`, `configuration-wiring`.
