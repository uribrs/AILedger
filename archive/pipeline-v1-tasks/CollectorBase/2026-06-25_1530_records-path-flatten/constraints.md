# Constraints

- Flatten is scoped to RECORDS-EXTRACTION ONLY. Do NOT change `JsonNav.At` / `JsonNav.StringAt` behavior — they
  back cursor / next_token_at / watermark / capture / next_url navigation, which resolve a SINGLE node and must
  NOT flatten (flattening would corrupt cursors/captures/next-url).
- Add a SEPARATE records-flattening resolver (new method); consumed by `ResponseMapper.Map` (+ `ExtractIds`). Do
  not mutate the shared `At`/`StringAt`.
- Generic engine — NO vendor identity/branch; behavior is response-shape-driven only.
- Passthrough-only emit preserved (full record published); envelope modes (verbatim/typed_wrapper/source_type_prefix)
  unaffected — flatten only changes WHICH elements are selected, never their content.
- Reuse existing Interpreter structures; net8.0; solution `CollectorBase.slnx`.
- No regression of the 71 passing tests — especially cursor / cursor_watermark / next_url pagination,
  capture / for_each threading, XML single-vs-array tolerance, and envelope/passthrough tests.
- Tests use the existing in-process harness (FakeHttpClientFactory + captured IAdapterExecutionContext).
