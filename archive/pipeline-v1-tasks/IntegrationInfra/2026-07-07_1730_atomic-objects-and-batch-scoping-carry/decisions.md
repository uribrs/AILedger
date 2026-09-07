# Decisions

- `BatchScopedStorage` home = `Envelopes/Common`. (Pinned — operator-decided: it manipulates the run metadata envelope; Envelopes/Common already owns the `storageUrl` wire contract; keeps Conducting-never-depends-on-Emission intact. Source's Egress placement was convenience, not principle.)
- D7's `_multipartFinalized` mechanism is superseded by the source's two-flag machinery — replace, never stack. (Pinned — B is a strict superset that also fixes M1, which D7 did not.)
- Port order: Changeset B (sessions/publisher/options/tests) before Changeset A (BSS + call sites + tests). (Pinned — B rewrites the lifecycle A touches.)
- Emission→Envelopes.Common edge accepted (sessions reference the metadata-key constants; Envelopes is a leaf). (Pinned.)
- Tests translated to plain xUnit `Assert`; FluentAssertions not introduced. (Pinned — package house style.)
- `RecordingPublisher` becomes one shared test helper for both ported test files. (Pinned.)
- Version bump `1.0.0-preview.2` → `1.0.0-preview.3`. (Pinned — matches the package's carry cadence.)
- Behavior-named symbols carry unchanged; D8/D2 naming rules of the package honored at target locations. (Pinned.)
- The package's `LogWarning` env-override pattern is followed for `PublishThrottling__SoftRecordWarningBytes` exactly as `MaxBytesPerBatch` does. (Pinned — source parity.)
- 1:1-parity checklist (every source hunk → target location) is a required execution artifact in execution_notes.md. (Pinned — this is the port's proof of "no functionality lost".)
