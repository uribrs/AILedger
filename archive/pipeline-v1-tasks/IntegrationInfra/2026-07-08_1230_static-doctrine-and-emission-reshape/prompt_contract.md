# Prompt Contract

Role:
You are a senior .NET engineer executing a doctrine-guided API reshape in an infrastructure library protected by a behavior-pinning test net.

Goal:
On a branch off `dev` in /Users/user/Dev/IntegrationInfra: (1) codify the static-taxonomy doctrine in DESIGN.md, then (2) reshape the Emission publisher stack from service-locating statics into one instance-based emitter with construction-time option resolution, deleting the static entrypoints. Full suite green with only mechanical test call-site churn.

Context:
- Scope + grounded facts: task.md. Rulings: decisions.md. Fences: constraints.md.
- Key files: src/IntegrationInfra/Emission/{ResultsBatchPublisher.cs, AdapterNdjsonPublisher.cs, Options/*, Ndjson/*}, DESIGN.md, Emission/README.md, Emission/README.Publishing.md.
- Test net: tests/IntegrationInfra.Emission.Tests (AtomicStreamedObjectsTests, BatchScopedStorageTests, EmissionTestDoubles, RecordingPublisher) — 24 static call sites to mechanically update.
- Prior task 2026-07-08_0155_invariant-pin-tests (merged to dev) built the net; treat its tests as immutable oracles.

Constraints:
- All of constraints.md verbatim. Highlights: doctrine commit before reshape commit; behavior-identical (assertion change = STOP); statics deleted not wrapped; resolution chain preserved once-at-construction; scope fence around the publisher stack; no version bump; DAG unchanged.

Success Criteria:
- DESIGN.md: "Static taxonomy" section (three classes, rulings, examples) + deferred-seams list updated (static-publisher DIP seam resolved) + no contradicting decision text remains.
- Emission has NO public static publish entrypoints and NO service-provider resolution inside any static body (grep-verifiable: `Resolve(context.Services)` gone from static bodies).
- New public instance emitter: explicit-options ctor AND `Create(IServiceProvider?)` preserving DI→IConfiguration→env→default; same publish surface (string/utf8, page + generic, naming gate, BuildMandatoryTargetPath); progress/execution context as method params.
- Startup guards preserved with same exception type and equivalent timing (choice recorded in execution_notes.md).
- `dotnet build IntegrationInfra.slnx` 0 errors; full suite green; test diffs are arrange/act-only (assertions byte-identical).
- Emission README + README.Publishing reflect the new surface; ISink note resolved-or-narrowed per its own framing.
- execution_notes.md logs per-step outcomes incl. MultipartUploadOptions resolution site finding and the sessions' options-handoff shape; state.json updated; archive mirrored.

Execution Rules:
- Verify every assumed shape against source before coding; audit the OPEN assumptions first.
- Part 1 and Part 2 are separate commits.
- Fix genuine product bugs surfaced en route directly; record in decisions.md.
- If behavior-identical proves impossible without an assertion change, STOP and surface — do not negotiate with the net.

Output Format:
- Two commits on the branch (doctrine, reshape) + updated docs + execution_notes.md + state.json.
- Final summary: branch, commits, emitter surface sketch, test-churn stats (files/lines, assertions untouched), grep-gate results, residual risks.

Stop Conditions:
- Goal achieved and verified.
- The net requires an assertion change to pass (behavior drift) — stop, surface.
- The sessions' internal options handoff cannot accept construction-time options without logic changes beyond the fence — stop, surface.
