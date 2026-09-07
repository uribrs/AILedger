# Decisions

- Target the EA promotion boundary (enrich-table column sets), not today's staging shape — staging is upstream of the contract we must honour, so it is ours to drop.
- Id derivation in the normalizer, not the collector — changing the derivation must never require re-releasing a collector.
- Derived id scope is `(instance_id, vendor parent key)` — cross-run stability would collapse EA's generations, which are demoted via `latest = false` keyed on `(value, type)` per flow.
- Falcon parent key = stated-or-derived AID, else the Discover combined `id` — mirrors `ExtractRecordKey`; recall shows 254 of 303 lab assets state no AID.
- Prototype input is the thin collector's own output; S3 samples are shape reference only — the operator ruled out modelling on their correlated envelope.
- Unit of work for the prototype is the batch folder the thin collector emits, since we own both ends. The page-file-vs-batch-folder question stays open for real collectors and is not settled here.
- Manifest carries no vendor name — the scope ids already identify the connector, and a vendor field would invite a per-vendor branch.
- Unknown label-contract version is a hard rejection, never a coercion or a best-effort parse.
- CVE explode happens before the boundary, in the normalizer — forced by `cve_id NOT NULL`.
- Proceeding on unverified: the enum members needed for validation are obtainable without a live cluster. If wrong: the generator needs a live `pg_catalog` read, or the enum sets get hand-seeded once and flagged as the single hand-authored part of the contract.
- Proceeding on unverified: the S3 Tenable samples are enough to sanity-check that a second vendor shape fits the same normalizer with no code change. If wrong: that check is dropped from the prototype and recorded as untested, rather than a Tenable tenant being sought.
- Failure policy (reject batch vs quarantine rows) is deliberately NOT decided here. The prototype fails the whole batch, which is the safe default; the real choice is the operator's and belongs to the design, not the prototype.
