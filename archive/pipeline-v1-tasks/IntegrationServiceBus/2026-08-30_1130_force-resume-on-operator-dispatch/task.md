# Force the resume path on an operator-initiated dispatch

## The contradiction this closes

A failed collector's checkpoint is now retained for 7 days. The collector's `CanResumeFrom` declines
anything older than 23h, and `ExecuteWithResumeAsync` falls through to `ProcessAsync` when it does —
silently restarting the collection. So six of the seven retained days hold state that cannot be used,
and a manual resume of a 2-day-old run recollects from zero without saying so.

Retention window and resumable window must be the same window.

## The change

1. `AdapterRunMessage` gains a marker: this dispatch is an operator-initiated resume.
2. the dispatcher sets it on `ProcessEventCommand`.
3. `ExecuteWithResumeAsync` honours it: marker set + row present → `ResumeAsync` directly, gate skipped.

The automatic recovery sweep is unchanged and keeps the 23h behaviour. Only a marked dispatch overrides.

## Not in this change

The backend, the Admin control, the `AdapterDoneMessage` resumability fact. ISB-only.
