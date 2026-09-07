# Decisions

- **D1 — Asset `type` stays const `"Host"`.** All ~11 asset-producing parsers in the repo use `Host`; `cybi.asset_type.cloud_resource` exists but is written by nothing. Changing the convention is not this branch's call. (Operator, 2026-08-02.)

- **D2 — Asset `value` stays `coalesce(nonblank(name), nonblank(external_id), id)`.** Repo convention is a human-recognizable name (CloudGuard `entityName`, Cortex `endpoint_name`, Defender `computerDnsName`, SentinelOne `computerName`). An opaque vendor id would diverge. (Operator, 2026-08-02.)

- **D2a — The `(value, type)` collision is accepted, not fixed here.** EA's asset identity is `(value, type)` and Wiz names are not unique (185 names cover 656 of 4,809 assets). The collision is inherent to the D2 convention and affects every parser equally. Recorded as a known property. (Operator, 2026-08-02.)

- **D3 — Wiz tags flatten to `"key=value"`.** No existing convention (A6), so pick the cloud-native form used by AWS/Azure CLI output. Entries with a blank key emit the bare value; entries with a blank value emit the bare key; fully blank entries are dropped.

- **D4 — Drop the `asset_provider_id` fallback join key.** Measured 100% resolution on the primary key across 7,153 real findings with 0 orphans. The fallback resolves nothing, forces a `BroadcastNestedLoopJoin` over ~518M comparisons, and admits a duplicate-row path.

- **D5 — Correlate with the shared `utilities.correlation` helper, not a hand-rolled join.** It is purpose-built for split lanes and its embedded mode guarantees primary-side cardinality (A5). Matches Cortex, Defender VM and CrowdStrike.

- **D6 — `"Region"` is deleted rather than sourced from the findings lane.** Region exists only on findings (`asset_region`). Carrying it onto the asset would be new behaviour, not a fix. Smaller change wins; can be added later if wanted.

- **D7 — Verification is static when Spark cannot start.** No JVM available. Correctness is argued against the sibling implementations and the documented `correlate` contract, and reported as unexecuted. Never claim a passing test run.
