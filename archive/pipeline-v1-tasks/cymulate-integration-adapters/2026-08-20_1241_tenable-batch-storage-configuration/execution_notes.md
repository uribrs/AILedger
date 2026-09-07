# Execution Notes

## Change
- Added `TenableIoCollectorConfiguration.BatchScopedStorage` with default `false`.
- Forwarded `_config.BatchScopedStorage` from `TenableIoCorrelatedFindingsFlow` into `TenableIoCorrelatedBatchPublisher`.
- Replaced the publisher's hard-coded emitter argument with its constructor parameter, defaulted to `false` for direct test construction compatibility.
- Added a default-false configuration assertion and made the existing scoped-path flow test explicitly configure `true`, proving end-to-end propagation.
- Updated default flat-path test helpers required by the new default.
- Preserved the existing InsightVM Cloud and Qualys boolean flips without editing them in this task.

## Verification
- Focused configuration + configured-scoped test: 3/3 passed.
- Full TenableIo test project: 153/153 passed.
- Full solution build, single-node: succeeded with 0 warnings and 0 errors.
- `git diff --check`: passed.
- TenableIo source scan shows configuration default false and flow forwarding; no emitter literal remains.
- Independent verifier passed after a comment-only wording correction; no unresolved findings.
- Isolated code review approved with no findings; its focused suite passed 43/43.

## Residual Risk
- No vendor-backed run was performed; configuration propagation is covered through the correlated flow publish capture.
