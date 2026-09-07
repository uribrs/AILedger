# Decisions

- The outcome vocabulary is defined in IntegrationInfra, not duplicated per repo. Both consumers must
  speak the same words for the mapping to mean anything.
- IntegrationServiceBus supplies the means; each collector decides how to use them. Checkpoint state
  shapes stay per-vendor.
- Position has one owner: the collector. The host stores what it is given and derives nothing.
- Cooperative and non-cooperative interruption are treated differently by design. Only the second
  leaves work to be redone.
- Prerequisite first: the Falcon test assembly is repaired before any new adapters work, because
  nothing in that repo can be tested until it compiles.
- The uncommitted partial-completion change stays on the branch and gains a test in this task rather
  than being reverted.
- Proceeding on unverified: `FlowExceptionHandling` lives in a package both consumers reference. If
  wrong, the vocabulary needs a different home and the Infra-first ordering changes.
- Proceeding on unverified: the Postgres upsert can report its rejection reason without a second
  query. If wrong, the reason needs either a `RETURNING` clause change or a follow-up read, which
  costs a round trip per refused write.
- Proceeding on unverified: flush-on-yield is achievable for Falcon. If a partial unit cannot be
  published for some collector, that collector declares the capability as unavailable and falls back
  to recording the rounded position plus the measured waste.
- Ordering across repos is Infra, then IntegrationServiceBus, then adapters, because the later two
  compile against the first.
