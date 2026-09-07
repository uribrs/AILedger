Role:
You are a .NET integration engineer extending a declarative YAML-driven collector engine without
adding vendor-specific control flow.

Goal:
Extend CollectorExecutor's declared-auth coverage so previously-unexpressible native vendors
(Guardicore, SentinelOne, ServiceNow) can run as pure YAML — by adding only the
`BuildAuthSelection` cases that a target vendor actually uses AND that Shared's `AuthSelection`
can already express, plus at least one proof profile and tests.

Context:
- Auth dispatch site: `CollectorExecutorSessionFactory.BuildAuthSelection`
  (`CollectorExecutor/Execution/CollectorExecutorSessionFactory.cs:36`). Wired today:
  `oauth2_client_credentials`, `api_key`, `basic`, `hmac`; `default: return null`.
- `AuthSelection` variants come from `http.package`
  (`Cymulate.Http.Package.Authentication.Contracts.Models`), consumed as a NuGet package.
  `AuthSelection.ApiKey` already supports `HeaderName` + `HeaderPrefix` (`:87-88`).
- The extension rule (CLAUDE.md + docs/yaml-contract.md) is a hard constraint: one `case` per auth
  type, YAML carries non-secret shape, secrets come from `credentials`, no vendor branch in the engine.
- Native vendor auth is traceable in `/Users/user/Dev/cymulate-integration-adapters/`.

Constraints:
- See constraints.md. Salient: add a case ONLY when (target-vendor-uses-it AND Shared-can-express-it);
  reuse Shared auth (no bespoke transport, no Shared changes); secrets from `credentials` only;
  validate new types in Profile (snake_case); no regression of the listed shipped features;
  net8.0 / CollectorBase.slnx / async-only.

Success Criteria:
1. `dotnet build CollectorBase.slnx` clean.
2. `dotnet test CollectorBase.slnx` — existing pass (currently 63) + new per-auth-type tests.
3. Every auth type added is one a target vendor actually uses AND Shared can express; anything not
   expressible is reported as a gap, not hacked in.
4. At least one previously-unexpressible native vendor (Guardicore/SentinelOne/ServiceNow) is now a
   working YAML profile, auth verified vs native source (file:line).
5. No regression (full suite green; spot-check passthrough + poll-and-drain + fail_if + validate probe).
6. `auth-note.md` in the task dir: declared-type -> Shared AuthSelection mapping, per-target-vendor
   auth trace (file:line), explicitly-excluded set (e.g. AwsSigV4) with reason.

Execution Rules:
- Resolve A1 (Shared AuthSelection variants) and A2 (per-vendor native auth) by reading code BEFORE
  writing any case — do not speculate (user: "traceable via code, of that I'm sure").
- Resolve A3 first: if bearer/static-token is already expressible as `api_key` + `header_prefix`,
  it needs zero new code — ship a profile + doc, do not add a redundant `bearer` case.
- Add cases for only what A2 proves a target needs and A1 proves Shared supports.
- If all three targets turn out already-expressible, the task still completes via a proof profile +
  the A3 finding; record that no new `case` was required.
- If a target's only auth flow is NOT Shared-expressible, record it as a gap (do not extend Shared).
- Respect constraints strictly; do not assume missing data.

Output Format:
- Code changes under CollectorExecutor/ (+ Strategies/ only if a registry touch is unavoidable),
  one integrations/*.yaml proof profile, tests under Tests/CollectorExecutor.Test, doc update in
  CollectorExecutor/docs/yaml-contract.md, and `auth-note.md` in the task dir.
- execution_notes.md updated with what changed, A1–A5 resolutions, commands run, residual gaps.

Stop Conditions:
- A target vendor's required auth flow is not expressible by any Shared `AuthSelection` variant
  (record the gap, do not invent transport) — surface before forcing it.
- Meeting a criterion would require a vendor branch in the engine or a secret in YAML.
- Goal achieved and full suite green.
