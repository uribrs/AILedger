# Decisions

- CanResumeFrom is a coarse pre-check by structural necessity (no event → no fingerprint); the authoritative gate is Restore. This is correct, not a bug — the deliverable is honest docs + tests, not "make the gates agree".
- The authoritative Restore gate already starts fresh on mismatch (no wrong-data); do NOT change its behavior.
- The real defect is the coverage gap: the start-fresh-on-mismatch and schemaVersion-bump paths are untested. Lock both.
- The existing resume tests only cover the matching-fingerprint happy path — the new tests must force a mismatch.
