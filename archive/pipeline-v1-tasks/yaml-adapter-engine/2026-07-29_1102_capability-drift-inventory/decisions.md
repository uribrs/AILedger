# Decisions

One decision per bullet. Amend in place if execution overturns one; do not delete.

- **D1 — The differential harness is built first (I0), before any dimension is inventoried.** *Why: the
  entire evidence standard depends on whether `5e5ca52` builds (A4); discovering it does not, after
  writing half the table on the assumption it does, would mean redoing the table.*

- **D2 — Coverage is established from enumerable surfaces, not from prose reading.** YAML keys from the
  original's model declarations, `throw` sites, nullable-member guards, checkpoint field names — each
  greppable and countable. *Why: an 11,364-line read has a recall problem, and the capabilities most
  likely to be missed are the small defensive ones (A7), which is exactly the class D18 belonged to.*

- **D3 — `git show 5e5ca52:<path>` is the working baseline; the vendored tree is consulted only for the one
  divergent file.** *Why: 64 of 65 files are identical, and the git ref is scriptable.*

- **D4 — Liveness and importance are separate columns.** "Live at all" is judged against all 279
  definitions; "matters most" against the operator's five. *Why: a capability unused by the five still
  runs in the other 274 (A8).*

- **D5 — `CHANGED-DELIBERATELY` requires a citation** to a commit, CHANGELOG entry or defect-register
  entry. *Why: without that bar, every unexplained difference can be reclassified as intentional after the
  fact, which is the failure mode this task exists to catch.*

- **D6 — Evidence strength is recorded per row, not assumed uniform.** Rows resting on execution are
  marked differently from rows resting on inspection. *Why: presenting inspection-only evidence as
  equivalent to a differential is the same over-claim, one level down.*

- **D7 — The seed finding is treated as a hypothesis, not a result.** A2 must be resolved — specifically,
  whether production actually compiles the vendored tree — before it is reported as a production gap.
  *Why: if the adapter compiles something else, it is a stale fork, not a capability gap, and the severity
  changes completely.*

- **D8 — Performance findings report shape changes, not magnitudes, unless measured.** *Why: D18's
  magnitude was asserted without measurement; a benchmark is a recommendation, not part of this task (A6).*

- **D9 — No fix, however small.** Even a one-character fix (the missing `TrimEnd()`) stays a
  recommendation. *Why: the operator scoped this as an inventory, and a task that quietly starts repairing
  loses the ability to report cleanly on what it found.*

- **D10 — Both directions are reported per dimension, including empty results.** A dimension with no
  improvements found says so explicitly. *Why: the operator asked to hear improvements; silence is
  ambiguous between "none found" and "not looked for".*
