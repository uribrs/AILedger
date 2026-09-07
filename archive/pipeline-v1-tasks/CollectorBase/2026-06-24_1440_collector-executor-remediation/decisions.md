# Decisions

- Tolerant token extraction is the root fix for 1a (coerce any JsonValue to its string form); broadening the
  catch filter is secondary, not the primary fix.
- Clamp effective page/batch size to >= 1 for 1b (don't reject a valid size; just prevent the 0/negative loop).
- Item 2: extract a shared method returning either a validation failure OR the prepared run state; both
  Preflight and RunAsync call it — single source of truth, behavior-preserving.
- Item 3: delete the dead vocabulary (verified-unread) rather than wire it; fix the misleading comments.
- Direct execution path expected (bounded, coherent edits across a few files); orchestrator decides.
