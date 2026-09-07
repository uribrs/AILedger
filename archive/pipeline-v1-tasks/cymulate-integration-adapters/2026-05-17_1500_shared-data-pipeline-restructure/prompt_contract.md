Role:
You are a senior .NET refactoring engineer working in the `cymulate-integration-adapters` repository.

Goal:
Introduce `Shared/Cymulate.Integration.Adapters.Shared/DataPipeline/` as the named home for the adapter data plane. **Retire `Publishing/` and replace it with `DataPipeline/Egress/`** (`Publishing` ceases to exist); rehome `Shared/Json/` under `Shared/DataPipeline/Json/`. Propagate namespace renames and identity-prefix type renames across the entire repository. Create an empty `Shared/DataPipeline/Ingress/` reserved for forthcoming source-side streaming work, and add `Shared/DataPipeline/README.md` describing the data-plane role and the three sub-roles. The deliverable is one PR with exactly two commits. No behavior change.

Context:
- The publisher (`Shared/Publishing/Ndjson/NdjsonBatchSession.cs` and siblings) is the sink-side half of the adapter data plane. It applies a four-tier heap defense already.
- `Shared/Json/` contains streaming readers (e.g., `TopLevelJsonArrayStreamReader`) and per-row UTF-8 normalization helpers used by every collector flow.
- `Session/`, `Recovery/`, and `Orchestration/` are control-plane citizens. They are not part of this move.
- A forthcoming Cortex XDR XQL streaming refactor and a future time-based adapter type will land first occupants inside `DataPipeline/Ingress/`. Neither of those is part of this task.
- The task design conversation, including the data-plane/control-plane rationale and the operator's decision to name the umbrella `DataPipeline`, is captured in `decisions.md` and `assumptions.md` in this directory.

Constraints:

* One PR, two commits exactly.
  - Commit 1: retire `Publishing/`, create `DataPipeline/Egress/`, rehome `Json/` under `DataPipeline/`, propagate namespace renames + identity-prefix type renames across collectors, tests, sample/runner projects, project files, and DI registrations.
  - Commit 2: `DataPipeline/Ingress/` placeholder file and `DataPipeline/README.md`.
* No behavior change. Diff is mechanical: paths, namespaces, `using` statements, `<Compile>` and `<ProjectReference>` entries, and three identity-prefix type renames (enumerated below).
* Namespace renames (complete — no residual `Publishing` namespace anywhere in the repo):
  - `Cymulate.Integration.Adapters.Shared.Publishing` → `Cymulate.Integration.Adapters.Shared.DataPipeline.Egress` (and every sub-namespace, e.g. `.Ndjson`, `.Multipart`, `.Telemetry`).
  - `Cymulate.Integration.Adapters.Shared.Json` → `Cymulate.Integration.Adapters.Shared.DataPipeline.Json`.
* Identity-prefix type renames (do exactly these three, no others):
  - `PublishThrottlingOptions` → `ThrottlingOptions`
  - `PublishBufferingOptions` → `BufferingOptions`
  - `PublishMemoryPressureOptions` → `MemoryPressureOptions`
* Type names kept verbatim:
  - `PublishResult` (verb-form return type — describes "result of publishing")
  - `CollectorNdjsonPublisher`, `ResultsBatchPublisher` (role-noun classes — describe what they are)
  - All `Publish*Async` method names (verb-form actions)
* Update every call site of the renamed option types — including tests and any sample/runner projects. `PublishThrottlingOptions.Resolve(...)` becomes `ThrottlingOptions.Resolve(...)`, etc.
* Do not touch `Session/`, `Recovery/`, or `Orchestration/` — control-plane.
* Do not touch the SDK's `IAdapterDataPublisher` interface or its members. That contract is external to this repo's data plane.
* Do not change runtime behavior. No method signatures, no formatting churn, no logic edits, no new public types.
* `DataPipeline/README.md` content requirements:
  - One-paragraph data-plane statement.
  - `Ingress/` / `Json/` / `Egress/` sub-role descriptions.
  - The boundary rule: "DataPipeline contains code that processes data records themselves. Code that processes control or metadata about the records lives elsewhere (`Session/` for transport, `Recovery/` for checkpoint decisions, `Orchestration/` for composition)."
  - A short note that `Ingress/` is currently empty and reserved for forthcoming source-side streaming work.
* Existing READMEs inside the moved folders move with them. Each gets a one-line cross-link added at the top pointing to the new `DataPipeline/README.md`.
* `DataPipeline/Ingress/` must contain a placeholder file so git tracks the directory. Use whichever placeholder convention already appears in this repo (`.gitkeep` or an empty marker `.cs`).
* Pre-commit hooks run. Do not pass `--no-verify`.
* Cortex XDR XQL streaming refactor is NOT in scope. Do not modify `CortexXdrXqlClient.cs` or `CortexXdrFindingsFlow.cs` except for import-line updates.
* `va_endpoints` and `sourceType` discriminator work are NOT in scope.

Success Criteria:

* Folder layout after merge:
  ```
  src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/
    DataPipeline/
      Ingress/<placeholder>
      Json/...           (formerly Shared/Json/)
      Egress/...         (formerly Shared/Publishing/)
      README.md
    Session/             (unchanged)
    Recovery/            (unchanged)
    Orchestration/       (unchanged)
    ...                  (other unchanged folders)
  ```
* No file under `Shared/Publishing/` or `Shared/Json/` remains after Commit 1 (the original directories are gone).
* `grep -r "Cymulate.Integration.Adapters.Shared.Publishing"` returns zero matches anywhere in the repo after Commit 1.
* `grep -r "\.Shared\.Json\b"` returns zero matches in `using`/`namespace` lines outside the moved files' own declarations after Commit 1.
* `grep -rn "\bPublishThrottlingOptions\b\|\bPublishBufferingOptions\b\|\bPublishMemoryPressureOptions\b"` returns zero matches anywhere in the repo after Commit 1.
* `dotnet build` of the solution succeeds without warnings other than those that existed before the move (e.g., the pre-existing `NU1900` vulnerability-feed warnings from the prior task).
* Every test project that already built and passed before this PR continues to build and pass with no test-logic changes (only import-line updates).
* Each commit message follows the repository's existing convention (concise summary line, no scope-bleed).
* Both commits pass pre-commit hooks without `--no-verify`.
* `DataPipeline/README.md` exists and matches the content requirements above.

Execution Rules:

* Do not assume missing data. If a reference outside the standard repo locations (e.g., NuGet metadata, external docs) names the moved namespaces, surface as a blocker before proceeding.
* Respect constraints strictly.
* Make Commit 1 stand alone as a no-op rename: the diff should contain only `git mv`-equivalent moves, namespace and `using` updates, project file path updates, and (where applicable) one-line README cross-links inside the moved READMEs. No README.md at the `DataPipeline/` root in Commit 1.
* If the build fails after Commit 1 staging, identify the missing reference and update it. Do not work around with `using` aliases or partial migrations.
* If a worker subagent is used, do not parallelize the move across workers — namespace propagation must be done atomically to avoid intermediate broken-build states.
* Verify by running `dotnet build` and the test commands the orchestrator confirms with the repo conventions. Report the exact commands used.

Output Format:

* List of changed files grouped by commit.
* Verification commands run, with pass/fail.
* The new `DataPipeline/README.md` content as written.
* Any unresolved blockers or accepted technical risks.
* Final task directory path.

Stop Conditions:

* Constraints would be violated (e.g., more than two commits would be required, or behavior change is unavoidable).
* A reference outside the repo names the moved namespaces and cannot be updated within this scope.
* Build or test failures cannot be resolved by mechanical import updates alone (i.e., would require code logic changes).
* The goal is achieved and verification is complete.
