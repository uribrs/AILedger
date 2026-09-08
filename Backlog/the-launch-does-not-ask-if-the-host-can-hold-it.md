refuse a launch the machine cannot hold, before it starts a run

`provider launch` checks authority, scope, role and staffing. It does not check whether the machine
has the memory to run the agent it is about to start. Two runs today ended `Cancelled` because the
host killed the provider process, and the ledger records them the same way it records a run the
operator interrupted.

## what actually happened

Three agents were live on one 48GB laptop: this session, a launched `claude`, and a launched `codex`.
Memory ran out. The kernel had already recorded `run.started`, so each kill left an active run that
had to be closed as `Cancelled`, and a `Cancelled` run reads as a governance event.

Across the whole ledger:

    runs        182
    cancelled    18
    failed       18

Not all eighteen cancellations are host kills — `cancelled-means-four-different-things.md` is the
entry about not being able to tell which are which, and this entry is one of the four meanings it
names. That entry makes the record legible after the fact. This one stops the event from happening.

## why the kernel is the right place for the check

The kernel already refuses a launch for seven authority-and-scope reasons before any run is recorded,
in `CliApplication.ResolveProviderGrants`. A host that cannot hold the process is the same kind of
refusal: it is knowable before the run exists, and knowing it afterwards costs a stranded run, a
misleading status, and whatever the agent had done before it died.

The alternative — let it start and fail — is what happens now, and it is worse than a refusal in
three ways. The run is recorded, so the work item is occupied and the next run on it is blocked. The
cancellation is indistinguishable from a real one. And the agent's partial work is unattributable,
because a killed process files nothing.

## the shape

A pre-launch check on available memory, refusing when the headroom is below a floor:

- read available memory, not free pages. On macOS `Pages free` is near zero on a healthy machine and
  swap does not shrink after it grows, so both read as exhaustion when there is none. `memory_pressure`
  reports the figure that means what it looks like. This is the specific mistake to encode in the
  check, because it was made in this repository and reported to the operator as a diagnosis.
- a floor per provider, not one global number, and a floor that is a default rather than a constant:
  the same launch is safe on an idle machine and fatal with two agents already live.
- count the agents this kernel already has live. `state.Runs` knows how many runs are active, and
  those are processes. A launch that would be the third concurrent agent is the one to refuse.
- refuse, do not warn. A warning on a launch the operator backgrounded and walked away from is a
  warning nobody reads.

## the escape hatch this needs

An operator who knows better must be able to proceed, the way `work complete --without-verification`
already works: an operator-only flag, a required reason, and the reason stays in the log. Without it
the check becomes the reason the kernel gets bypassed, and `waivers-need-a-floor.md` is the entry
about what a bypass with no floor turns into.

## cost

One probe and one comparison in the launch path, plus the flag. It touches no event, no rule copy and
no replay: a refusal before `run.started` writes nothing, so there is no history for the validator to
keep accepting. That is the same reason the seven grant refusals live where they do.
