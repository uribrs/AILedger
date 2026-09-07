# Orchestration Plan

## Complexity Decision

- Path: **decompose**
- Rubric: Complexity high (two repos, CLR assembly-identity semantics, packaging, ~178 files, frozen
  cross-repo contracts) / Separability high / Coupling low / Execution risk high (a wrong claim about ALC
  type identity would misdirect the entire migration) / Dependency order low (all streams run
  concurrently) / Worker clarity high (each stream has explicit inputs and a bounded output).
- Rationale: four genuinely independent read-only investigations feeding four distinct report sections.
  Reading Infra's `Conducting/*` plus ISB's whole loader chain plus 178 library files in one context would
  crowd out the analysis that matters. The integrating deliverable — the ISB change map — is retained in
  the main thread precisely because it depends on all four.

## Research Decisions

- **None needed.** `workflow.researchNeeded = false`. Every OPEN assumption in `assumptions.md` resolves by
  reading source inside the two repos; none depends on external vendor/protocol behaviour.
  Two exceptions handled without `technical-researcher`: the S3 collector inventory (D-A) is read-only AWS
  inspection assigned to W4, not external research; the "local-install test plan" is an internal doc
  search also assigned to W4.
- The one genuinely external claim — that ECMA-335 type forwarding cannot rename a namespace — is already
  recorded as a **settled constraint** in `constraints.md`, not an open assumption, so it needs no research.

## Worker Plan

All workers are **strictly read-only**. None may write to either repo or to the task directory; each
returns findings as text and the main thread owns all file writes. Boundaries are file-domain partitioned
so no two workers analyse the same source.

- **W1 — bidirectional content drift.**
  Scope: the 72 same-named file pairs between ISB `origin/dev`
  `Sdk/Cymulate.Integration.Sdk` and Infra `src/Cymulate.Integration.Client`, namespace-normalised.
  Owns: content comparison of shared-name files only.
  Output: drift table (file, side, classification, cross-repo flag) + true count reconciled against the
  operator's expected ~22.
  Dependencies: none.

- **W2 — set asymmetry disposition.**
  Scope: the 33 Client-only files (`Query/BaseQueryAdapter`, 6 `Query/Contracts`, 11 `Query/Models/Plan`,
  `Query/Models/Runtime/RawResponse`, `Query/Persistence`, 13 `Query/Pipeline`) and any ISB-only files.
  Owns: where those types live in ISB today (`Application.Query` and elsewhere), and whether Client's
  copies are stale / dead / would duplicate or conflict once ISB references Client.
  Output: disposition per file group + explicit verdict on whether Client must *shed* files, not only gain
  them.
  Dependencies: none. Boundary vs W1: W1 owns content of shared-name pairs; W2 owns files present on only
  one side.

- **W3 — concept verification (highest value).**
  Scope: Infra `src/IntegrationInfra` (`Conducting/*`, `Contracts/`, `Kernel/`, `Envelopes/`) against ISB
  `Infrastructure.Core/Services/{AdapterLoader,AdapterActivator,AdapterManager,AdapterLoadContext}.cs`,
  `Application/Services/AdapterRegistry.cs`, `Infrastructure.Core/Decorators/*`,
  `Domain/Interfaces/IAdapterActivator.cs`.
  Owns: the central question — does `Conducting/*` assume it drives adapters **in-process** while ISB
  drives them through an **isolated collectible ALC**? If those models disagree, "Client as the entrypoint
  into ISB" does not hold as stated and that outranks every packaging detail.
  Output: verdict (holds / holds with gaps / does not hold) with file:line citations and every gap named.
  Dependencies: none.

- **W4 — decision evidence.**
  Scope: `Docker/Dockerfile.WebApi`, both repos' `nuget.config`, ISB `Directory.Packages.props`, CI
  definitions, the S3 collector inventory (read-only AWS), presence of `IIndicatorCapability` /
  `ICollectorCapability` in Client, and the "local-install test plan" referenced by Infra's root
  `Directory.Build.props`.
  Owns: evidence for D-A and D-B. **Must take no decision.**
  Output: evidence per decision with trade-offs; candidate (a)(i) framed as a reversal of a documented
  BREAKING decision.
  Dependencies: none.

## Synthesis Approach

Main thread integrates W1–W4 into the 7-section report required by `prompt_contract.md` Output Format.
Section 5 — the file-by-file ISB change map with its compiler-verified vs silent-runtime split — is
**authored in the main thread**, not delegated, because it is the join across all four workers and the
split is the single most error-prone judgement in the deliverable. Contradictions between workers are
resolved by returning to cited source, not by averaging. Findings land in `execution_notes.md`.

## Verification Obligations

- Cross-check every Success Criterion in `prompt_contract.md`.
- Every behavioural claim must carry a `file:line` citation; unsourced assertions are findings.
- The compiler-verified vs silent-runtime split must be independently re-derived, not accepted.
- Confirm no decision on D-A or D-B was smuggled in as a recommendation.
- Confirm the drift count is reported truthfully rather than bent toward the operator's "~22".
- Confirm zero edits were made to either repo (`git status` clean in both, modulo the untracked task dir).
- Code-reviewer: **skipped for Phase 1** — the deliverable is an analysis report with no code,
  configuration, migration, or contract artifact. To be run in Phase 2, which is code-bearing.
