# Assumptions

- A1 — VALIDATED — Adapter commit `54aef287` is the first commit that makes YamlAdapter fail to compile against its pinned engine package. actor: verifier; citation: `review/verifier-2.md`, independent clean builds of `59ba3351` and `54aef287`.
- A2 — VALIDATED — Engine commit `ef407cf` added the required `HandshakeFor` contract only after engine package `1.0.0-preview.19` had been published. actor: verifier; citation: `review/verifier-2.md`, preview.19 nuspec provenance `88be469` and engine ancestry.
- A3 — VALIDATED — Adapter PR #352 merged the regression into `dev`; the earlier related PR #350 did not independently break compilation. actor: verifier; citation: `review/verifier-2.md`, merge topology and clean ref builds.

## Prior Art

- A4 — VALIDATED — Package/source drift must be checked in both directions rather than assuming one side is newer. actor: verifier; citation: `review/verifier-2.md`, fetched engine source plus fresh feed-restored package comparison; source: lessons.md#L-96bb76cc (verifier, 2026-08-02).
