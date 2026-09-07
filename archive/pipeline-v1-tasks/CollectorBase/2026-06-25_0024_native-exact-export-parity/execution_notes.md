# Execution Notes — native-exact export parity + complete Defender profile

## Engine
- `Profile/Profile.cs` — `MappingSpec` gained `emit_mode` (mapped | verbatim | typed_wrapper |
  source_type_prefix), `type_value`, `wrapper_key`/`data_key` (defaults type/data), `envelope_fields`.
  `PaginationSpec` gained `allow_offset_fallback` (default true). `ValidateFetchStep`: validates emit_mode;
  requires `type_value` for wrapper/prefix; relaxes correlation_keys requirement to `mapped` mode only.
- `Interpreter/Interpreter.cs` — `ResponseMapper.Map`: non-`mapped` modes emit the FULL raw element
  (DeepClone) with NO sourceType/field-projection/correlation injection. `mapped` unchanged (default).
- `Execution/CollectorExecutorRunner.cs` — new `ApplyEnvelope` wraps records for typed_wrapper /
  source_type_prefix, resolving the templated discriminator + `envelope_fields` against the page `ctx`
  (so `{{rec_id}}` resolves inside for_each). Applied AFTER the watermark read (bare element) and BEFORE
  slicing. `verbatim` needs no wrapping. source_type_prefix drops pre-existing dup keys (native AddMetadata).
- `Strategies/Pagination/NextUrlPaginationStrategy.cs` — honors `allow_offset_fallback: false` (stop when
  no nextLink instead of offset-advancing) for nextLink-only endpoints.

## Resolved OPEN assumptions
- A3: mapper "keep element" seam = emit_mode branch in Map (returns the full element for passthrough modes).
- A5: envelope metadata templating uses the runner's per-page `ctx` (incl. for_each `{{rec_id}}`) — confirmed.
- A6: watermark reads the record's top-level field; under passthrough the record is the full raw element so
  the field is present; wrap happens AFTER the watermark read. No breakage.
- A7: base-date input key = `{{input.base_date}}` (ISO), supplied by the Runner from `--base-date`.
- A8: native base-date remap (365⇒180) + 14-day vuln-changes floor are computed date-logic NOT expressible
  in templating → documented as parity gaps in parity-note.md; no runner control flow added (per decision).
- A4: passthrough/wrapper records inject no correlation keys (native injects none) — validation relaxed.

## Profiles
- `integrations/tenable-io.yaml` — findings+assets chunk fetch → `emit_mode: verbatim` (full record, no
  wrapper/sourceType), matching TenableIo*ChunkProcessor.
- `integrations/defender-vm.yaml` — machines/software → `typed_wrapper` (machine/software); vulns→inventory,
  changes→delta, recommendations→recommendationCatalog, recVulns→recommendationScopedVulnerability (+
  `recommendationReference: {{rec_id}}`), all `source_type_prefix`. Completed to native scope: machines
  `$filter=lastSeen ge {{input.base_date}}`, new vuln-changes stage (SoftwareVulnerabilityChangesByMachine,
  sinceTime), correct native endpoints; vulns/changes `allow_offset_fallback: false`.

## Tests (Tests/CollectorExecutor.Test) — 56/56 green (52 prior + 4 new)
- `Emit_Verbatim_PassesFullRecordThrough_NoDiscriminator` — full record (incl. nested), no sourceType.
- `Emit_TypedWrapper_WrapsFullRecordUnderData` — exactly `{type,data}`, full record under data.
- `Emit_SourceTypePrefix_FlattensRecordWithTemplatedMetadata` — sourceType + templated recommendationReference + flattened record.
- `Emit_Mapped_DefaultMode_StillProjectsSubset` — mapped mode unchanged (subset, no passthrough).
- `/emit/recs` mock endpoint added (multi-field records incl. nested object).

## Commands
- `dotnet build CollectorBase.slnx` → clean.
- `dotnet test CollectorBase.slnx` → 56/56 pass.

## Post-review repairs (verifier-1 PASS; code-reviewer-1 — 2 major, fixed)
- **M2 — sugar path unvalidated**: the single-fetch `streams.<x>` path never checked emit_mode (a sugar
  profile `typed_wrapper` w/o `type_value` would emit `{"type":""...}`). Fixed → extracted `ValidateMapping`,
  called from BOTH the `steps` fetch path and the sugar path.
- **M1 — silently-ignored knobs**: `type_value`/`envelope_fields` on `mapped`/`verbatim` were dropped at
  runtime. Fixed → `ValidateMapping` now rejects `type_value` outside typed_wrapper/source_type_prefix and
  `envelope_fields` outside source_type_prefix.
- Test strength: strengthened `Emit_SourceTypePrefix` to assert the EXACT key set; added
  `Emit_SourceTypePrefix_EnvelopeWinsOnKeyCollision` (discriminator/metadata override colliding record keys —
  the subtlest `ApplyEnvelope` line) and `EmitMode_Validation_RejectsMisuse` (locks M1+M2). Suite 58/58.
- Accepted (verifier cosmetic notes, no fix): O1 `ApplyEnvelope` key-skip is case-sensitive vs native's
  case-insensitive — irrelevant for Defender's canonical-lowercase keys; O2 Defender software/recs page-1
  templates a no-op `$skip=0` (subsequent pages are nextLink-driven, identical to native).

## Deferred / residual
- cortex-xdr, crowdstrike-falcon, qualys profiles remain on `mapped` subset mode (not yet native-exact) — see parity-note.md.
- Defender base-date 180d-remap + 14d lookback clamps are documented parity gaps (computed date-logic).
- No live Defender run (no creds); parity proven structurally via tests + native source citations.
