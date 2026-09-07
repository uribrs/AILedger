# Constraints

## Functional (hard)
- Must collect assets that have **no vulnerabilities** (only `/assets/export` can — vulns export is structurally blind to clean hosts).
- Must collect **all severity levels**, including `info` (remove the legacy info-severity filter).
- Must preserve **every field the parser requires** (audited present across the two feeds; see decisions for the name-delta map).
- May **skip closed/FIXED** vulns (`filters.state=[OPEN,REOPENED]`); must still fetch **all assets** regardless.
- **No-vuln assets must survive end-to-end** and must **not** spawn phantom/empty findings.

## Architectural
- Collector stays **dumb**: raw vendor JSON, no in-collector join or enrichment. Correlation/mapping is the parser's job.
- **No per-asset `/assets/{id}` enrichment** — v3 ACR comes from the bulk assets feed.
- Preserve **resilience/resumability** of both chunked exports (mirror existing chunked export handling + checkpointing).
- Parser changes only in the Tenable parser + shared split-model preparation; do not break other integrations using `preparation.py`.

## Process
- Legacy repo is **read-only** (reference).
- Adapter work lands in the **Uri checkout** (`/Users/user/Dev/Uri/cymulate-integration-adapters`).
- Volume reality: all-severity findings ≈ **~3× today** — downstream path (parser → Spark → postgres, batchful upload) must tolerate it.
