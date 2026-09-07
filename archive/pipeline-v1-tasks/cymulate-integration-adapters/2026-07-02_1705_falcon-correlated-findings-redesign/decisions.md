# Decisions

- Rollout: parser-first — parser auto-detects input shape (legacy split pair vs correlated records) and supports both; collector flow then replaced outright, no collector mode flag. (User: "whatever suits you"; chosen for no dual collector paths + no deploy-ordering hazard.)
- `CollectFindings` publishes correlated records as `findings_*.json` only; the separate `assets_*.json` lane is dropped — envelopes are the asset spine. Standalone `CollectAssets` flow unchanged. (User-confirmed.)
- Strip `apps` and `suppression_info` from findings at emission. (~70% payload reduction; parser provably never reads them. User-confirmed.)
- Window-boundary orphan semantics accepted: asset-window-scoped fetching matches what the parser already keeps. (User-confirmed.)
- Record schema: `{aid, chunk, isLastChunk, findingsInChunk, host, findings[]}`; chunk cap ~2000 findings/record.
- Sort `remediation.entities` canonically at emission — fixes pre-existing parser `entities[0]` nondeterminism.
- One Spotlight pass over `status:['open','reopen','closed']` — status lanes and month segments retire; every run is aid-scoped so segment completeness machinery is obsolete by construction.
- Checkpoints carry watermarks + aid-batch position only (never cursors); both scrolls re-anchor on 120s expiry (Discover: `last_seen` floor + processed-aid dedupe; Spotlight: `updated_timestamp` floor + per-batch seen-id dedupe).
- Prototype (`FalconCorrelatedFindingsProbe.cs`) is the behavioral spec; mirror it, don't re-derive.
- Major collector version bump; checkpoint version bump with no cross-version resume.
- Egress page hash: streaming hash over the logical object's bytes, logged on the publish-completion line, all collectors.
