# Decisions

- D1: Preserve YAML as the domain language. No YAML migration or TPL-shaped YAML is introduced.
- D2: Use a compile/execute architecture: existing parse + validation -> immutable domain plan -> sequential executor -> existing services and sinks.
- D3: Express domain nodes around existing concepts: sequence, operation, poll, capture, foreach, publication, and streaming merge composition. Exact class names remain implementation-local.
- D4: Enforce strict sequentiality as an invariant across the full execution, not merely `MaxDegreeOfParallelism = 1` on individual blocks.
- D5: Keep an operation cohesive in the first design. Request/auth, pagination, error handling, mapping, hydration, and sink interaction remain its internal pipeline unless later evidence justifies extraction.
- D6: Model completion with typed outcomes, including durable defer and cancellation. Exceptions remain for unexpected faults, not ordinary workflow control.
- D7: Preserve Shared boundaries: YamlCollector bridges host orchestration/session/publication/recovery; the standalone engine remains host-agnostic.
- D8: Preserve predecessor streaming-merge semantics and treat that implementation as current baseline, even though its branch work is presently uncommitted.
- D9: Migrate incrementally behind an internal seam, characterize first, cut over only after direct-operation and workflow parity, then remove the legacy runner.
- D10: Do not add DAG scheduling, parallel foreach, or independent stage concurrency; current bank evidence does not justify them.
- D11: Resolve checkpoint/fingerprint behavior as part of the architecture, not as incidental runner state. Shared progress and engine cursor state must restart coherently.
- D12: Use the five strongest native/YAML matches as semantic anchors: Defender VM, Tenable.io, CrowdStrike Falcon, InsightVM Cloud, and Qualys.

## Decisions required before implementation

- DR1: Approve Dataflow specifically, or choose plain TPL primitives behind the same domain executor contract.
- DR2: Name the canonical engine home and synchronization strategy for the second repository.
- DR3: Approve the temporary migration seam and its selection mechanism (test-only, configuration, or branch-level cutover).
- DR4: Approve checkpoint compatibility/versioning policy after Phase 0 characterization.

