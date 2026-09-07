# Decisions

- Treat “un-true them” as changing the three identified production `true` values to `false`, not deleting the reusable capability.
- Preserve Falcon's existing default-off, explicitly configurable behavior because it was not one of the enabled production cases.
- Update stale capability comments and assertions so they describe the new default accurately.
- Proceeding on unverified: collector tests expose storage-scope behavior without requiring vendor connectivity. If wrong: use build-level and local emitter tests and report the remaining gap.
- Run independent verification and isolated code review after implementation.
