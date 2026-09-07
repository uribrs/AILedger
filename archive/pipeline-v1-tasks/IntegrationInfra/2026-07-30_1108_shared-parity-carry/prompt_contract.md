Role:
You are a senior .NET engineer carrying proven capabilities from an in-production shared library into
an infrastructure package protected by a behavior-pinning test net, under a hard upstream data contract.

Goal:
Close the three functional-parity gaps between the adapters repo's `Shared` library and
`IntegrationInfra`: (1) restore the deterministic RFC-4122 v5 `instanceBatchId` and fold batch scoping
into `NdjsonBatchEmitter` behind a single boolean, (2) restore the NDJSON content hash on both batch
sessions and in both publish-completed log lines, (3) carry `SessionAuthRetry` into `Conversation`.
Finish with Infra building clean and the full suite green.

Context:
- Repo `/Users/user/Dev/IntegrationInfra`; branch off `origin/dev` (`ad2699a`), which is
  content-identical to the audited tree (`git diff --stat 40f828a ad2699a -- src/ tests/` is empty).
- Baseline: builds clean, 247 tests pass across 8 projects.
- Read-only reference source for every carry:
  `/Users/user/Dev/cymulate-integration-adapters/.claude/worktrees/dev-work/src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared`
- Authoritative prior analysis (grounded, verified — do not re-derive):
  `/private/tmp/claude-501/-Users-user-Dev-cymulate-integration-adapters/05349b9b-55de-47d7-9ed0-9652dc6ab990/scratchpad/CARRY_PLAN.md`
  and `.../PARITY_AUDIT.md`.
- `instanceBatchId` is consumed by upstream ops and has a unique index on the backing store. The
  current segment-string value breaks the sequence. Namespace GUID `b584b489-7c3d-4caf-97eb-49d7ff6d78fb`
  is frozen forever.
- Why the ergonomics half matters: the three current Shared consumers each pay 14–31 lines of plumbing
  to opt in (Qualys 14 across 3 files, InsightVmCloud 31 across 6, Falcon 16 across 5), and each of the
  4 publish sites re-implements an ordering contract whose dud-page rule is easy to get silently wrong.

Constraints:
* See `constraints.md` — all of it is binding. Highlights that most often get violated:
* `BatchIdNamespace` is frozen; `BuildBatchInstanceId` output must be byte-for-byte identical to Shared's.
* Scoping folds into `PublishUtf8PageAsync` / `PublishPageAsync` ONLY; the four named page methods
  inherit it by delegation; batch-level `PublishAsync`/`PublishUtf8Async` stay unscoped.
* `bool batchScopedStorage = false` on both ctor and `Create` — the default is what keeps ~24 existing
  test call sites compiling unchanged.
* Content-hash digest must be identical for single-upload and multipart publication of identical content.
* `SessionAuthRetry` stays a static, replays exactly once, never loops, returns a second 401/403
  unchanged, disposes both attempt requests but never the returned response, flows `ct` into both sends
  and the refresh.
* Do NOT add `ContentHashHex` to `PublishResult` (SDK type; belongs to the absorption task).
* No csproj changes. No version bumps. No commits, no pushes.
* Do not modify anything in the adapters repo.
* Execution shape is mandated: main agent implements the seam itself and gates on green BEFORE fan-out;
  then one subagent per work item, file-touch-partitioned, none touching a seam file.

Success Criteria:
* `dotnet build IntegrationInfra.slnx` reports 0 errors.
* Full suite green: the 247 baseline tests still pass, plus the new ones.
* `instanceBatchId` is an RFC-4122 v5 UUID matching Shared's golden vector byte for byte, proven by the
  three carried pin tests (`_MatchesRfc4122V5GoldenVector`, `_IsDeterministic_AndParseable`,
  `_DiffersAcrossPagesAndBases`).
* `BatchScopedStorageTests.cs:49` and `:145` no longer assert the segment string.
* A consumer can enable batch scoping with ONE boolean and ZERO publish call sites, proven by a test
  that publishes a page with the flag on and asserts both the scoped upload path and the dud-page
  `RestoreBase`.
* `ContentHashHex` is present on both batch sessions and in both publish-completed log lines, with a
  test proving single-upload and multipart publishes of identical content produce the same digest.
* `SessionAuthRetry` present in `Conversation` with its 4 carried tests passing.
* Both accepted limits (consumer-side `AdvancePage` ordering; no concurrency safety under scoping) are
  documented on the ctor flag, and the emitter's existing "safe for concurrent publishes" doc comment is
  reconciled with the scoping caveat.
* Emission and Conversation READMEs reflect the new surface.

Execution Rules:
* Do not assume missing data — every carry has a verbatim source file; read it rather than reconstructing.
* Respect constraints strictly.
* The seam gate is real: run build + full suite after the seam and before any fan-out. The ONLY tolerated
  failures at that gate are `BatchScopedStorageTests.cs:49` and `:145`; anything else stops the task.
* Do not adapt a failing assertion to match new behavior unless it is one of those two. Any other
  assertion change is a STOP condition — surface it.
* Fix review-surfaced bugs directly during execution; reserve questions for genuine forks.
* If `dotnet test` hangs, stop polling and hand verification to the verifier agents.

Output Format:
* Code changes in `/Users/user/Dev/IntegrationInfra` on a new branch off `origin/dev`, uncommitted.
* `execution_notes.md` appended per step with what changed, what was verified, and any deviation.
* `state.json` step statuses updated as work progresses.
* `review/verifier-N.md` and `review/code-reviewer-N.md` from the orchestrator's review passes.
* Final report: build/test result, the four success-criteria proofs, and any accepted risk.

Stop Conditions:
* When every success criterion is met.
* Seam gate fails for any reason other than the two known pinned assertions.
* A test assertion other than those two would have to change.
* `BuildBatchInstanceId` output does not match Shared's golden vector.
* A required write is blocked by sandbox or approval restrictions.
