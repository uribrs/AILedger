# Prompt Contract

Role:
You are a senior .NET infrastructure engineer executing a bounded cleanup + verbatim-carry task in the Cymulate.IntegrationInfra NuGet library.

Goal:
On a new branch off `main` in /Users/user/Dev/Uri/localprojects/IntegrationInfra: (1) remove the duplicated trigger-parsing helper and its duplicated test class, (2) rename the two "Legacy" executor types to behavior-based names, (3) carry seven indicator-mechanism files verbatim from the adapters repo's Shared library into their decided concern homes, with minimal tests for the pure-logic pieces. Build and full test suite green.

Context:
- Source of truth for what/why: task.md. Placement + naming rulings: decisions.md. Verified facts: assumptions.md.
- Shared source root for the carry: /Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/
- Prior carry tasks under ai/active/*-carry document the relocation discipline; mirror their style.
- Carried namespaces: Cymulate.IntegrationInfra.Contracts (ITopicHandler), .Conducting.Indicators (BaseFlowHandler), .Kernel.Indicators (IocTypeDetector, PrivateIpDetector), .Envelopes.Indicators (IocUploadRequest, IoaUploadRequest, IoaRuleRequestConverter).
- Renames: AdapterBusLegacyFlowExecutor → AdapterBusClassifiedFlowExecutor; CollectorResumeLegacyExecutor → CollectorResumeClassifiedExecutor (file names follow; update the two call sites and any doc/README mentions of the old identifiers).

Constraints:
- All items in constraints.md apply verbatim; highlights: new branch off main; zero behavior change; verbatim carry; no "Legacy" identifiers; IndicatorNames excluded; no csproj version edits; no AdapterGlobalDefaults relocation; style mirrors neighbors; build via `dotnet build IntegrationInfra.slnx`.
- Before deleting the Conducting trigger parser: re-run the identity diff and the src/tests usage sweep; diff the two test classes and port any case Job.Tests lacks.
- Update in-repo docs (concern READMEs, ai/ indexes) only where they reference deleted/renamed identifiers; no doc rewrites.

Success Criteria:
- `dotnet build IntegrationInfra.slnx` succeeds.
- Full test suite passes (all 7 test projects; if a run hangs, stop and report — do not poll-loop).
- `rg -i "legacy" src/ -g '*.cs'` shows zero identifier hits (comment mentions of legacy wire shape / IHttpSession path allowed).
- Exactly one trigger-parsing class exists, `Job/AdapterTriggerParsing.cs`; `CollectorTriggerParsing` absent from src/ and tests/.
- The seven carried files exist at the decided homes, compile, logic byte-identical to Shared modulo namespace/using/rename rewires.
- `IndicatorNames` absent from the repo.
- New tests exist and pass for IocTypeDetector, PrivateIpDetector, IoaRuleRequestConverter.
- execution_notes.md records what was done per step; state.json steps updated.

Execution Rules:
- Do not assume missing data; verify with the repo.
- Respect constraints strictly; scope creep is a defect.
- Fix review-surfaced bugs directly during execution; ask only at real forks.

Output Format:
- Code changes on the branch + execution_notes.md (per-step log, deviations flagged) + updated state.json.
- Final summary: branch name, files touched/added/deleted, test results, suggested version bump, deferred items restated.

Stop Conditions:
- Goal achieved (all success criteria verified).
- The identity re-diff or usage sweep contradicts assumptions.md (duplicate not identical, or live references found) — stop and surface.
- Build/test failures traceable to CodeArtifact auth — stop and surface (auth issue, not code).
- Any fix would require a behavior change to pass — stop and surface.
