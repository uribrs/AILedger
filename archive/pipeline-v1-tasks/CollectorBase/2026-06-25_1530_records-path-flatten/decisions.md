# Decisions

- The defect is established (proven 0-of-3 multi-host, 2-of-2 single-host via the real code path) — execution does NOT re-investigate, it fixes.
- Fix = a NEW flatten-aware records resolver in the Interpreter; `At`/`StringAt` are left byte-for-byte unchanged.
- The resolver is consumed by `ResponseMapper.Map` and (pending A3) `ExtractIds`; nothing else.
- No vendor identity: the resolver keys off encountering an array mid-path, never off a vendor name or profile field.
- Flatten changes only WHICH elements are selected as records, never their content — passthrough + envelope semantics are untouched.
- qualys.yaml:47 is the in-repo proof case; it stays as authored (no YAML edit) under the implicit-flatten decision (A1 lean).
