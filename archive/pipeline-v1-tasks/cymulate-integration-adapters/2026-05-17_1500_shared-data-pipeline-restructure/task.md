# Shared/DataPipeline restructure

Introduce `Shared/Cymulate.Integration.Adapters.Shared/DataPipeline/` as the named home for the adapter data plane. **`Publishing` is retired and replaced by `Egress`**; `Json` is rehomed under the new umbrella.

- `Shared/Publishing/` → `Shared/DataPipeline/Egress/` (rename — `Publishing` ceases to exist)
- `Shared/Json/` → `Shared/DataPipeline/Json/` (rehome)

Create `Shared/DataPipeline/Ingress/` as an empty folder reserved for a future source-side streaming primitive (Cortex XDR XQL refactor and a forthcoming time-based adapter type are the expected first occupants — not delivered in this task).

Add `Shared/DataPipeline/README.md` describing the data-plane role and the three sub-roles, with the rule that distinguishes the data plane from neighboring folders (`Session/`, `Recovery/`, `Orchestration/`).

The namespace rename must be **complete**, not partial: types and option classes whose names carry the dying `Publish*` identity prefix are renamed to match the new location. Verb-form names (`PublishPageAsync`, `PublishResult`) and role-noun class names (`*Publisher`) describe what the code does and stay as-is. See `decisions.md` for the exact rename scheme.

No behavior change. One PR, two commits:

1. **Commit 1** — retire `Publishing/`, create `DataPipeline/Egress/`, rehome `Json/` under `DataPipeline/`, propagate namespace renames and identity-prefix type renames repo-wide.
2. **Commit 2** — add `DataPipeline/Ingress/` placeholder and `DataPipeline/README.md`.
