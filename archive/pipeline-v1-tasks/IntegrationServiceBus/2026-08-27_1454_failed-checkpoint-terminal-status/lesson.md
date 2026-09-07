# Lessons — 2026-08-27_1454_failed-checkpoint-terminal-status (2026-08-27)

## L-9db99c35 — A3
belief:  Excluding a status from a recovery sweep's SELECT is enough to stop that sweep dispatching such a row.
counter: The sweep is select-then-claim with a validate/deserialize stretch between. A row marked terminal inside that window has a released claim, so TryClaimAsync still matched and the run was re-dispatched after its done was published. The guard has to be on the claim statement, not only the selection.
source:  review/code-reviewer-2.md B1; CheckpointRepository.cs:565, CheckpointRecoveryHandler.cs:85 and :295 (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  grep -n "status <> 'Failed'" ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Postgres/Persistence/CheckpointRepository.cs
do not:  do not gate a select-then-claim sweep on the select alone; put the predicate on the claim.

## L-5838490b — A4
belief:  One cleanup predicate can serve two retention horizons if it branches on a status column.
counter: Narrowing DeleteExpiredAsync to terminal rows removed the only sweep that reaped non-terminal ones, making rows with unusable PlatformEventJson and rows in a retired tenant partition immortal. Two questions need two methods: the original TTL predicate for non-terminal rows, a separate terminal delete on the longer horizon.
source:  review/code-reviewer-1.md B1; review/verifier-1.md F1 (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  grep -n 'DeleteTerminalExpiredAsync' ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Domain/Cymulate.IntegrationServiceBus.Domain/Interfaces/ICheckpointRepository.cs
do not:  do not repurpose an existing sweep predicate to a narrower population without asking what the old population loses.

## L-bb0d01f6 — A2
belief:  A checkpoint row marked terminal is inert, so its updated_at_utc is a stable failure timestamp.
counter: A redelivery re-claims it and TryAcquireExecutionLockAsync flips Failed to Idle by design — without that a redelivery executed against a row recovery could not see, and a pod loss in that window stranded the run with no done. A terminal row is revivable and its retention clock restarts if it is.
source:  review/code-reviewer-1.md B2; CheckpointRepository.cs:665 (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  grep -n 'CASE WHEN adapter_checkpoints.status' ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Postgres/Persistence/CheckpointRepository.cs
do not:  do not treat a terminal marker as write-once without checking every path that upserts the row.

## L-76bacf0b — A8
belief:  Stripping secrets from a retained row gives an unconditional secret-free guarantee.
counter: A redelivery restores the whole payload via platform_event_json = EXCLUDED.platform_event_json, so a revived row carries its secrets again until it fails and is re-stripped. The guarantee holds only while the row stays terminal and untouched.
source:  review/verifier-2.md F5; CheckpointRepository.cs ON CONFLICT DO UPDATE (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  grep -n 'platform_event_json = EXCLUDED.platform_event_json' ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Postgres/Persistence/CheckpointRepository.cs
do not:  do not state a data-removal guarantee without tracing every path that rewrites the field.

## L-71758bac — A11
belief:  An in-memory repository used as the test double behaves equivalently to the Postgres one for lifecycle questions.
counter: Its TryClaimAsync returned true unconditionally including for missing keys, its lock path zeroed progress where Postgres preserved it, and one blocker fix landed in Postgres only while its in-memory test passed anyway. The double answered the open restart-vs-resume question the opposite way from production.
source:  review/code-reviewer-2.md B1 and I2; review/verifier-2.md (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  grep -n 'TryClaimAsync' ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Core/Services/InMemoryCheckpointRepository.cs
do not:  do not read a green in-memory suite as evidence about lifecycle semantics without diffing the double against the real store.

## L-a561ce63 — A9 L-910c8f52
belief:  The Postgres raw SQL added for terminal-status retention is correct.
counter: Docker was unavailable for the whole task, so roughly 37 CheckpointRepositoryTests never executed. Four hand-written statements — the mark with its owner guard and jsonb strips, both delete predicates, and the lock's status CASE — have never touched a database.
source:  review/verifier-3.md open item 1 (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  dotnet test ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Tests/Cymulate.IntegrationServiceBus.API.UnitTests/Cymulate.IntegrationServiceBus.API.UnitTests.csproj --filter FullyQualifiedName~CheckpointRepositoryTests
do not:  do not treat ToQueryString or in-memory parity as evidence for raw SQL.

## L-dc61d2c4 — D1
belief:  The feature is three edits: mark the status, exclude it from the sweep, delete it on a longer cutoff.
counter: It is those three plus a claim release, a claim-time status guard, a terminal-row revive, a second delete method, and a secret strip. Each was found by review rather than design, and two were regressions the earlier simplification introduced.
source:  decisions.md Corrections; three verifier and three code-review cycles (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  grep -c 'MarkFailedAsync\|DeleteTerminalExpiredAsync' ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Domain/Cymulate.IntegrationServiceBus.Domain/Interfaces/ICheckpointRepository.cs
do not:  do not price a lifecycle change by the number of predicates it changes; price it by what the thing it replaces was silently doing.
