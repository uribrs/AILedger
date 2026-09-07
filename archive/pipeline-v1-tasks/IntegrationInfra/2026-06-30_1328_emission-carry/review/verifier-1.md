# Verifier-1 Report — Emission Carry

Verifier: independent re-run. Method: normalized verbatim diffs vs source, residual-ref greps,
held-value diffs, fresh `dotnet build` + `dotnet test`, git/mtime checks. Nothing fixed.

## Overall verdict: PASS (1 minor PARTIAL on XML-doc letter, resolved in favor of the dominating verbatim constraint)

All 7 success criteria met. Build clean (0 errors), 78 tests pass (33+20+10+15), source repo
unmodified, Kernel untouched, no bogus package, D8 renames consistent, held values unchanged,
JSON-validator invariant preserved, no ISink reshape performed.

---

## Criterion-by-criterion

### 1. 18 units relocated, substructure mirrored, behavior VERBATIM — PASS
- 17 Egress `.cs` + `CollectorGlobalDefaults` = 18 carried units, all under `Emission/` with
  `Multipart/`, `Ndjson/`, `Telemetry/` mirrored.
- Normalized diff (stripping `using`/`namespace` lines, applying the Shared->Kernel/Emission namespace
  rewrites and the 3 D8 renames as token substitutions) of ALL 18 pairs returned **OK verbatim** for
  every file. No logic drift. Only namespace rewrites + Kernel rewires + D8 renames differ, exactly as
  required.

### 2. Kernel rewires + no residual Shared.* + defaults out of Glossary — PASS
- `grep "Cymulate.Integration.Adapters.Shared"` under `src/` => **none**.
- Emission `.cs` import `Cymulate.IntegrationInfra.Kernel.Exceptions` (3x) and `.Kernel.Telemetry` (3x).
- No `DataPipeline.Json` / `Kernel.Json` reference in Emission (uses `System.Text.Json` directly) — A-json
  correctly N/A.
- No `Glossary` directory under target `src/`; defaults live at `Emission/AdapterGlobalDefaults.cs`.

### 3. D8 renames consistent + held values unchanged — PASS
- All three types renamed (type decls, filenames, and all internal refs: `AdapterNdjsonPublisher` references
  `AdapterGlobalDefaults.*`; `AdapterOutputDefaults` references `AdapterGlobalDefaults.*`).
- No residual `Collector*` D8 **type** identifiers in any `.cs`. Remaining "Collector" hits are: (a) English
  prose comments in OTHER concerns (FaultGovernance/Conversation) and one `<see cref="AdapterTopics.Collector"/>`
  prose comment — none are the renamed types; (b) README source-path/history references (expected, documents
  the rename). Acceptable.
- Held-value diff (const/static-readonly declarations) of source `CollectorGlobalDefaults` vs target
  `AdapterGlobalDefaults` => **IDENTICAL**. Confirmed: content-type `"application/x-ndjson"`, suffix `".json"`,
  `DefaultMaxBytesPerBatch = 50L*1024*1024`, `FindingsFileName "findings"`, `AssetsFileName "assets"`,
  lookback 365, buffer 64*1024, capacity 1024*1024, `Utf8NoBom` (no BOM). `BuildPageTargetPath` D6 format +
  trim logic verbatim.

### 4. No bogus package; Kernel not modified — PASS
- `Directory.Packages.props`: **no** `Microsoft.Extensions.Configuration` PackageVersion added. csproj has
  no invented PackageReference (only pre-existing SDK/Http.Session/OpenTelemetry/MS.Extensions.Http). MS.Extensions
  resolved transitively as the executor reported.
- `git diff --stat src/IntegrationInfra/Kernel` => **empty**; no untracked files under Kernel. Kernel untouched.

### 5. README extended — PASS
`Emission/README.md` covers: carried set (engine + sessions + writers + formatters + options + GC snapshot),
the single-authoritative-JSON-validator invariant (ResultsRecordFormatter), egress invariants, Kernel rewires
(Exceptions, Telemetry), the 3 D8 renames with note that held values are unchanged, and the Deferred section
(ISink/StreamKind seam, static-entry kill, assets/findings hardwiring as reshape candidates).

### 6. Tests genuine + build/test pass — PASS
- `dotnet build IntegrationInfra.slnx`: **Build succeeded, 0 Errors** (10 NU1507 warnings = pre-existing
  dual-package-source config noise, unrelated to this carry).
- `dotnet test IntegrationInfra.slnx`: Conversation 10, FaultGovernance 20, Kernel 33, **Emission 15** =
  **78 passed, 0 failed**.
- Emission tests are genuine, not vacuous: `ResultsRecordFormatterTests` (minify, trim+single-line, empty=>empty,
  invalid JSON throws via `ThrowsAny<JsonException>`) and `EmissionDefaultsTests` (asserts concrete held values:
  content-type, suffix, 50MB cap, UTF8-no-BOM preamble empty; exercises `BuildPageTargetPath` zero-pad,
  slash-trim, whitespace-name guard => ArgumentException, non-positive-page guard => ArgumentOutOfRangeException).
  Theory-driven where appropriate. DI/streaming publisher seams correctly left untested per A-test.

### 7. Source repo NOT mutated — PASS
- Source is not under git. mtimes: newest source `.cs` = 2026-05-27; `CollectorGlobalDefaults.cs` = 2026-04-26.
  All predate today (2026-06-30). No source file modified.

---

## Invariant spot-checks (explicitly requested)
- **Single authoritative JSON validator preserved:** `ResultsRecordFormatter` carried verbatim (internal static,
  `JsonDocument.Parse` + minified serialize; invalid JSON throws). Exposed to tests via
  `InternalsVisibleTo("IntegrationInfra.Emission.Tests")` only (test-only, no runtime effect). No second
  validation path introduced.
- **No ISink reshape performed:** static entry `public static class AdapterNdjsonPublisher` present with
  `PublishFindingsUtf8PageAsync` / `PublishAssetsUtf8PageAsync` intact; `FindingsFileName`/`AssetsFileName`
  constants present and still hardwired in the publisher (verbatim). Reshape flagged in README Deferred section.

## XML-doc coverage — honest judgment (PARTIAL on the letter of Criterion 5)
- Type-level XML docs present on relocated public types. However, most public **methods/members**
  (constructors, `WriteRecordAsync`, `FlushAsync`, `DisposeAsync`, `PublishAsync`, batch-session methods,
  options `FromConfiguration`/`Resolve`/`ValidateOrThrow`, const fields, etc.) have **no** `///` doc.
- Verified this is INHERITED FROM SOURCE: the source files document types but leave the same members
  undocumented (e.g. source `ResultsStreamWriter`, `NdjsonBatchSession`, `ResultsBatchPublisher`). The executor
  carried docs verbatim — it did NOT strip them.
- This is the documented tension: criterion 5 says "XML docs on every public member" but constraints.md (line 3)
  and criterion 1 mandate **verbatim** carry. The executor chose verbatim (execution_notes S5: "proportional
  stance"). I judge this correct: the verbatim constraint dominates; adding docs would mean editing carried
  bodies. Newly-authored public members (the defaults type's `BuildPageTargetPath`) ARE documented.

## Must-fix items
- None blocking. Build clean, tests pass, all hard criteria met.

## Notes for PR review (not defects)
- D8 rename (Collector*->Adapter*) is the headline naming decision — flagged for human PR review per D3.
- ISink/StreamKind reshape + static-entry kill + assets/findings DIP/OCP rework = deferred reshape, correctly
  carried verbatim and documented.
- Member-level XML-doc sparseness is inherited from source; if criterion 5's letter is to be enforced strictly,
  that is a follow-up doc pass, not a carry defect.
