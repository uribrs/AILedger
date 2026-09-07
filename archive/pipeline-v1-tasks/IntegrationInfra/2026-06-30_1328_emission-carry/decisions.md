# Decisions (operator-fixed — inputs, not to be re-debated)

- **D1:** Emission = `DataPipeline/Egress/*` + `Glossary/CollectorGlobalDefaults`. Carry verbatim on logic.
- **D2:** Charter — "deliver produced records to their destination as bounded, validated batches"; depends
  on Kernel + SDK. The engine is the SINGLE authoritative JSON validator — preserve that invariant.
- **D3:** Naming not bound by verbatim (precedent: faultgovernance-carry D8). Neutralize the 3 generic-infra
  Collector* type names → Adapter* (`CollectorGlobalDefaults`/`CollectorOutputDefaults`/`CollectorNdjsonPublisher`).
  Wire-safe; held values unchanged. Headline naming decision flagged for PR review.
- **D4:** RESHAPE DEFERRED — the charter's ISink seam (StreamKind), killing the static entry + assets/findings
  hardwiring (DIP/OCP), is a reshape requiring an operator decision. Carry verbatim now; flag as candidate.
- **D5:** Verbatim relocation, namespace-only rewrite, source reference-only — same spirit as prior carries.
- **D6 (standing — every carry):** SOLID separation, DRY, excellent XML docs, documentation, tests.

- **D7 (operator, post-review):** Fix H1 (leaked multipart upload on cancel/dispose) in IntegrationInfra
  ONLY — a deliberate, sanctioned divergence from the verbatim source (operator: "fix it here only").
  Both `NdjsonBatchSession` and `NdjsonUtf8BatchSession` gain a `_multipartFinalized` flag (set on
  successful complete and after a catch-abort); `DisposeAsync` aborts an un-finalized initiated multipart
  with `CancellationToken.None`. Source repo NOT changed. This is the only intentional behavior change in
  the carry; everything else remains verbatim.
