Role:
You are a .NET engineer fixing a JSON/XML navigation defect in a declarative collector interpreter,
without changing navigation used elsewhere.

Goal:
Make records extraction flatten across repeated (array) elements in `records_path`, so a path that crosses
an array (e.g. `…HOST_LIST.HOST.DETECTION_LIST.DETECTION`) yields every nested record instead of silently
zero — while leaving cursor/next-token/watermark/capture/next_url navigation exactly as it is.

Context:
- Defect proven this session (treat as established): `JsonNav.At` (Interpreter.cs:22-40) is object-only dot-nav
  → null on hitting an array; `ResponseMapper.Map` (Interpreter.cs:92-99) keeps only `JsonObject`s; `ListAt`
  (Interpreter.cs:57-66) does not flatten array-of-arrays. Repeated XML siblings become a `JsonArray`
  (XmlResponse.cs:65-78). NET: a records_path crossing a repeated element → 0 records, silently.
  Measured: multi-host detections 0/3; single-host 2/2.
- In-repo impact: integrations/qualys.yaml:47 findings stream. qualys.yaml:29/:65 and all other profiles use
  terminal-array records_path and must not change behavior.

Constraints:
- See constraints.md. Salient: flatten scoped to RECORDS-EXTRACTION ONLY via a NEW resolver; `At`/`StringAt`
  UNCHANGED; no vendor identity; passthrough + envelope semantics untouched; reuse Interpreter structures;
  net8.0 / CollectorBase.slnx; the 71 existing tests stay green.

Success Criteria:
1. `dotnet build CollectorBase.slnx` clean.
2. `dotnet test CollectorBase.slnx` — 71 existing pass + new tests:
   (a) records_path crossing a repeated parent flattens → multi-host detections = 3 (not 0);
   (b) single-host still = 2 (single-vs-array tolerance preserved);
   (c) array-of-arrays at the record position handled per the A2 decision;
   (d) a regression test locking cursor / next_token_at / watermark / capture navigation UNCHANGED.
3. integrations/qualys.yaml:47 findings emits detections across multiple hosts (harness test with a
   multi-host + multi-detection XML mock).
4. No regression (full suite green; spot-check pagination + capture + envelope).
5. flatten-note.md in the task dir: root cause, the records-only flatten resolver, why At/StringAt were left
   untouched, and the chosen A1 (implicit vs explicit) / A2 (array-of-arrays + scalar) / A3 (ExtractIds) resolutions.

Execution Rules:
- Pin A1/A2/A3 (assumptions.md) at the top of execution and record the resolutions; LEAN implicit-flatten,
  ExtractIds shares the resolver, scalars at record position skipped.
- Add a new resolver method; do NOT edit `At`/`StringAt`. Route `ResponseMapper.Map` (and ExtractIds per A3) to it.
- Do not re-investigate the defect; it is proven. Do not add vendor branches. Respect constraints strictly.

Output Format:
- Code changes under CollectorExecutor/Interpreter/ (+ minimal call-site wiring), tests under
  Tests/CollectorExecutor.Test (incl. a multi-host XML harness test for qualys.yaml:47), flatten-note.md in the
  task dir, execution_notes.md updated with A-resolutions, commands, residual risks.

Stop Conditions:
- Flattening records would require changing `At`/`StringAt` (shared nav) — stop and surface; the records resolver
  must be separate.
- A pagination/capture/next_url regression appears that can't be resolved without touching shared nav — stop.
- Goal achieved and full suite green.
