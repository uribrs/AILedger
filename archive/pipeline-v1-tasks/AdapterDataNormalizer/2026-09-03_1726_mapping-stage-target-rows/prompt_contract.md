# Prompt Contract — Mapping stage: manifest-described records to target-shaped rows

Role:
You are a senior TypeScript engineer building a data-normalization stage against a Postgres
promotion boundary you cannot change, with real vendor data on disk and measured baselines to hold.

Goal:
Add a mapping stage to the Node manifest-driven normalizer in
`/Users/user/Dev/Uri/localprojects/AdapterDataNormalizer` that turns the records the existing
generic reader yields into rows shaped for `integration.parser_output_assets_enrich` and
`integration.parser_output_exposures_enrich`, with per-vendor mapping expressed as data, correct
instance-scoped ids, content-composed exposure identity, and enum validation — then measure it.

Context:
- Read `README.md`, `src/manifest.ts`, `src/reader.ts` first. They are the measured baseline.
- Reader yields `{lineNumber, recordIndex, parentKey, parent, record}`. `parentKey` is already
  resolved through one function from the manifest's ordered `layout.parentKey` candidates. Do not
  re-derive it.
- Manifests already exist: `manifests/{falcon-assets,falcon-findings,tenable-findings}.json`.
- The C# prototype to port decisions from is at
  `~/Dev/Uri/localprojects/adapter-data-normalizer/src/Normalizer/`:
  `Ids/RowIdDerivation.cs` (112 lines), `Rows/CanonicalJson.cs` (75), `Rows/ExposureContent.cs`
  (76). Verbatim copying is explicitly permitted by the operator. Port the reasoning in the
  comments, not only the code — the comments are where the measured findings live.
- `constraints.md`, `assumptions.md` and `decisions.md` in this directory are binding. Read them.

Constraints:
- Every constraint in `constraints.md` applies. It is not a summary; it is the contract.
- Per-vendor mapping is DATA. Adding a vendor must not edit any engine file.
- A mapping declares the manifest fields it reads; the engine verifies them against the lane
  manifest at load time and fails before reading any record when one is absent.
- Exactly ONE id-derivation code path serves both lanes.
- `created_at` is excluded from every content hash.
- Emit enum `@map` values (`windows-server`, `active-directory`), never Prisma identifiers.
- No lane buffering. Memory must stay bounded by the largest line.
- No Postgres write, no create-entities call, unless the plan shows it is cheap.
- Do not modify `src/reader.ts`, and do not change `recordPath` / `parentKey` semantics in
  `src/manifest.ts`.
- Do not commit or push.

Success Criteria:
1. Both Falcon lanes (`falcon-assets.json`, `falcon-findings-big.json`) and the Tenable findings
   lane produce target-shaped rows, with Tenable requiring only new mapping data — zero engine
   edits. Demonstrate the zero-edit claim, do not assert it.
2. A mapping that reads a field absent from its manifest is rejected at load time, before any
   record is read. Shown by a deliberate negative case.
3. Row ids are byte-identical across two runs with the same `instance_id`, and different across
   two runs with different `instance_id` values. Both directions shown.
4. Exposure identity composes canonical content. Report how many `(parentKey, cve_id)` pairs
   repeat in the Falcon lane and confirm the composed identity keeps them distinct. The prototype
   measured 37,750 of 122,310 on a different batch; report what this data actually gives.
5. `created_at` exclusion is demonstrated, not just coded: two runs separated in wall-clock time
   produce the same ids.
6. Enum validation rejects a non-member and accepts `windows-server` / `active-directory` in their
   `@map` spelling. Report the per-lane reject count against real data.
7. The twelve fingerprint columns are all produced, and the ordering constraint that `asset_id`
   precedes the exposure content hash is handled explicitly.
8. Measured numbers reported for every lane: records/s, MB/s, peak RSS, and the 474 MB Falcon lane
   completing under a heap cap. State the cap used. Compare against the recorded baseline of
   405 MB/s and note any regression.
9. `README.md` updated with the new measured numbers and an honest extension of its "Deliberate
   limits" section.
10. The generated enum artifact records its provenance: source repo, commit hash, date, and the
    target cluster whose column set was used.

Execution Rules:
- Do not assume missing data. Read the file.
- Respect constraints strictly.
- When an assumption in `assumptions.md` is settled by evidence you produced, record the actor and
  the citation. Do not promote an assumption on belief.
- Where the C# prototype's evidence is known weak — synthetic divergence case, exit-bar SQL that
  cannot report non-zero, `instance_id` pinned to a constant, one vendor only — do not inherit the
  claim. Re-measure or mark it untested.
- Report a regression against the measured baseline as a finding, not as noise.

Output Format:
- Source under `src/`, mapping data under `mappings/`, generated enum artifact in a clearly named
  generated file.
- `execution_notes.md` in this task directory: what was built, every measured number, and every
  deviation from the plan with its reason.
- Updated `README.md`.
- A short final report: what holds, what was measured, what is still untested.

Stop Conditions:
- The goal is achieved and every success criterion has been demonstrated or explicitly reported as
  not met.
- The declarative mapping format cannot express a real Falcon or Tenable mapping without an escape
  hatch to arbitrary code. Stop and surface it — that trade-off is the operator's call, because it
  forfeits the load-time checkability the manifest exists to provide.
- The committed schema dumps turn out not to contain the `parser_output_*_enrich` column sets.
- Holding the streaming property requires buffering a lane.
