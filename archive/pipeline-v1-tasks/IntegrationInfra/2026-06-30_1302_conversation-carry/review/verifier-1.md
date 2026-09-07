# Verifier-1 — Conversation Carry

**Verdict: PASS** (1 of 7 criteria PARTIAL — defensible, not a defect)

Independent verification of TASK-20260630-1302 against the 7 Success Criteria in `prompt_contract.md`.
Build and tests re-run by the verifier; diffs taken against the source of truth.

---

## Per-criterion findings

### 1. 15 flat Session files + 3 cert handler relocated, behavior VERBATIM — **PASS**
- Source has **16** flat `Session/*.cs`; excluding `LogRedaction.cs` = **15** carried. Produced flat dir
  has 16 = those 15 + `AdapterSessionLifecycle` (the reclaim). Correct. (The contract's "18" counted the
  `TransportErrorHandling/` subdir; actual flat count is 16.)
- Body-diff (stripping `using`/`namespace` lines) of all 14 non-AdapterHttpClient flat files + 3 cert
  handler + 2 helpers + lifecycle: **byte-identical bodies**.
- **`SessionHandle.cs`**: body byte-identical modulo the single `namespace` line. Dispose ordering verbatim.
  Confirmed.
- **`AdapterHttpClient.cs`** is the only file whose body differs — by exactly **one added blank line**
  (whitespace, no logic change). Its `using` rewrites are precisely the expected Kernel rewires
  (`TransportErrorHandling`→`Kernel.Transport`, plus `Kernel.Redaction` + `Kernel.Exceptions` added).
- No `Collector`-prefixed **type/member identifiers** anywhere. The word "collector" survives only in
  verbatim XML-doc prose / one error string — a generic role-noun, not an identity-bearing type name, so
  D3 neutralization is not triggered. Correct.

### 2. AdapterSessionLifecycle reclaimed; RateLimiterHelper + HttpStatusExtractor carried — **PASS**
- `AdapterSessionLifecycle.cs` present under `Conversation/`, body verbatim; source copy still in
  `Orchestration/` (reclaim = copy, source untouched).
- Both helpers under `Conversation/Helpers/`, bodies verbatim, namespace neutralized
  `…Shared.Helpers`/`…Shared.Orchestration` → `…Conversation`.

### 3. Kernel rewires correct; no residual Shared.*; excluded items not re-carried — **PASS**
- `TransportErrorHandling`→`Kernel.Transport`, `AdapterHttpRequestFailedException`→`Kernel.Exceptions`,
  `LogRedaction`→`Kernel.Redaction`, `DataPipeline.Telemetry`→`Kernel.Telemetry`: all four rewires present
  in the carried code (`SessionTelemetry.cs` line 4, `AdapterHttpClient.cs` usings) and **all four target
  types/namespaces exist in Kernel**.
- `grep` for `Cymulate.Integration.Adapters.Shared` under all of `src/`: **NONE**.
- `TransportErrorHandling*`/`LogRedaction*`/`UnknownFlowRetryPolicy*` NOT present under `Conversation/`.

### 4. Microsoft.Extensions.Http added; Kernel NOT modified — **PASS**
- `Directory.Packages.props`: `PackageVersion Include="Microsoft.Extensions.Http" Version="10.0.9"`.
- csproj: `PackageReference Include="Microsoft.Extensions.Http"` (versionless, central-managed). Correct CPM.
- Build resolves the package; Authentication/Sdk.Query come transitively (no extra refs).
- `git diff` on `src/IntegrationInfra/Kernel` is **empty**; `git status` clean. Kernel untouched. Confirmed.
- Minor evidence gap: the source repo has **no `Directory.Packages.props`** (its csproj reference is
  versionless, resolved by the wider Cymulate feed), so the "10.0.9 = source floor" claim cannot be
  independently confirmed *from this repo*. The version is in the local NuGet cache and the build resolves
  cleanly, so this is not a blocker — just a claim not fully traceable to a source-pinned floor.

### 5. XML docs on every public member; README extended — **PARTIAL**
- **README: PASS.** `Conversation/README.md` extended with a "Carried" section covering the reclaim, the 2
  helpers, all four Kernel rewires, the `CreatePipeline`-deferred note, the http.package substrate
  dependency, and the explicit leak-is-expected/no-facade note. Fully satisfies the criterion's enumerated
  items. (Minor: the top charter section still lists `TransportErrorHandling`/`UnknownFlowRetryPolicy` in
  the concern's conceptual inventory; the "Carried" section correctly clarifies they are rewired-not-carried
  — slightly loose but not contradictory.)
- **Per-member XML docs: gap, but verbatim-justified.** ~5 carried files have public members with **0**
  `<summary>` (CredentialTokenCache, StreamedResponseBodyReader, CredentialProviderSubscription,
  ISessionFactory, SessionTelemetry). Verified these gaps are **verbatim from source** (source had 0
  summaries on the same files; carry preserved 0→0 — no docs were dropped). The criterion's literal "every
  public member" is therefore not met, but the executor explicitly chose verbatim preservation over
  enrichment, citing the FaultGovernance precedent. This is a defensible verbatim-vs-enrich tension, not a
  regression. No doc-enforcement (CS1591/GenerateDocumentationFile) is configured, so build is unaffected.

### 6. Tests cover testable pure units, not vacuous; counts confirmed — **PASS**
- Verifier re-ran `dotnet test IntegrationInfra.slnx`: **Conversation 10, Kernel 33, FaultGovernance 20 =
  63 passed, 0 failed, 0 skipped.** Matches the claim exactly.
- Tests are substantive: HttpStatusExtractor (structured-preferred ordering, three message-parse forms,
  non-http false path, out-of-range rejection, http-no-status fallback) and RateLimiterHelper
  (permit-limit boundary via synchronous `AttemptAcquire`, per-minute, per-hour). Real assertions on real
  behavior — not vacuous. Session/auth/cert left untested per A-test (http.package-coupled) — appropriate.

### 7. Source repo NOT mutated — **PASS**
- No source `.cs` has an mtime newer than the task start (`find -newermt 2026-06-30` empty).
- Source `Orchestration/AdapterSessionLifecycle.cs` mtime May 12; `Session/SessionHandle.cs` Jun 22 — all
  pre-task. Source repo untouched.

---

## Cross-cutting constraints
- **No facade over the http.package substrate:** confirmed — the only interface (`ISessionFactory`) is
  carried-from-source, not a new wrapper. Substrate types remain exposed (leak-is-expected per D4).
- **Conversation depends on Kernel, never reverse:** Kernel has no reference to `Conversation`. Confirmed.
- **Build clean:** 0 errors (8 pre-existing NU1507 source-mapping warnings, unrelated to this carry).

---

## Must-fix items
**None blocking.** Optional follow-ups (operator discretion):
1. (Minor) If criterion 5's "every public member" is to be read literally, add `<summary>` to the ~5
   files that inherited zero docs from source. Current verbatim stance matches FaultGovernance precedent
   and is defensible — leave as-is unless a doc-coverage gate is later introduced.
2. (Cosmetic) Tighten README top section so `TransportErrorHandling`/`UnknownFlowRetryPolicy` aren't listed
   as concern inventory without the rewired-to-Kernel qualifier inline.
3. (Trace) Record where `Microsoft.Extensions.Http` 10.0.9 was sourced as the floor, since the source repo
   has no central props to confirm it.

## Overall
The carry is verbatim where required (SessionHandle dispose ordering byte-identical; 20/21 files identical
bodies; the 21st differs only by whitespace + the mandated Kernel-rewire usings), the Kernel rewires are
correct and complete, no `Shared.*` residue, Kernel untouched, source unmutated, build clean, 63 tests pass.
The single PARTIAL (per-member XML docs) is a documented verbatim-vs-enrich tradeoff with precedent, not a
defect. **PASS.**
