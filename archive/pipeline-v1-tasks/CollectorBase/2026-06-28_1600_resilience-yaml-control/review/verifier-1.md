# Verifier-1 — resilience YAML control panel + watermark seed + runs anchor

**Overall: PASS.** Build clean, 92/92 pass (88 prior + 4 new), no existing test modified to pass, no regression. B1 went REJECTED-and-documented (no faked knob); the no-regression null gate is genuine.

## Task 1 — build + tests
- `dotnet build CollectorBase.slnx` → `Build succeeded. 0 Error(s)` (12 NU1507 warnings, pre-existing/benign package-source-mapping notices). **PASS**
- `dotnet test CollectorBase.slnx` → `Passed! Failed: 0, Passed: 92, Skipped: 0, Total: 92`. **PASS**
- The 4 new tests exist with the exact required names (CollectorExecutorTests.cs): `WatermarkSeed_FreshRun_SeedsFloorFromInput` (:1010), `WatermarkSeed_Resume_UsesPersistedWatermark_IgnoresSeed` (:1028), `SessionFactory_ResilienceKnobsPresent_PopulateSessionSpec` (:1058), `SessionFactory_ResilienceKnobsAbsent_LeaveSessionSpecNull_SharedDefaultPreserved` (:1076). **PASS**
- No existing test was reshaped to pass: pre-existing neighbors intact (`CursorWatermark_DepthCapReset_CollectsAll_NoDuplicates` :992, `CursorExpiry_ReactiveReset_FromVendorError_CollectsAll` :1090). The 88→92 delta is purely additive. (Repo is not a git checkout per env, so no diff confirmation — verified by reading the surrounding code.) **PASS**

## Task 2 — Success Criteria 1–6
- **SC1** (build clean + new tests) — PASS (Task 1).
- **SC2** (A: fresh seeds `{{watermark}}`, resume ignores seed) — PASS. FetchStepExecutor.cs:42-47 seeds only when `runCtx.Watermark` is null/empty; tests assert fresh→`wm=2026-01-02` + 2 records (:1022-1024) and resume→`wm=2026-01-03` with explicit `DoesNotContain wm=2026-01-02` + 1 record (:1050-1052).
- **SC3** (B2: present→populated, absent→null, unit-tested) — PASS. SessionFactory.cs:46-65; tests :1066-1072 (7/9/11 refill/cap/queue, 4 fails/45s) and :1085-1086 (both null).
- **SC4** (B1: maps real strategy IF supported else documented; verifier states outcome) — PASS. Outcome below.
- **SC5** (C: runs root → repo root; `--out`/`CS_OUT` still wins) — PASS. Program.cs:107-109, ResolveRepoRoot() :186-195.
- **SC6** (docs + falcon seeded) — PASS. capabilities.md resilience section (:166-220) + yaml-contract.md (:83, :191-199); crowdstrike-falcon.yaml seeds `watermark_seed: "{{input.base_date}}"` on assets (:33) and findings (:62).

## Task 3 — NO-REGRESSION GATE (critical) — PASS
- CollectorExecutorSessionFactory.cs:46-58 — `RateLimiter = retry.RateLimit is { } rl ? new RateLimiterOptions{...} : null`. Line 59-65 — `CircuitBreaker = retry.CircuitBreaker is { } cb ? new CircuitBreakerOptions{...} : null`. Conditional null, NOT a fabricated default. Confirmed by comment at :44-45 ("only set when the profile declares them … stay null and DefaultSessionFactory substitutes the Shared machine-profile defaults").
- `grep -rn "rate_limit|circuit_breaker" integrations/` → **NONE FOUND** across all profiles. The unit test `SessionFactory_ResilienceKnobsAbsent_...` asserts both null on a no-knob profile. Existing-profile behavior is byte-identical.
- Note (not a defect): there are now **6** integration profiles (guardicore.yaml added since CLAUDE.md/contract wrote "5"); none declare the new resilience knobs, so the count discrepancy has no bearing on regression.

## Task 4 — WATERMARK SEED fresh-vs-resume — PASS
- FetchStepExecutor.RunAsync:38 sets `runCtx.Watermark = seed.Watermark` (resume value, if any). :42 guards `if (string.IsNullOrEmpty(runCtx.Watermark) && !string.IsNullOrWhiteSpace(step.Pagination.WatermarkSeed))` — seed applied ONLY when no resumed watermark; a resumed `seed.Watermark` is never overridden.
- Seed rendered via the template engine: :45 `TemplateRenderer.Render(step.Pagination.WatermarkSeed, seedCtx)` over `BuildContext(inputs/config)`.
- Tests assert correctly: fresh → first `/things/query` carries `wm=2026-01-02` and yields 2 records (seeded floor filters out 2 of 4); resume (CheckpointState.Watermark = `2026-01-03`) → `wm=2026-01-03`, `DoesNotContain wm=2026-01-02`, 1 record. The seed and persisted values are distinct (01-02 vs 01-03), so the resume assertion genuinely proves the seed is ignored.

## Task 5 — B1 OUTCOME — REJECTED-and-documented (no code change, no faked knob) — PASS
- assumptions.md A1: REJECTED, verified against Cymulate.Http.Package.Session 2.0.2 — `RetryOptions` exposes only getters (`get_RateLimitDelaySource`/`get_DelaySource`); no settable header/rate-limit strategy enum; `KnownIndustryHeaders`/`RetryAfterOnly` not in the constructable surface.
- SessionFactory.cs:29 & :35 — `server_delay_strategy` only flips `RespectRetryAfterHeader` and `ExternalizeServerSuggestedDelays` via `retry.ServerDelayStrategy != "disabled"`. No fabricated strategy enum is set. Matches A1.
- Docs match the code: yaml-contract.md:199 "`server_delay_strategy` is honor-vs-`disabled` ONLY; it does **not** select a header-parsing strategy"; capabilities.md:194 same truth ("any other value just means 'honor' … does not select a header-parsing strategy … fixed and not exposed"). No faked knob anywhere.

## Task 6 — Runner repo-root resolve — PASS
- Program.cs ResolveRepoRoot() :186-195 walks up from `Directory.GetCurrentDirectory()` then `AppContext.BaseDirectory` to the first dir containing `CollectorBase.slnx`; falls back to CWD if not found.
- Default runs root :107-109 — `Opt("out","CS_OUT") is {Length:>0} ? Path.GetFullPath(outOpt) : Path.Combine(ResolveRepoRoot(), "runs")`. `--out`/`CS_OUT` override preserved and resolved relative to CWD.

## Issues
None blocking. Cosmetic only: the test `WatermarkSeedProfile` uses `next_token_at` (vs `next_token_path` elsewhere) — irrelevant, the suite is green. The "5 profiles" wording in the contract/constraints is now 6 (guardicore) but none carry the new knobs, so the regression claim holds.
