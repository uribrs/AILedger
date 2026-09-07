# Decisions

- Retention is expressed by the existing `status` column plus `updated_at_utc`, not by a new column.
  A terminal row is not written to again, so `updated_at_utc` is a stable failure timestamp.
- The recovery sweep is gated on **status**, not on a timestamp. Gating it on a timestamp is what
  forced the previous attempt to clear state on every lock acquisition, and what let an expired row
  be re-dispatched by the 5-minute sweep before the 30-minute cleanup could delete it.
- The cleanup job's checkpoint predicate deletes only terminal rows. Non-terminal rows are left to
  recovery and to the unservable escalation, which end them loudly rather than silently.
- Proceeding on unverified: every collector-reported `failed` done reaches the branch that replaces
  the delete. Known exceptions exist (shutdown cancellation, claim loss) and are documented risks.
- Proceeding on unverified: nothing reads `Credentials` back off a stored checkpoint. The retry lane
  is to supply them fresh.

## Corrections after review cycles (orchestrator)

Two decisions above are stale and are superseded here. They are left in place because the history of
the belief is itself evidence.

- **Superseded — "the cleanup job's checkpoint predicate deletes only terminal rows."** Reversed by
  cycle-1 blocker B1. Repurposing `DeleteExpiredAsync` removed the only sweep that reaped non-terminal
  rows, making rows with unusable `PlatformEventJson` and rows in a retired tenant partition immortal.
  The cleanup job now runs **two** deletes: `DeleteExpiredAsync` on the pre-existing `Checkpoint:TtlHours`
  cutoff for non-terminal rows, and `DeleteTerminalExpiredAsync` on the retention cutoff for `Failed`
  rows. Two questions, two horizons.
- **Superseded — "a terminal row is not written to again, so `updated_at_utc` is a stable failure
  timestamp."** Reversed by cycle-1 blocker B2. A redelivery re-claims the row and
  `TryAcquireExecutionLockAsync` now flips `Failed → Idle`, which is required: without it a redelivery
  executed against a row recovery could not see, and a pod loss in that window stranded the run with no
  done. So a terminal row is revivable by design, and its retention clock restarts if it is.
- **Consequent, and weaker than first stated:** the secret-free guarantee is conditional. Credentials
  and headers are stripped while the row is terminal and untouched, but a redelivery restores the full
  payload via `platform_event_json = EXCLUDED.platform_event_json`. Steady state is stripped; a revived
  row carries its secrets again until it fails and is re-stripped. This belongs in the PR body.
- **Completing the above:** what makes releasing the claim on the mark safe is not the sweep exclusion
  alone but the `status <> 'Failed'` guard on `TryClaimAsync` itself. The sweep is select-then-claim, so
  a row marked terminal inside that window has a released claim and would otherwise be claimed off a
  stale read. The guard is the load-bearing half; the exclusion only avoids the wasted work.
- **Completing the secret list:** three things are stripped, not two — top-level `Credentials`,
  top-level `Headers`, and the nested encrypted blob at `Payload.credentials`.
