# Verifier Report — Retire field-subset `mapped` emit mode

Verdict: **PASS** (with two minor hygiene gaps — non-blocking).

Build: `0 Error(s)`. Tests: `Passed! Failed: 0, Passed: 58, Skipped: 0, Total: 58`.

## Success Criteria

### SC1 — Build clean — PASS
`dotnet build CollectorBase.slnx` → 0 errors. Seam signature simplification (`IRecordMapper.Map(response, mapping)` 2-arg) compiles across `DotPathMapper`, runner call sites (CollectorExecutorRunner.cs:256/265/270), and Seams.cs:84.

### SC2 — Tests pass; mapped-subset rewritten to full-record passthrough — PASS
- 58/58 green.
- `Emit_DefaultMode_PassesFullRecordThrough` (Tests/...Tests.cs:1552) asserts NO `emit_mode` → verbatim full record, exact key set `{id,name,nested,score}`, no `sourceType` injected. Meaningful.
- `Emit_Verbatim_PassesFullRecordThrough_NoDiscriminator` (1439) asserts full key set + nested object survival + no discriminator.
- `Emit_TypedWrapper_WrapsFullRecordUnderData` (1459), `Emit_SourceTypePrefix_FlattensRecordWithTemplatedMetadata` (1477), `Emit_SourceTypePrefix_EnvelopeWinsOnKeyCollision` (1498), `EmitMode_Validation_RejectsMisuse` (1515) all present and strong (collision → envelope wins; verbatim+type_value rejected; wrapper without type_value rejected on the sugar path).
- Falcon findings retry test (`ProcessAsync_Findings_HonorsRetryAfterInProcess_Emits3Records`, :570) now asserts `page` contains raw `"aid"` — full hydrated record published untouched. Genuine passthrough assertion, not weakened.

### SC3 — No mapped/Fields/correlation injection; default verbatim; Map always full record — PASS
- `ResponseMapper.Map` (Interpreter.cs:92-99): deep-clones each `JsonObject` at `records_path`, no projection/sourceType/correlation. Always full record.
- `MappingSpec.EmitMode` default = `"verbatim"` (Profile.cs:159). No `Fields`, no `CorrelationKeys`, no `SourceType` on the model.
- `StreamSpec`/`StepSpec` carry no `CorrelationKeys`/`SourceType`.
- `ValidateMapping` (Profile.cs:266-283) accepts only `verbatim|typed_wrapper|source_type_prefix`; `type_value` required for wrapper/prefix and rejected on verbatim; `envelope_fields` only for source_type_prefix; no correlation requirement. `mapped` not accepted anywhere.
- Code/yaml grep for `mapped` / `.Fields` / `CorrelationKeys` / `SourceType` symbol → no residue in CollectorExecutor or Strategies (only false positives: `mappedFailurePolicy:` in Strategies/Resilience — unrelated Shared API param).

### SC4 — All 5 shipped profiles validate + emit right shape; dead keys stripped — PASS
Inspected all `integrations/*.yaml`:
- tenable-io: verbatim (both streams, `records_path: "$"`).
- crowdstrike-falcon: verbatim (assets + spotlight findings).
- qualys: verbatim (host/detection/KB).
- defender-vm: `typed_wrapper` (machine/software) + `source_type_prefix` (inventory/delta/recommendationCatalog/recommendationScopedVulnerability with `envelope_fields`).
- cortex-xdr: `source_type_prefix` endpoint + va_cves.
No `fields:` / `correlation_keys:` / dead `source_type:` keys remain in any shipped profile. `type_value` used correctly throughout.

### SC5 — Docs updated; no regression (poll-and-drain, conformance, envelope, `__`-guard) — PASS
- yaml-contract.md mapping section (75-93) rewritten to passthrough-only: "FULL record, untouched. There is no field projection, no sourceType injection, no correlation-key injection." Envelope table accurate. No stale `mapped`/`fields`/`correlationKeys`.
- architecture.md: no stale terms.
- Regression coverage intact in the 58-test suite: poll-and-drain, ResumeAsync/checkpoint, body_retry_after defer, reserved `__`-capture guard (Profile.cs:197-199 retained), envelope feature (4 tests), bus/page-counter conformance — all green.
- `ApplyEnvelope` (StepHelpers.cs:216-248) unchanged: discriminator-wins-on-collision preserved (`if (!output.ContainsKey(...))`, line 242).

## Bugs / Gaps (ranked)

1. **MINOR (hygiene) — `Runner/DefaultProfiles.cs` still uses the retired schema.** Lines 22/36-37/39/53-54 carry `source_type:`, `fields:`, `correlation_keys:`. These are now silently dropped by `IgnoreUnmatchedProperties`, so the built-in Falcon runner profile still **loads and validates** (no `emit_mode` → verbatim default → no validation error), but the intended `crowdstrike_device`/`crowdstrike_vulnerability` sourceType discrimination is now lost — it emits verbatim. Not in the contract's changed-file list and non-breaking, but it is stale dead config in a shipped runtime path. Recommend stripping `fields:`/`correlation_keys:` and, if sourceType stamping is still wanted for the local runner, converting to `emit_mode: source_type_prefix` + `type_value:`.

2. **TRIVIAL (hygiene) — Test fixtures retain dead keys.** ~30 fixtures in CollectorExecutorTests.cs (e.g. :1706-2422) still carry `source_type:` / `fields: { id: id }` / `correlation_keys: [id]`. Tolerated by `IgnoreUnmatchedProperties`; tests pass and assert passthrough, so behavior is correct, but the dead keys are misleading. Contract only mandated stripping `integrations/*.yaml` (done), so this is out of strict scope — cosmetic only.

Neither gap affects correctness of the engine, the shipped profiles, or any success criterion. The `mapped` mode is genuinely gone from the engine, model, validator, shipped profiles, and docs; passthrough is the only behavior; the envelope is the sole knob.
