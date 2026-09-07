# Decisions

- Correct axis: ALL list-extractions (records, ids, capture_list, accumulate_list) flatten across a repeated parent; single-node nav (At/StringAt) does NOT. This group fixes the two list-capture sites the prior pass missed.
- Defect is established (B1) — execution fixes, does not re-investigate.
- `ListAtFlattened` already exists (prior pass) — reuse it; the change is two call-site reroutes + post-processing preserved.
- `At`/`StringAt` untouched; `drain_path` left as-is (A1).
- The new tests must use the capture→for_each shape (the structure the prior flatten test did not exercise) — that gap is exactly why B1 escaped.
