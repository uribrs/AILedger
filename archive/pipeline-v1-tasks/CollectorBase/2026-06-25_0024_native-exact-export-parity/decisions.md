# Decisions

- Parity target = native-exact output byte-shape per collector (user choice over uniform passthrough).
- Engine gains a verbatim full-record passthrough mode + a per-step envelope selector; mapped-subset stays default.
- Envelope modes: `verbatim`, `typed_wrapper` ({type,data}), `source_type_prefix` ({sourceType,...record}) — names mirror native formatter shapes.
- `verbatim` injects nothing (no sourceType, no correlation keys) — matches native Tenable.
- Do NOT create a Defender pagination strategy — `next_url` already replicates native OData behavior; complete the profile instead.
- Defer cortex-xdr / crowdstrike-falcon / qualys to a later task; they remain on mapped-subset mode.
- Vendor specifics live only in YAML; the engine stays generic.
- If native base-date/lookback logic can't be expressed via templating, document it as a parity gap rather than adding runner code.
