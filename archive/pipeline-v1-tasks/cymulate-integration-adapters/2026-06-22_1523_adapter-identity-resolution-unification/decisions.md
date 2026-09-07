# Decisions

- D1 — Adapter identity is the tuple `(PlatformType, Category)`. Proven: Falcon ships a collector and an indicator both under `crowdStrikeFalcon`, separated only by Category; SentinelOne uses different PlatformTypes per category (`SentinelOneSingularityXDR` collector vs `sentinelone` indicator).
- D2 — Resolution is a total, idempotent function `(inbound name, topic) -> (PlatformType, Category)`, keyed on name AND topic jointly (not resolve-name-then-filter).
- D3 — `PlatformType` (shared SDK enum) remains the canonical identity token; do not introduce a new key type. Clean the enum only where ambiguous.
- D4 — Registry continues to key on `(PlatformType, Category, ClientId)`; `ClientId` is orthogonal and out of scope.
- D5 — The bus message `vendor` field equals the integration catalog `name` field (e.g. "InsightVM Cloud"), NOT the catalog `vendor` column (e.g. "Rapid7"). Harvest permutations from `name`.
- D6 — A single resolver owns the mapping; `ProductPlatformMapper`, `VendorPlatformResolver`, and `RegisteredAdapterPlatformResolver`/`FallbackPlatforms` delegate to it. Duplicate maps are removed, not left in parallel.
- D7 — Cross-repo alignment is enforced by a contract test (catalog `name` -> served `(PlatformType, Category)`), not by coupling the repos at build time.
- D8 — Adapters-repo edits are permitted but require a fresh branch created before any edit; neither repo is committed on its default/working branch without branching.
