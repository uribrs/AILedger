# Execution Notes

- Internal recon froze the `OperationConfig.HandshakeFor` consumer/producer surface and split the investigation into disjoint adapter and engine/package histories.
- Adapter boundary evidence: `research/adapter-history.md` proves `59ba3351` and PR #350 merge `a5b2e60b` build successfully, while authored child `54aef287` and PR #352 merge `80fad2bd` fail with the same three `CS1061` errors. Every ref resolves engine `1.0.0-preview.19`.
- Engine lineage evidence: `research/engine-package-lineage.md` proves published preview.19 comes from `88be469`, while `HandshakeFor` first appears five days later in `ef407cf` and reaches engine `dev` via PR #51 merge `1b765b9`.
- A fresh CodeArtifact restore matches the cached preview.19 package and DLL byte-for-byte and lacks both `HandshakeFor` and `handshake_for`.
- Synthesized cause: adapter PR #352 merged a consumer of an engine public member before any compatible engine package had been published; PR #350 is not the breaking PR.
- Product source and branches were not changed. Evidence was written only under this task directory and temporary build/package locations.
