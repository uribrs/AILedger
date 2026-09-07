# Constraints

- The 279 surveyed YAML definitions remain unchanged. Their existing vocabulary, defaults, validation behavior, and observable meaning are the compatibility contract.
- TPL/TPL Dataflow is an internal execution mechanism, not a YAML vocabulary and not a public engine contract.
- No parallelism: within one collector execution there is at most one active operation/block delegate, request, transformation, publication, or checkpoint transition at a time. No pipeline overlap or prefetch.
- Preserve adapter public APIs, Shared subsystem ownership, ISB host contracts, output/S3 naming, progress semantics, recovery budgets, and done-event payload shape unless a separately approved contract changes them.
- Keep the standalone YAML engine free of Cymulate/Shared references. Adapter bridges continue to own Shared integration.
- Preserve predecessor streaming-merge behavior: per-page enrichment and publication, decorator chaining order, cursor resume, and no held whole-run replay.
- Preserve all live uncommitted work on `feature/yaml-engine-declarative-enrichment`; no reset, checkout-overwrite, or reconstruction from cached context.
- Read current source before every implementation phase; file/line assumptions in earlier analysis are non-authoritative.
- No vendor names or vendor-conditional branches in engine production code. Vendor-matched YAMLs are tests and behavioral references only.
- Compile from the current validated YAML model into an immutable domain execution plan. Do not make Dataflow blocks the domain model.
- Expected outcomes such as retry and durable defer must be typed and must not be flattened into generic workflow failures.
- Publication and checkpoint commits are serialized, ordered, cancellation-aware, and tested for no partial/duplicate externally visible result.
- Do not create speculative DAG/parallel vocabulary or abstractions for unused future features.
- Keep operation internals cohesive initially; split request, pagination, mapping, or hydration only when a demonstrated composition or testability need justifies it.
- Small methods, single-responsibility collaborators, feature-local folders, and neighboring .NET conventions apply. Avoid forced abstraction.
- Do not permanently maintain two orchestration engines. A temporary migration seam is allowed only until parity/cutover.
- Every phase must leave a buildable, reviewable state and record verification evidence before the next phase begins.
- Implementation is not authorized by this planning artifact alone; execution starts only when the user accepts the plan or explicitly asks to proceed.

