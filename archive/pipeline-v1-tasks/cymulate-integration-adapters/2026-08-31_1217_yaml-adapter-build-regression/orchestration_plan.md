# Orchestration Plan

## Problem Classification

- Contract sufficiency: sufficient.
- Classes:
  - `package-drift` — adapter source consumes a contract absent from the restored package binary.
  - `cross-repo-contract` — producer and consumer changes landed through separate repositories and release pipelines.
  - `build-regression` — the requested boundary is a reproducible compile pass/fail transition.
- Additional classified prior art: none — A4 already captures the relevant bidirectional package/source drift lesson.
- Recon correction: confirmed; this is specifically a package-publication ordering regression, not a runtime connection-test failure.
- New or changed artifacts:
  - None introduced — this investigation produces evidence files only and changes no runtime artifact.

### Attention Items

| id | failure mode | causal path and impact | planned handling | source |
|---|---|---|---|---|
| R1 | Misattribute authored commit as merge commit | `54aef287` authored delta and `80fad2bd` merge play different roles; conflating them names the wrong break point | `research:adapter-history` | `research/internal-recon.md` |
| R2 | Treat engine source version text as publication proof | CI may override version and the same preview label can identify different source states; conclusion could falsely claim the fix is published | `research:engine-package-lineage` | `research/internal-recon.md` |

### Research Questions

No external research needed — immutable local/fetched Git objects, NuGet contents, and builds are authoritative for this regression.

## Complexity Decision

- Path: decompose.
- Axis scores: Complexity medium | Separability high | Coupling low | Dependency order low | Execution risk medium | Worker clarity high.
- Rationale: the task spans two repositories and recon found disjoint adapter-history and engine-package-lineage evidence sets. The shared contract is frozen and synthesis only orders the timelines.

## Research Decisions

- None needed externally. Repository and artifact evidence answers every decision-changing question.
- Internal recon: complete → `research/internal-recon.md`.

## File Ownership

- Disjoint sets found: 2.
- W1 owns: `research/adapter-history.md` only; inspects adapter Git refs and temporary build locations.
- W2 owns: `research/engine-package-lineage.md` only; inspects engine Git refs and NuGet package artifacts.
- Shared surface frozen in phase 0: `OperationConfig.HandshakeFor : List<string>?`, YAML alias `handshake_for`, package identity `Cymulate.Integration.Yaml.Engine/1.0.0-preview.19`, and immutable comparison refs from internal recon.

## Worker Plan

- W1 — scope: prove adapter pass/fail and PR boundary; owns: `research/adapter-history.md`; inputs: frozen refs and package identity; output: immutable ancestry plus isolated build evidence; phase: 1; continuity: fresh.
- W2 — scope: prove engine source/package chronology; owns: `research/engine-package-lineage.md`; inputs: frozen contract and preview.19 identity; output: commit ancestry, timestamps, version lineage, and binary-contract evidence; phase: 1; continuity: fresh.

## Synthesis Approach

Order W1's first consumer/merge boundary against W2's source and publication boundary. Distinguish last passing adapter state, authored breaking commit, merge-to-dev commit, engine implementation commit, engine PR merge, and published package contents.

## Verification Obligations

- Cross-check against `prompt_contract.md` Success Criteria.
- Confirm isolated parent build passes and breaking child build fails for the expected missing member.
- Confirm PR #350 lacks the member reads and PR #352 introduces them.
- Confirm fresh `preview.19` restore lacks `HandshakeFor` even though fetched engine source contains it.
- Preserve both product repositories without source or branch changes.
