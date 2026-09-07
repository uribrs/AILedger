# Constraints

- Do NOT change `JsonNav.At` / `JsonNav.StringAt` — single-node nav (cursor / next_token_at / watermark /
  capture-SCALAR / next_url) must stay literal/non-flattening.
- Only the two LIST-capture sites are rerouted: `capture_list` (StepHelpers.cs:176) + `accumulate_list` (:191) →
  `JsonNav.ListAtFlattened`. Keep their existing post-processing (for_each list build; append+dedupe).
- `drain_path` ListAt (Runner.cs:654) stays on non-flattening `ListAt` (out of scope; no consumer crosses a
  repeated parent) — see A1.
- Generic engine, no vendor identity/branch; passthrough + envelope semantics untouched.
- net8.0; CollectorBase.slnx; no regression of the 75 passing tests.
- Tests use the existing in-process harness.
