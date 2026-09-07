# Assumptions

- [VALIDATED] Collector B exists and is an independent C# agent-side collector (`IAsyncFindingsCollector`, Newtonsoft `JObject`, `Cymulate.Agent.*` infra), structurally parallel to A with the same findings-perspective loss. (Confirmed by inspection 2026-06-09.)
- [VALIDATED] Host list `?action=list&details=All&show_asset_id=1` returns a superset of detection HOST_DETAILS incl. ID/IP/OS/DNS/NETBIOS/ASSET_ID. (Confirmed live via probe.)
- [VALIDATED] FQDN is nested at `DNS_DATA.FQDN`, not top-level; parser's flat `"FQDN"` path yields "". (Confirmed live.)
- [VALIDATED] SPARK `explode(DETECTION_LIST)` drops findings-less hosts; assets derived post-explode. (Confirmed by reading parser.)
- [VALIDATED] Collector B publishes the SAME `{HOST_DETAILS, DETECTION_LIST}` combined feed as A (emit code at QualysCollector.cs ~line 602-627 / 974-977; stores per-host compound JSON to S3 batch or stream writer). Same shape the SPARK parser consumes. Parity is meaningful; the parser fix covers both collectors' output. (Confirmed by discovery 2026-06-09.)
- [OPEN] Does the host-list API on a real (non-lab) tenant return insertable identity (NETBIOS|IP) for never-scanned / findings-less hosts, or are those sparse? Unmeasured (lab tenant had 0 findings-less hosts). → affects how to handle identity-less hosts.
- [OPEN] Whether the FQDN fix (`DNS_DATA.FQDN` → DNS fallback) is in-scope for this task or deferred. Treated as in-scope-optional; confirm with user if it expands parser risk.
- [VALIDATED] Collector B has NO resume/checkpoint (does not implement IResumableAdapter; returns CollectionStats). No checkpoint interaction to preserve; page-streaming there is purely a bounded-memory improvement. AdaptiveConcurrencyLimiter is long-lived per run — unaffected by streaming. (Confirmed by discovery 2026-06-09.)
- [REJECTED] "Add a separate CollectAssetsAsync flow" (suggested by discovery agent E2) — contradicts the signed decision to keep ONE combined feed and left-join. Do not add an assets flow in either collector.
- [OPEN] Are there downstream consumers (asset dedup, finding linkage) that assume "every emitted host has ≥1 finding"? If so, admitting findings-less assets could surface latent bugs. → flag during verification.
