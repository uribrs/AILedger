the ledger is written at the end of a run, or not at all

`CLAUDE.md` says "record a claim before you act on it. record it first, then earn it." measured
across 249 runs, the median first ledger write lands at **72% of the way through the run**.

agents do not record as they go. they work, then write everything in one burst immediately before
finishing.

## the measurement

the five runs with cost fields, which carry their run id as the correlation on the child's writes:

| run | duration | first write | last write → end | writes |
|---|---|---|---|---|
| R26 | 949s | 778s (82%) | 93s | 10 |
| R24 | 875s | 683s (78%) | 204s | 4 |
| R27 | 639s | 439s (69%) | 18s | 4 |
| RPROOF1 | 411s | 251s (61%) | 32s | 11 |
| R25 | 595s | 298s (50%) | 31s | 7 |

R26's ten writes all landed between 08:04:04 and 08:05:22 — seventy-eight seconds, after fifteen
minutes of work.

it is not a property of those five. attributing every event to the run whose subject actor wrote it
inside the run's window, across all 299 runs:

- 249 runs wrote something. **median first write at 72% of run duration; p25 36%, p75 83%.**
- **50 runs recorded nothing at all** — 19 of 30 `failed`, 16 of 43 `cancelled`, 14 of 224
  `completed`, 1 of 2 `protocolError`.

the 249-run figure attributes by actor and time window, which is the method ALT5 rejected for
`millisecondsToFirstLedgerWrite` and for the right reason — two concurrent runs by one actor could
cross-attribute. it is sound enough for a distribution and the five runs that use run-id correlation
show the identical shape.

## the two things it costs

**a claim written at 72% is a summary, not a claim.** it describes a decision already made and
already built on. the whole reason the kernel exists instead of a markdown note is that the
assumption is recorded before the work leans on it. that ordering is not happening, and until now
nothing could see that it was not.

**anything that dies before the burst loses everything.** this is why the entry beside it, on
timeouts, matters more than its own size suggests. two thirds of failed runs and a third of
cancelled runs left no record. the loss is not the last few minutes of a run; it is the run.

## what it should do

the field that measured this is `millisecondsToFirstLedgerWrite`, and it cannot support the next
question, because **null means four different things**: the launcher's correlation equalled the run
id, the child never wrote, the history read failed or hit its fifteen-second deadline, or the
interval came back negative. the field's own comment claims it distinguishes "a run which burned
tokens and recorded nothing from a run which did the work". it cannot — that case and a failed
measurement are the same value.

three steps, in order:

- **count the child's writes on `run.completed`.** one field, from the scan that already runs. zero
  writes then means "burned tokens, recorded nothing"; a null interval with a non-zero count means
  the measurement failed. it also makes the batching visible per run without reading a sidecar.
- **stop the scan at the first match.** the log is append-ordered by `recordedAt`, so the earliest
  event after `runStartedAt` is the first one found. today the scan reads every event in the task
  under a fifteen-second deadline, which degrades on exactly the tasks with the most runs, and
  degrades silently to null.
- **then decide whether to drive it.** a gate — refuse `work complete` when the item's runs all
  recorded in the last quarter of themselves — is available and is probably too blunt. the briefing
  asking for the claim before the first edit is the cheaper thing to try first, and the count field
  above is how you would know whether it worked.

## what this is not

it is not a reason to widen `EnsureCostRecordIsWellFormed`. C15 records that class of defect four
times: telemetry arithmetic inside the completion path that strands a finished run as active. the
count field is read on the same scan and must take the same precautions.
