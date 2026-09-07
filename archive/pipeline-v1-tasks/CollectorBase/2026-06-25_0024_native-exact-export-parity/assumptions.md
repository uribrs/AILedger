# Assumptions

- **A1** — Native parity target is the per-collector output **byte-shape** of `DefenderVmRecordFormatter` /
  `TenableIo*ChunkProcessor` (the record emitted to egress), NOT the upstream parser's post-ingest schema.
  Status: VALIDATED (user chose "native-exact envelope"; native source read this session).

- **A2** — `verbatim` mode must inject neither `sourceType` nor `correlation_keys`, because native Tenable
  emits the record untouched. Existing mapped mode keeps injecting both. Status: VALIDATED (against
  `TenableIoAssetsChunkProcessor` — "no remapping").

- **A3** — In passthrough/envelope modes, `records_path` still selects the record array; the WHOLE element
  is emitted (vs `fields` subset). `mapping.fields` is ignored when an envelope/passthrough mode is set.
  Status: OPEN — confirm the mapper seam cleanly supports "keep element" vs "project fields" during execution.

- **A4** — `typed_wrapper`/`source_type_prefix` records do NOT need correlation keys injected by us — the
  upstream parser derives them from the raw fields (native injects none beyond the discriminator).
  Status: OPEN — confirm no current consumer depends on our injected `correlationKeys` for these streams.

- **A5** — Envelope metadata templating (`recommendation_reference: "{{rec_id}}"`) resolves from the same
  token scope available to the inner `for_each` sub-step (`{{<as>}}` etc.). Status: OPEN — confirm the
  template/token plumbing reaches the emit/envelope step.

- **A6** — Watermark advance (`watermark_field`) is independent of emit shape: it reads the record field
  regardless of envelope, so passthrough does not break watermark/pagination. Status: OPEN — confirm against
  the runner's watermark block (it currently reads from the MAPPED record `rec[watermark_field]`, which in
  passthrough mode may need to read the raw element instead).

- **A7** — Defender base-date floor comes from a RUN-envelope / Runner input (e.g. `base_date`/`since`)
  already wired for other vendors; the YAML references it as `{{input.*}}`/`{{config.*}}`. Status: OPEN —
  confirm the exact input key our Runner/RUN-envelope supplies for Defender's `lastSeen`/`sinceTime`.

- **A8** — Native Defender base-date special case (`GetAssetsBaseDate`: if base == now-365d ⇒ now-180d) and
  the 14-day vuln-changes lookback are **value/threshold logic** expressible via templated dates/inputs, not
  new control flow. Status: OPEN — if they require computation the templating can't express, record as a
  documented parity gap rather than adding engine code (resist inner-platform creep).

- **A9** — The 3 deferred vendors (cortex-xdr, crowdstrike-falcon, qualys) keep the mapped-subset mode and
  continue to pass their existing tests unchanged. Status: VALIDATED (scope decision).
