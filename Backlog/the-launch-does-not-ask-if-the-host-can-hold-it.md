refuse a launch the host cannot support, before it starts a run

`provider launch` checks authority, scope, role and staffing. It does not check whether the machine
has the memory to run the agent, whether that provider is authenticated, or whether the launcher's
sandbox can reach the provider state it needs. Those are host-readiness failures, not agent runs.
Today they are discovered only after `run.started`, so predictable failures consume run ids, occupy
work, pollute governance history and delay independent verification.

## what actually happened

Three agents were live on one 48GB laptop: this session, a launched `claude`, and a launched `codex`.
Memory ran out. The kernel had already recorded `run.started`, so each kill left an active run that
had to be closed as `Cancelled`, and a `Cancelled` run reads as a governance event.

The standalone-memory close-out exposed the other two cases on one work item. `RV4FINAL` reached
Claude without usable authentication and returned `Not logged in` after three seconds. Two Codex
launches, `RV4FINAL2` and `RV4FINAL3`, ran inside a wrapper sandbox that could not provide the
provider access they needed and failed in under four seconds. Equivalent launches with host access,
`RV4FINAL4` and `RV4FINAL5`, completed under Codex and Claude. Three failed governed runs were
therefore recorded to discover conditions the host already knew before useful work began.

Across the whole ledger:

    runs        182
    cancelled    18
    failed       18

Not all eighteen cancellations are host kills — `cancelled-means-four-different-things.md` is the
entry about not being able to tell which are which, and this entry is one of the four meanings it
names. That entry makes the record legible after the fact. This one stops the event from happening.

## why the kernel is the right place for the check

The kernel already refuses a launch for seven authority-and-scope reasons before any run is recorded,
in `CliApplication.ResolveProviderGrants`. Insufficient memory, missing authentication and an
inaccessible provider home are the same kind of refusal: they are knowable before the run exists,
and knowing them afterwards costs a stranded run, a misleading status, provider startup time and
whatever the agent had done before it died.

The alternative — let it start and fail — is what happens now, and it is worse than a refusal in
three ways. The run is recorded, so the work item is occupied and the next run on it is blocked. The
cancellation is indistinguishable from a real one. And the agent's partial work is unattributable,
because a killed process files nothing.

## the shape

One adapter-level readiness contract, used by both an explicit `provider preflight` command and
`provider launch`, runs before context construction and before `run.started`. It returns structured,
non-secret results rather than scraping a provider's normal launch output:

- executable found and runnable;
- authentication usable, using a provider-supported non-consuming status probe where one exists;
- provider state directories readable and, where startup requires it, writable from the effective
  launch sandbox;
- required local sockets or subprocess capabilities available without making a model request;
- memory headroom sufficient for that provider and the current number of active runs.

The memory check must retain the measured design already established here:

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

Authentication and sandbox checks must fail with distinct codes such as `not-authenticated`,
`provider-state-inaccessible` and `sandbox-capability-denied`. The message names the failed probe and
the remediation, but never prints credential material. A short timeout classifies an inconclusive
probe separately from a confirmed authentication failure.

`provider preflight --provider codex|claude` gives the operator the same answer without creating a
task or run. A multi-provider form makes cross-provider verification availability visible while the
workflow is still being planned, and gives `single-agent-relaxation.md` an evidence-bearing input
instead of discovering provider availability at the completion gate.

## acceptance criteria

- A missing login, inaccessible provider-state directory, denied required sandbox capability and
  insufficient memory each refuse before any `run.started` event is appended.
- The refused launch starts no provider agent session, makes no model request and consumes no
  provider tokens; a documented lightweight status subprocess is allowed when the provider exposes
  no safer authentication probe.
- The explicit preflight and launch path use the same implementation and return the same structured
  reason for the same host state.
- Tests use fake adapters and disposable directories to prove the provider launch method is never
  called after a failed preflight; no test depends on the developer machine's real credentials.
- A successful preflight does not promise that the provider cannot later fail. It proves only the
  named startup prerequisites at the time checked, and its message says so.
- Logs and ledger refusals contain provider name, probe kind and safe diagnostic text, never tokens,
  cookies, environment values or credential-file contents.

## the escape hatch this needs

An operator who knows better must be able to proceed, the way `work complete --without-verification`
already works: an operator-only flag, a required reason, and the reason stays in the log. Without it
the check becomes the reason the kernel gets bypassed, and `waivers-need-a-floor.md` is the entry
about what a bypass with no floor turns into.

## cost

One provider contract, small adapter-specific probes and one comparison in the launch path, plus the
explicit command and override flag. It touches no canonical event, rule copy or replay: a refusal
before `run.started` writes nothing, so there is no history for the validator to keep accepting.
That is the same reason the seven grant refusals live where they do.
