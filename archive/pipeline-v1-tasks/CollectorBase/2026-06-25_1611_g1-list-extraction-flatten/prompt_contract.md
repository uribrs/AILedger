Role:
You are a .NET engineer completing a navigation class-fix in a declarative collector interpreter.

Goal:
Make `capture_list` and `accumulate_list` flatten across a repeated (array) parent — so a captured list whose
path crosses an array yields the nested items (and the `for_each` over it runs) instead of silently empty —
by routing them through the existing `JsonNav.ListAtFlattened`, while leaving single-node navigation
(`At`/`StringAt`) unchanged.

Context:
- Defect B1 (established): `StepHelpers.cs:176` (capture_list) and `:191` (accumulate_list) use the
  non-flattening `JsonNav.ListAt`; a capture crossing a repeated element → empty → its `for_each` never runs →
  0 emitted. Same primitive already fixed for records_path/ids; `JsonNav.ListAtFlattened` exists.
- The prior flatten test used a DIRECT records_path, not the capture→for_each shape — which is why B1 escaped.

Constraints:
- See constraints.md. Salient: reroute ONLY the two list-capture sites; keep their post-processing; do NOT touch
  `At`/`StringAt`; `drain_path` stays as-is (A1); generic engine, no vendor identity; passthrough/envelope
  untouched; net8.0/CollectorBase.slnx; 75 prior tests stay green.

Success Criteria:
1. `dotnet build CollectorBase.slnx` clean.
2. `dotnet test CollectorBase.slnx` — 75 prior pass + NEW: (a) capture_list crossing a repeated parent yields the
   flattened ids and its for_each runs (built on the capture→for_each shape); (b) accumulate_list crossing a
   repeated parent accumulates across iterations; (c) regression-lock that capture-scalar + cursor/next_token nav
   stay single-node (At/StringAt unchanged — flattening them would be wrong).
3. A multi-step harness test mirroring the integrations/qualys.yaml findings shape (capture_list host_ids crossing
   the repeated HOST → for_each → detections) emits findings across multiple hosts end-to-end (was 0).
4. No regression (full suite green).
5. note.md (or execution_notes) records: corrected axis, the two rerouted sites, the drain_path (A1) decision,
   the two nit (A2) resolutions.

Execution Rules:
- Pin A1 (leave drain_path) + A2 (document the two nits) at the top; record resolutions.
- Reroute the two sites; do not re-investigate the defect; do not touch single-node nav. Respect constraints.

Output Format:
- Code change in CollectorExecutor/Execution/CollectorExecutorStepHelpers.cs (+ optional Interpreter.cs doc/nit),
  tests in Tests/CollectorExecutor.Test, note + execution_notes in the task dir.

Stop Conditions:
- Rerouting capture/accumulate would require changing `At`/`StringAt` — stop and surface (it must not).
- Goal achieved and full suite green.
