# Verifier Pass — YamlCollector E2E Readiness Report

## Verdict: PASS-WITH-GAPS

The report meets every Success Criterion and answers A/B/C/D concretely for tenable.io.
Independent spot-checks of 12+ load-bearing claims against the actual source in all three
repos all held — including exact line numbers. Snapshots match A5 exactly, assumptions are
all VALIDATED with statuses updated, and the diagram artifact exists, is self-contained
(CSP-safe), and covers A→D. The only defects are two wrong/imprecise line citations in the
evidence table and one factual dependency (Finding #2) that rests on a backend-repo detail
stated as fact without an inference label. None undermine any High finding or the verdict.
This is unusually well-grounded work.

## Criterion-by-criterion

| Success Criterion (prompt_contract.md) | Status | Notes |
|---|---|---|
| Phases A–D each answered concretely for tenable.io (message shape, dispatch mechanism, adapter selection, pre-collection steps, collection loop incl. checkpoint/S3/events, error paths) | MET | A: `AdapterRunMessage` shape + RMQ/HTTP ingress + content-detection. B: map→`PlatformEvent`, host-side YAML inline, `ProductType=YamlEngine`, `ProcessAsync`. C: payload parse, YAML resolve, schema validate, credentials, checkpoint gating, per-call engine. D: workflow create→poll→download, records→S3, per-stage/per-chunk checkpoints, typed errors, completion. All concrete, all cited. |
| Readiness verdict with enumerated, severity-ranked findings (each with evidence) | MET | Executive verdict + 10-row severity table (High→Low) + "working as intended" list + "what ready requires" 5-step actionable path. Every row carries file:line evidence. |
| Engine-drift status between the two engine copies (ADR-0003) | MET | W4 diff: adapters copy strictly ahead (Workflow/, cursor recovery, failure classifier, embedded schema); every shared file differs; `IntegrationEngine.cs` ~889 normalized lines. Direct route cannot run `tenable.yaml`. |
| Detailed flow diagram A→D incl. error/checkpoint paths, rendered + source committed | MET (minor) | Mermaid `sequenceDiagram` inline in report (in task dir) covers UseYaml gate, YAML miss→native fallback, poll loop, for_each publish, checkpoints, 3 error branches incl. Retry-After. Rendered HTML present + self-contained. No standalone `.mmd` file — source lives in the report; satisfies "committed to task directory" in substance. |
| Write-up saved to task dir (execution_notes.md + final report) | MET | Both present; execution_notes has W1–W4 + seam verification. |
| Constraint: read-only, no product-code changes | MET | No product files modified; git status of all three repos clean. |
| Constraint: per-repo branch + HEAD snapshot recorded | MET | Matches live git exactly (see spot-check 13). |
| Constraint: distinguish bug/gap/drift/WAI | MET | Findings table has a Type column doing exactly this. |
| Execution rule: validate A2–A5, update statuses | MET | All A1–A5 VALIDATED in assumptions.md and echoed in the report. |

## Spot-check results (all against live source)

| # | Claim | Result |
|---|---|---|
| 1 | `Adapters:UseYaml` default `false` at `ProcessEventCommandHandler.cs:59` | CONFIRMED — `GetValue(ConfigurationKeys.Adapters.UseYaml, false)` on line 59 exactly. |
| 2 | Vendor→name normalizer `"Tenable.io"` → `tenable.io` (dots preserved) | CONFIRMED — `vendor.Trim().ToLowerInvariant().Replace(' ', '-')` at `ProcessEventCommandHandler.cs:443` and `TriggerFlowMapper.cs:474`; only spaces are replaced, so the `.` survives. |
| 3 | Canonical vendor string is `"Tenable.io"`; `YamlEngine=98` | CONFIRMED — `PlatformType.cs:58` `[EnumMember(Value="Tenable.io")] TenableIo=16`; `YamlEngine=98` at :98. |
| 4 | S3 key `{env}/yaml-files/{name}.yaml` | CONFIRMED — `S3YamlDefinitionFetcher.cs:40` `$"{envPrefix}/{YamlFolder}/{name}"` with `.yaml` appended (:36); `YamlFolder="yaml-files"` (:25). |
| 5 | `WorkflowRunner` throws on `RetryAfter` at ~`:243` → `YAML_WORKFLOW_FAILED` | CONFIRMED — `WorkflowRunner.cs:243-244` `if (opResult.RetryAfter is not null) throw new WorkflowStageException(...)`; caught at `YamlOperationRunner.cs:241-244` → `"YAML_WORKFLOW_FAILED", isRetryable:false`. |
| 6 | Magic loader `IgnoreUnmatchedProperties` | CONFIRMED — `YamlIntegrationLoader.cs:39`. |
| 7 | Magic schema lacks `workflow` key (`additionalProperties:false`) | CONFIRMED — no `workflow` anywhere in `schemas/integration.schema.json`; `additionalProperties:false` at :7. Adapters engine schema HAS it at `:32` and `:457` (matches report citation). |
| 8 | Magic `Platform.Integrations.Sdk` has no `Workflow` type | CONFIRMED — grep for `Workflow` across the SDK returns nothing. |
| 9 | `tenable.yaml` orchestration is a top-level `workflow:` block at 217-263 | CONFIRMED — `workflow:` at :217, stages/capture/poll/for_each with `{{stages.*.output.chunks}}` templating. |
| 10 | No YAML uploader in any repo (all `yaml-files` refs are readers) | CONFIRMED — every hit is a fetcher/loader/local-runner/test; no writer/PutObject to `yaml-files`. |
| 11 | Phase C adapter refs: class `:22`, `ProcessAsync :161`, `ResumeAsync :215`, `CanResumeFrom :262`, resumable strategies incl. `workflow`, 24h age | CONFIRMED — all exact; `ResumableStrategies = ["cursor","link_header","offset","page_number","workflow"]` at :31; `_maxCheckpointAge = TimeSpan.FromHours(24)` at :38. |
| 12 | Finding #9: docstring claims mid-workflow resume unsupported, but code + E2E support it | CONFIRMED and internally consistent — docstring `WorkflowRunner.cs:32-33` says "a resumed run restarts the workflow from the first stage," yet `:50-122` restore `CompletedStages`, skip completed stages, resume past published chunks; E2E `MidWorkflowCrash_ResumesPastPublishedChunk_NoRefetch` exists at `WorkflowExportVendorE2ETests.cs:138`. The docstring is genuinely stale. |
| 13 | Repo snapshots (magic master@1d1c5c2, ISB dev@6a9fcc0, adapters dev@86b2029) | CONFIRMED — live git matches all three exactly. |

## Issues found (severity-ordered)

**Low-1 — Wrong citation in Finding #6 evidence (`ISB appsettings.json:25`).**
The `Adapters` config block is at `appsettings.json:58`, not :25, and `UseYaml` does not appear
in appsettings at all. The substantive claim is still correct (and actually stronger than
stated: UseYaml is absent from committed config, so it relies entirely on the `false` code
default at `ProcessEventCommandHandler.cs:59`). Fix the line reference or drop it.

**Low-2 — Slightly off line range in Finding #7 (`AppHost/Program.cs:101-108`).**
The stale ServiceBus wiring + "consumes work.dispatched / reads YAML from mongo" comment is at
roughly `:102-110` (comment 102-104, `AddProject<…ServiceBus>` at 105). Vicinity is right; the
underlying claim (hollow ServiceBus still wired, retired `contracts/*.yaml` still present) is
CONFIRMED — both `isb-control.yaml` and `isb-data.yaml` exist.

**Low-3 — Finding #2 rests on an unlabeled inference about the backend's vendor string.**
The report's Phase A message shape states `"product": "Tenable.io"` as fact and Finding #2
derives the `tenable.io.yaml` mismatch from it. The `"Tenable.io"` string is well-grounded in
the ISB `PlatformType` `EnumMember` (spot-check 3), but the actual backend that emits the run
message is out of scope (not in the three repos) and was not read. This should be labeled as an
inference from the ISB enum rather than asserted. Note the finding survives either way: no
uploader exists, so neither `tenable.io.yaml` (if product="Tenable.io") nor `tenable.yaml`
(if product="Tenable") would be present in S3 — the mismatch risk holds regardless, but the
report doesn't make that robustness explicit.

**Nit — Diagram source is inline in the report, not a standalone `.mmd`.**
The contract says "mermaid source in task dir + rendered artifact." The source is committed
(inside the report, which is in the task dir) and the rendered HTML exists, so this is
satisfied in substance. A standalone `.mmd` would be cleaner but is not required.

## Unresolved gaps

- **Backend repo not inspected.** Phase A is reconstructed from the ISB ingress contract, not
  from the backend that publishes the message. This is inherent to the stated scope (three
  repos, backend not among them) and the report is internally honest about ingress being the
  boundary, but the exact wire vendor string / whether the backend ever sends `integrationName`
  is unverifiable here. This is the single load-bearing external unknown behind Findings #1/#2.
- **Runtime rollout state unverifiable from code** (Finding #6) — whether any environment sets
  `Adapters:UseYaml=true` or binds a `Collectors`-category queue is env-supplied. The report
  correctly flags this rather than guessing; no action needed, just noting it remains open.

No overstated High findings, no unlabeled speculation beyond Low-3, no internal contradictions
between report / execution_notes / assumptions / diagram.
