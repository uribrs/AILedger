# DefenderVm yaml parity — engine batch 1 + yaml rewrite

Make defender-vm.yaml reproduce the native DefenderVmCollector's output per the
signed-off parity ladder (operator reference table):

| Step | Exact-match records |
|---|---|
| yaml fixes only | 0% (97.8% one-field-short) |
| + annotate (sourceType) | ~97.8% |
| + const-in-mapping (wrap) | ~98.05% |
| + reference fan-out | 100% — EXPLICITLY OUT OF SCOPE (engine batch 2) |

Scope: two small generic engine capabilities + schema/loader/tests, then the
defender-vm.yaml rewrite, gated offline against the 3-day native capture at
logs/published-batches/20260716-171011/ (965,540 records; oracle).
