# Lessons — 2026-08-27_1245_failed-checkpoint-retention-ttl-column (2026-08-27)

## L-d13589c6 — B1
belief:  Retaining a failed run's checkpoint instead of deleting it is a storage change with no lifecycle consequences.
counter: The delete took the row's claim with it. Retaining left it claimed and freshly heartbeated, so a TransientFailure redelivery was denied by the execution lock, burned its retry budget on an error code not classified as non-transient, and dead-lettered. The claim must be released in the same UPDATE that stamps the hold.
source:  review/code-reviewer-1.md B1; ProcessEventCommandHandler.cs:1302, CheckpointRepository.cs:793 (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  grep -n 'claimed_by_instance = NULL' ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Postgres/Persistence/CheckpointRepository.cs
do not:  do not change what a terminal path deletes without asking what else that delete was releasing.

## L-716f3c62 — CR2-B1
belief:  A recovery sweep predicate and a TTL cleanup predicate over the same column should agree; their disagreement is a smell.
counter: They answer different questions. The sweep asks should this be re-run (never, while a hold exists); cleanup asks should this be deleted (yes, once the hold lapses). Making them agree left an expired hold fully sweep-eligible, and with a 5-minute sweep against a 30-minute cleanup it re-dispatched a collection that had published its failure done a week earlier.
source:  review/code-reviewer-2.md B1; review/verifier-2.md F2; CheckpointRecoveryJob.cs:18 vs CheckpointCleanupJob.cs:19 (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  grep -n 'RetainUntilUtc' ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Postgres/Persistence/CheckpointRepository.cs
do not:  do not harmonise two predicates over one column before establishing that they ask the same question.

## L-44a4039e — I3
belief:  Extending how long an existing row is retained is a lifecycle change with no security dimension.
counter: adapter_checkpoints.platform_event_json holds a full serialized PlatformEvent whose Credentials are not always the encrypted blob — EventsController.cs:479-484 assigns GetCredentials() output directly. Retention extended stored plaintext vendor credentials from deleted-at-end-of-run to seven days by default and thirty by configuration.
source:  review/code-reviewer-2.md I3; EventsController.cs:479-484 (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  grep -n 'platformEvent.Credentials = firstCred' ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Hosts/Cymulate.IntegrationServiceBus.API/Controllers/EventsController.cs
do not:  do not extend a row's lifetime without auditing what the row serialises; check every entry path, not the one whose sample you have.

## L-6bfcda78 — B2
belief:  A retention hold on a dead run should survive whatever later writes touch the row.
counter: A hold means the run is dead; a takeover means it is alive again. Preserving it across TryAcquireExecutionLockAsync made the row invisible to GetRecoverableAsync for life, so a resumed run had no recovery behind it and updated_at_utc also kept it out of DeleteExpiredAsync. The rule is: ordinary progress writes preserve, takeovers clear.
source:  review/code-reviewer-1.md B2; CheckpointRepository.cs:618 (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  grep -n 'retain_until_utc = NULL' ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Postgres/Persistence/CheckpointRepository.cs
do not:  do not treat preserve-on-write as one rule; separate a write to a dead row from a takeover of it.

## L-5d94327f — A1
belief:  The retention branch in CompleteExecutionAsync is reached by every collector-reported failure.
counter: A collector-reported failure during pod shutdown returns at ProcessEventCommandHandler.cs:279 before CompleteExecutionAsync and stamps nothing; the CLAIM_LOST returns do the same. Shutdown and claim-loss both bypass retention entirely.
source:  review/verifier-2.md A1; ProcessEventCommandHandler.cs:279 (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  sed -n '270,285p' ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Applications/Cymulate.IntegrationServiceBus.Application/Commands/ProcessEventCommandHandler.cs
do not:  do not assume a completion helper is the sole terminus of a failing run without enumerating the early returns above it.

## L-910c8f52 — A3 A9 R5
belief:  The retain_until_utc migration and every raw SQL statement added for it are correct.
counter: Docker is unavailable on this machine, so MigrateAsync never ran and roughly 37 CheckpointRepositoryTests never executed. The claim release, ownership guard, jsonb credential strip and the lock's retain_until_utc = NULL are all hand-written SQL that has never touched a database. ToQueryString() evidence covers only the two LINQ predicates.
source:  review/verifier-2.md R5; execution_notes.md W1 build sections (verifier, 2026-08-27) (verifier, 2026-08-27)
verify:  dotnet test ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Tests/Cymulate.IntegrationServiceBus.API.UnitTests/Cymulate.IntegrationServiceBus.API.UnitTests.csproj --filter FullyQualifiedName~CheckpointRepositoryTests
do not:  do not treat a green in-memory suite as evidence for a Postgres repository's raw SQL.

## L-0a485b73 — D1
belief:  A second configuration key for the retention window should be wired into the cleanup job the way the existing TTL key is.
counter: The deadline is stamped absolutely at failure time, so the job only compares — CheckpointCleanupJob and DependencyInjection were untouched. Recon showed the wiring was unnecessary machinery before any of it was built.
source:  decisions.md drift section; research/internal-recon.md (recon, 2026-08-27) (recon, 2026-08-27)
verify:  grep -c FailedRetention ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Core/Jobs/CheckpointCleanupJob.cs
do not:  do not wire a horizon into a sweep when the deadline can be stamped at write time.
