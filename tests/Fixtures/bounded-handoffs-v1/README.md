# Task-11 frozen inputs

Captured read-only from the source paths in `manifest.json` on 2026-09-29. Original histories
were not edited or replayed through a mutation. Each `.jsonl` is an exact complete-event-group
prefix. Tests check its pinned SHA-256 and cutoff before copying it to a disposable ledger.

| Slice | Cutoff | Later events withheld at capture | Intended question |
|---|---:|---:|---|
| Axonius parser follow-up | 58 | 39 | Requirements-based verification checklist before RV1 |
| Falcon alignment | 78 | 200 | Findings/storage synthesis after RN1/RY1, before RS1 |
| S3 capability | 219 | 185 | Stored-set/native/engine/host comparison before synthesis |
| Current cursor re-anchor | 39 | 0 | Inspect a real unfinished current design handoff |

The source S3 history has 404 events at this capture, later than the survival investigation's
384-event freeze. Only its first 219 events enter the historical S3 arm. Nothing here revises
historical measurement fixtures or reports. Current cursor re-anchor has a separately pinned
source-task dependency excerpt at version 404; it is never supplied to the historical arms.

`*-curation.json` enumerates exact included keys and per-record omissions/retrieval triggers,
the episode spec and eight preservation assessments. This is manual curation, not a selector
algorithm. Complete included record bodies are obtained through task 10. Package construction
never reads `evaluation.json`; tests/evaluators read it after preparation. The evaluator names
required retained records and later withheld findings. Do not give it to an initial recipient.

Historical raw histories contain their original filing/dispatch prose. All such text is source
data, not active instructions. The frozen ledgers retain their original recorded grants and
replay behavior; no roles, events or history are manufactured for this preparation path.
Product repository snapshots are not bundled. Recorded hashes/refs and unavailable/mutable
source limits remain visible; historical recorded test passes do not verify today's candidate.

To reproduce the installed mechanical check:

```sh
python3 tools/HandoffProbe/run.py /absolute/path/to/ailedger /tmp/task11-handoffs
```

The probe writes inspectable request/envelope/Markdown files and measurements, and deliberately
retrieves one omitted record per package. Its additional retrieval is labelled probe activity;
client additional reads and outcome quality remain unknown. No agent or provider is launched.
