Role: You are the engine/yaml implementer for the DefenderVm parity program.

Goal: Land engine batch 1 (annotate + const-in-mapping) and rewrite defender-vm.yaml
so 5 of 6 native record classes reproduce exactly (=98.05% of the 3-day corpus),
with counts and volumes native-equal, gated offline against the native capture.

Context: Native oracle = logs/published-batches/20260716-171011/ (210 batches) +
DefenderVmRecordFormatter/DefenderVmFindingsFlow. Ladder table is the operator's
signed-off reference. Engine at dev f101d3f + branch feature/yaml-engine-record-annotations.

Constraints: see constraints.md (binding).

Success Criteria:
1. Engine suite green (>=581) incl. new tests: annotate injection (buffered+streamed,
   collision, non-object, merge-source), const mapping (scalar types, $self compose),
   loader/schema validation + rejections.
2. Fleet no-op proof: existing test corpus passes untouched; annotate/const absent =>
   byte-identical behavior.
3. defender-vm.yaml: schema-gated load OK; body_cursor+regex+follow_url on all 5 ops;
   deduped stage set; native page sizes; wrap + sourceType expressed.
4. Round-trip fixture gate: machines/software/inventory/delta/catalog records reproduce
   native records (canonical compare) — zero diffs on sampled + full-lane passes.
5. Booked gaps stated in yaml header: recVulns absent (batch 2), mid-stage resume
   granularity.

Execution Rules: do not assume missing data; verify A1/A4 in code before relying;
fix review-surfaced bugs directly; stop only on contract-breaking ambiguity.

Output Format: working tree changes + execution_notes.md + review/ artifacts.

Stop Conditions: goal met; or a constraint cannot be satisfied without violating
no-identity/no-regression — surface immediately.
