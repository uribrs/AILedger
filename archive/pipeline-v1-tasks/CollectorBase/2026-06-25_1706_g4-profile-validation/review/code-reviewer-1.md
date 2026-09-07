# Code Review — g4 profile validation (ValidatePagination + `__`-guard extension)

Scope reviewed: `CollectorExecutor/Profile/Profile.cs` (validation region) and the four
`Validate_*` tests in `Tests/CollectorExecutor.Test/CollectorExecutorTests.cs`. Read against the
6 shipped `integrations/*.yaml` and the strategy registry.

## Verdict
Functionally correct for the shipped profiles and the documented contract. One real
inconsistency worth fixing (case-sensitivity vs. the registry); the rest are minor/nits.

---

## MINOR

### M1 — `ValidatePagination` switch is case-sensitive; the strategy registry is case-insensitive
`Profile.cs:288` switches on the raw `pagination.Strategy` string with exact-case labels
(`"cursor"`, `"cursor_watermark"`, `"next_url"`). The runtime registry that actually resolves
these names compares case-insensitively — `StrategyRegistry.cs:13` uses
`new(StringComparer.OrdinalIgnoreCase)`, and `HasPaginator` (`RunPreparer.cs:44`) gates on that.

Consequence: a profile written as `strategy: Cursor` or `strategy: NEXT_URL` would
(a) pass `ValidatePagination` unconstrained (no required-field check fires) yet (b) resolve and
run at runtime via the case-insensitive registry — so a `Cursor` strategy with no `next_token_at`
sails through validation and only misbehaves at run time. The whole point of this change is to
fail-closed on missing token paths; the case gap is a hole in exactly that guarantee.

Every shipped YAML uses lowercase, so no current profile is affected — hence MINOR, not MAJOR.
Fix: normalize before switching, e.g.
`switch (pagination.Strategy?.ToLowerInvariant())` (or `Trim().ToLowerInvariant()` to also match
the registry's behavior, then keep lowercase case labels). This makes the validator agree with the
resolver.

### M2 — `cursor` (plain) required-field check is now enforced where the single-fetch sugar path previously had none
The task says two inline cursor checks were replaced by the helper. Confirmed both call sites now
route through `ValidatePagination`: the sugar path at `Profile.cs:264` and `ValidateFetchStep` at
`Profile.cs:278`. The `cursor` arm (`Profile.cs:290-293`) preserves the original
`next_token_at`-required semantics. No behavior lost. Worth a deliberate check against shipped
profiles: every `strategy: cursor` / `cursor_watermark` / `next_url` occurrence in the 6 YAMLs
supplies `next_token_at` (and `watermark_field` for `cursor_watermark`), so none of them regress:
- crowdstrike-falcon.yaml:29-32, 57-61 — cursor_watermark with both fields. OK.
- defender-vm.yaml:33,41,55,65,73,88 — next_url with next_token_at. OK.
- qualys.yaml:27,45 — next_url with next_token_at; :63 none (unconstrained). OK.
- cortex-xdr.yaml:34 offset, :49/:66 none — all unconstrained. OK.
- tenable-io.yaml — only `none`. OK.
- guardicore.yaml:35 offset — unconstrained. OK.
No legitimately-valid shipped profile is rejected. (Informational, not a defect.)

---

## NIT

### N1 — Negative tests assert on message substrings, not exception identity
`Validate_CursorWatermark_MissingFields_Rejected` (test:1330) and
`Validate_NextUrl_MissingNextToken_Rejected` (test:1349) catch `InvalidOperationException` and
assert `Contains("next_token_at is required for cursor_watermark"/"...next_url")`. These substrings
are specific enough that the assertion proves the *intended* rule fired, not some other validation
error — good. `Validate_AccumulateListReservedKey_Rejected` (test:1308) asserts only
`Contains("reserved")`, which is adequately specific (only the `__`-guard emits "reserved").
Positive test `Validate_ValidWatermarkAndNextUrl_Load` (test:1366) asserts
`ProfileLoader.Load(...).Vendor == "demo"`, which proves load returns a profile (no throw) — a real
positive. Test quality is fine; the nit is only that none assert the *absence* of the opposite-arm
message (e.g. the watermark test could also assert it does NOT contain a `next_url` message), which
would harden against a future copy-paste switch bug. Optional.

### N2 — `accumulate_list` `__`-guard extension is correct
`Profile.cs:215-219` now chains `Capture`, `CaptureList`, and `AccumulateList` keys through the
same `.Concat(...)` / `StartsWith("__", Ordinal)` check, identical semantics across all three. This
is the right extension — `poll_and_drain` rides `__drained_<idx>` on the accumulate/capture-list
space, so an `accumulate_list: { __drained_1: ... }` collision is exactly what must be rejected, and
test:1308 exercises it. Correct.

---

## Strategy-coverage confirmation
`none` / `offset` / `page_number` are intentionally absent from the switch → unconstrained, which
matches the registered paginator names (`StrategyRegistryDefaults.cs:30-35`: `cursor`, `offset`,
`page_number`, `none`, `next_url`, `cursor_watermark`). No `"single_page"` vs `"none"` mismatch in
the validator — `SinglePageStrategy.Name => "none"` (`Strategies/Pagination/SinglePageStrategy.cs:14`),
and the validator never references `single_page`. (Note: an existing, out-of-scope test at
test:3233 writes `strategy: single_page`; that name is not registered and would fail
`HasPaginator` at runtime, but it is unrelated to this change and not part of the reviewed diff.)

## Summary
No BLOCKER/MAJOR. The one substantive item is M1 (case-sensitive validator switch vs.
case-insensitive registry) — a small fail-closed gap that no shipped profile triggers but that
contradicts the change's own intent; a one-line `ToLowerInvariant()` normalization closes it.
Everything else is confirmation that the helper, the call-site routing, the `__`-guard extension,
and the tests are correct.
