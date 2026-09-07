# Verifier 1

## Verdict and findings

**PASS.** The investigation satisfies the original request and all four success criteria. Independent Git, package, and build checks reproduce the regression boundary and support the attribution:

- The first broken authored tree is `54aef287745c83018fb3a9d65245a3401fe53145`; its direct parent `59ba33517f15b3283dbf82545c6b23927f883a64` passes. An independent clean `git archive` build of the production YamlAdapter project returned exit `0`, 0 warnings, and 0 errors for `59ba3351`, then exit `1` with the three expected `CS1061` errors for `54aef287` at `ConnectionValidator.cs:184/194`.
- `a5b2e60b` is the PR #350 merge and the last passing `dev` state. Its relevant YamlAdapter production/test trees are byte-equivalent to passing `59ba3351`. `80fad2bd` is the PR #352 merge and the first broken `dev` state; its relevant trees are byte-equivalent to broken `54aef287` after branch reconciliation. Git ancestry independently confirms `59ba3351 -> 54aef287 -> 80fad2bd` and `a5b2e60b -> 80fad2bd`.
- Both independently restored comparison builds resolve `Cymulate.Integration.Yaml.Engine/1.0.0-preview.19`. The package `.nuspec` identifies source commit `88be469f9af93754b356fda7e7c39da3dfac4aef`, dated 2026-08-25; that tree has no `HandshakeFor` or `handshake_for`. Engine commit `ef407cf8030fa2cbf838e5f2d113a29ac30eb32f`, dated 2026-08-30, first adds the property, YAML alias, schema, and binder propagation and is included in engine PR #51 merge `1b765b9`.
- The isolated-restored and global-cache preview.19 artifacts match byte-for-byte (`.nupkg` SHA-256 `52daaba1...17cd7`; DLL SHA-256 `58aee1b8...63779`), and direct DLL inspection finds no `HandshakeFor`. Therefore the defect is release ordering: adapter PR #352 consumed a producer contract while still pinning the older published package. The corrective order is to publish a new engine package containing `ef407cf`, then bump the adapter pin and rebuild/test.
- Product trees were preserved. Before and after verification, the adapter checkout remained at `fd4bf1c` with the same pre-existing modifications to two `ai/skills` files and two untracked tool READMEs; the engine checkout remained clean at stale local `e1a5355`. Verification wrote only this review artifact and isolated outputs under `/private/tmp/yaml-adapter-verifier-20260831`.

## Success-criteria coverage

| success criterion | result | evidence |
|---|---|---|
| Name the first broken commit and responsible PR | PASS | First broken authored commit `54aef287`; introduced to `dev` by merge `80fad2bd`, PR #352. Confirmed by direct parent relationship, relevant-tree equality, merge ancestry, and independent failing build. See `research/adapter-history.md` and verifier build evidence above. |
| Name the last passing relevant commit or parent | PASS | Direct parent `59ba3351` passes; PR #350 merge / last prior `dev` state `a5b2e60b` has an identical relevant tree and is therefore also passing. See `research/adapter-history.md` and independent clean build. |
| Explain whether adapter code, engine source, package publication, or ordering caused the regression | PASS | Adapter code first consumed `HandshakeFor`; engine source added it separately ten seconds earlier but merged later; published preview.19 came from the five-day-earlier `88be469` and lacks the member. The causal failure is cross-repository publication/consumption ordering, surfaced by adapter PR #352. See `research/engine-package-lineage.md` and package provenance/hash checks. |
| Reproduce the pass/fail boundary without modifying product source | PASS | Independent immutable exports of `59ba3351` and `54aef287` built the production project: exit `0` versus exit `1` with three `CS1061` errors. Both resolved preview.19. Repository status snapshots confirm preservation. |

## Assumption Disposition

| id | disposition | citation | actor |
|---|---|---|---|
| A1 | VALIDATED | Independent direct-parent check plus clean builds of `59ba3351` (pass) and `54aef287` (three expected `CS1061` errors); corroborated by `research/adapter-history.md`. | independent verifier |
| A2 | VALIDATED | Preview.19 `.nuspec` points to `88be469` (2026-08-25), while `git log -S`/source inspection places the first complete contract at descendant `ef407cf` (2026-08-30); corroborated by `research/engine-package-lineage.md`. | independent verifier |
| A3 | VALIDATED | Merge subjects/parents identify PR #350 as `a5b2e60b` and PR #352 as `80fad2bd`; relevant-tree equality maps the former to the passing parent and the latter to the broken authored delta. | independent verifier |
| A4 | VALIDATED | Both directions were checked: fetched engine source contains the member, while a fresh feed-restored preview.19 package, its provenance commit, its DLL, and the adapter's resolved assets do not. | independent verifier |

## Attention Item Disposition

| id | final disposition | citation | actor |
|---|---|---|---|
| R1 | RESOLVED — `54aef287` is explicitly reported as the first broken authored tree; `80fad2bd` is explicitly reported as the PR #352 merge that first introduced it to `dev`. | Merge parents, ancestry, relevant-tree equality, and `research/adapter-history.md`. | independent verifier |
| R2 | RESOLVED — publication conclusions use restored artifact provenance and binary hashes/content, not `Directory.Build.props` version text. | Preview.19 `.nuspec` commit `88be469`, matching fresh/cache hashes, absent DLL member, and `research/engine-package-lineage.md`. | independent verifier |

## Decision Drift

- **Compare suspected commit and first parent using isolated evidence:** followed. The verifier independently exported `59ba3351` and `54aef287` with `git archive` and built each under `/private/tmp`.
- **Correlate adapter PR merges with engine commit and package-version history:** followed. Merge topology, timestamps, package provenance, binary contents, and resolved package identity were cross-checked.
- **Proceed on unverified that PR #352 is breaking; expand if wrong:** resolved without drift. The hypothesis was validated, so the contingency scan was not required.
- **Do not implement or publish a fix:** followed. No product source, branch, package, or publication was changed.

## Terminal disposition

| item | disposition |
|---|---|
| Verification | PASS |
| Original request | SATISFIED |
| Unresolved gaps | NONE |
| Product-tree preservation | CONFIRMED |
