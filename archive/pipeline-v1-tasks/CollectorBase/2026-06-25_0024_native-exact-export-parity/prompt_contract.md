Role:
You are a senior .NET engineer extending the generic CollectorExecutor interpreter so its emitted records
match the byte-shape each native collector produces, without regressing prior work.

Goal:
Add a verbatim full-record passthrough emit mode and a per-emitting-step envelope selector to the engine,
then make the Tenable and Defender profiles emit native-exact shapes and complete the Defender profile to
native collection scope. Keep the existing mapped-subset mode as the default.

Context:
- Repo: /Users/user/Dev/Uri/localprojects/CollectorBase (net8.0; CollectorBase.slnx).
- Mapper today: CollectorExecutor/Interpreter/Interpreter.cs `ResponseMapper.Map` → `{sourceType, <fields
  subset>, <correlationKeys>}`. The runner publishes via SliceByBytes + the per-emit-target page counter.
- Profile model: CollectorExecutor/Profile/Profile.cs (StepSpec, MappingSpec); loader/validation there.
- Native target shapes (read-only reference, model exactly, hardcode nothing in the runner):
  - Tenable: `Collectors/TenableIoCollector/Flows/{Assets,Findings}/*ChunkProcessor.cs` — verbatim full
    record; NO wrapper, NO sourceType, NO correlation keys.
  - Defender: `Collectors/DefenderVmCollector/Processing/DefenderVmRecordFormatter.cs` — machines/software
    `{type:"machine"|"software", data:{<record>}}`; vulns/changes/recs/recVulns `{sourceType:<v>, <record
    flattened>}` (values: inventory|delta|recommendationCatalog|recommendationScopedVulnerability); recVulns
    also prepends `recommendationReference:<rec id>`. Scope/URLs: `DefenderVmUrlBuilder.cs` +
    `Flows/{Assets/DefenderVmAssetsFlow,Findings/DefenderVmFindingsFlow}.cs`.
- `next_url` pagination strategy already replicates native OData nextLink + `$skip`/`$top` fallback — do not
  rebuild it.

Constraints:
- See constraints.md (authoritative). Generic engine; per-step envelope in YAML; mapped-subset stays default
  & unchanged; verbatim injects nothing; no regression of poll-and-drain / SDK conformance / remediation /
  reserved `__`-guard; net8.0; reuse Shared egress.

Success Criteria:
1. `dotnet build CollectorBase.slnx` clean.
2. `dotnet test CollectorBase.slnx` all pass — existing 52 PLUS new tests: (a) verbatim passthrough emits the
   FULL record (all original keys, no injected sourceType/correlation); (b) `typed_wrapper` → `{type,data}`;
   (c) `source_type_prefix` → `{sourceType, ...record}` including a templated `recommendation_reference`;
   (d) the existing mapped-subset mode is unchanged.
3. integrations/tenable-io.yaml emits verbatim full records (matching TenableIo*ChunkProcessor); integrations/
   defender-vm.yaml emits the `{type,data}` and `{sourceType,...}` shapes (matching DefenderVmRecordFormatter)
   AND is completed to native scope ($filter=lastSeen ge <base_date> + base-date logic, vuln-changes stage
   with 14-day lookback, correct native endpoints/stages).
4. A parity note in the task dir mapping each native formatter shape → the YAML that reproduces it, with
   file:line citations, and explicitly listing the 3 deferred vendors (cortex-xdr, crowdstrike-falcon, qualys).
5. No regression: full suite green; spot-check poll-and-drain + adapter/bus/resume wiring intact.

Execution Rules:
- Resolve OPEN assumptions A3–A8 against the code during execution (mapper "keep element" seam; correlation
  injection for wrapped streams; envelope metadata templating scope; watermark read under passthrough; the
  Defender base-date input key; whether native base-date/lookback is templatable).
- If native base-date/lookback logic can't be expressed via templating, record it as a documented parity gap
  rather than adding per-vendor control flow to the runner (resist inner-platform creep).
- Build all engine modes; apply to Tenable + Defender only; do not touch the 3 deferred vendor profiles
  beyond confirming they still pass.
- Respect constraints strictly; async-only.

Output Format:
- Code: Profile.cs, Interpreter/Interpreter.cs (+ runner wiring as needed), integrations/tenable-io.yaml,
  integrations/defender-vm.yaml, Tests/CollectorExecutor.Test. A `parity-note.md` in the task dir.
  execution_notes.md updated; state.json steps in sync.

Stop Conditions:
- All success criteria met and verified.
- A native shape cannot be reproduced without adding vendor control flow to the runner (stop, surface — it
  may belong as a documented gap or a hand-coded collector).
- An OPEN assumption resolves in a way that contradicts a constraint (stop, surface).
