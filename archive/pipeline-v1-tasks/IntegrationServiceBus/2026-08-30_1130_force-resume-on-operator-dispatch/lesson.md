# Lessons — 2026-08-30_1130_force-resume-on-operator-dispatch (2026-08-30)

## L-a8ddf024 — A9
belief:  Raw SQL that reads correctly to reviewers parses correctly in Postgres.
counter: MarkFailedAsync's jsonb strip relied on #- and - binding left to right. Postgres binds #- looser, so the expression parsed as jsonb #- (text[] - text - text) and threw 42883 on every call. Three verifier passes and three blind reviews read it; one reviewer raised operator precedence and it was dismissed by inspection.
source:  CheckpointRepository.cs:471-474; Npgsql 42883 in CheckpointRepositoryTests, 15 of 47 failing (verifier, 2026-08-30) (verifier, 2026-08-30)
verify:  dotnet test ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Tests/Cymulate.IntegrationServiceBus.API.UnitTests/Cymulate.IntegrationServiceBus.API.UnitTests.csproj --filter FullyQualifiedName~CheckpointRepositoryTests
do not:  do not accept review consensus as evidence that hand-written SQL parses; run it against the engine, and parenthesise mixed jsonb operators explicitly.

## L-0f0a6065 — R5
belief:  A missing /var/run/docker.sock means no container runtime is available on the machine.
counter: Docker CLI and colima were both installed; the colima VM named default (aarch64, 4 CPU, 10GiB, docker runtime) was merely Stopped. colima start produced a working endpoint and the container suites ran for the first time, immediately finding a blocker that had been latent for the whole change set.
source:  colima list showing profile default Stopped; docker info reporting server 29.5.2 after start (verifier, 2026-08-30) (verifier, 2026-08-30)
verify:  colima list; docker info 2>&1 | grep 'Server Version'
do not:  do not report a container-gated suite as unrunnable from an absent socket alone; check for an installed but stopped runtime first.

## L-e9b2e21e — A5
belief:  A plain retry on the same correlation id is harmless when a retained checkpoint exists.
counter: Taking the execution lock commits Failed to Idle before the resume decision, spending the retention protection. The adapter then declines the stale checkpoint and the handler restarts, consuming a week of retained progress nobody asked to discard. The lock now reports the revival and a declined resume on a revived terminal row fails instead of restarting.
source:  ProcessEventCommandHandler.cs:1277; operator-reported, confirmed by reading the lock ordering at :140 versus :232 (verifier, 2026-08-30) (verifier, 2026-08-30)
verify:  grep -n 'RESUME_DECLINED_RETAINED_CHECKPOINT' ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Applications/Cymulate.IntegrationServiceBus.Application/Commands/ProcessEventCommandHandler.cs
do not:  do not assume a durable state transition performed by a lock can be reasoned about from process-local flags later in the same call.

## L-645966cc — A4
belief:  Adding an optional typed field to an inbound wire message is additive and cannot break existing producers.
counter: Typing forceResume as a bool made the parser strict about a field that had previously been an ignored unknown property. Any non-boolean value — null, 1, "true", an array, an object — failed deserialization of the whole message and settled the delivery as Retry, stopping the run. The producer is in another repo.
source:  ForceResumeWireContractTests.I4_NonBooleanRootValuesDoNotForce, 5 failures; fixed by removing the bound property (verifier, 2026-08-30) (verifier, 2026-08-30)
verify:  dotnet test ~/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/UnitTests/Cymulate.IntegrationServiceBus.Application.UnitTests/Cymulate.IntegrationServiceBus.Application.UnitTests.csproj --filter FullyQualifiedName~ForceResumeWireContractTests
do not:  do not call a typed optional field additive on a wire contract whose producer is another repo; a wrong type is fatal where an unknown key was ignored.

## L-7448468a — A1 A7
belief:  No collector re-checks staleness inside ResumeAsync or the load path it uses, so bypassing CanResumeFrom is sufficient to force a resume.
counter: Evidence covers Falcon and CloudGuard only, both read in a different repository. No collector implementation exists in IntegrationServiceBus, so nothing here can observe it. If wrong for a vendor, a forced resume is declined and fails rather than restarting.
source:  review/verifier-2.md A1 and A7; decisions.md accepts it knowingly (verifier, 2026-08-30) (verifier, 2026-08-30)
verify:  grep -rn 'IsCheckpointStale' ~/Dev/cymulate-integration-adapters/src --include=*.cs
do not:  do not treat the force bypass as proven for the whole collector fleet on two vendors' evidence.
