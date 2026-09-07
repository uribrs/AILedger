# Assumptions

- A1 (VALIDATED): The YAML vocabulary must remain unchanged. The user explicitly requires the YAML bank and its language to remain intact.
- A2 (VALIDATED): “No parallelism” means strict global sequentiality within one collector execution, including no overlap between otherwise single-threaded Dataflow blocks.
- A3 (VALIDATED): The useful block boundaries are the YAML domain concepts; TPL is the execution substrate, not the model presented to YAML authors.
- A4 (VALIDATED): Direct operation execution is the dominant workload; only `defender-vm.yaml` and `tenable.io.yaml` currently declare workflows in the surveyed bank.
- A5 (VALIDATED): Existing predecessor work for declarative enrichment and streaming merge is part of the baseline and must be preserved.
- A6 (OPEN): `System.Threading.Tasks.Dataflow` is acceptable as a dependency in both engine homes. Confirm package availability, version ownership, trimming/deployment implications, and whether plain TPL primitives would meet the same structural goal with less machinery.
- A7 (OPEN): A temporary legacy/new executor selection seam is acceptable during migration. It must never execute both publishing paths for one run and must be removed at cutover.
- A8 (OPEN): The canonical source of truth for the duplicated engine is this adapter repository. `cymulate-magic-integration/Platform.Integrations.Sdk` currently contains a drifting copy; resolve ownership before cross-repository implementation.
- A9 (OPEN): Existing checkpoint payloads can be extended compatibly. If not, define explicit checkpoint versioning and safe restart behavior before cutover.
- A10 (OPEN): Operation-level cohesion is the correct first migration boundary. Split finer-grained internals only if characterization exposes a real reuse boundary.
- A11 (OPEN): The complete deployed YAML population is represented by the surveyed bank. If production definitions exist elsewhere, add them to the compatibility corpus before cutover.

