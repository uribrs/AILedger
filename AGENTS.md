# Working in this repository

This repository is governed by its own kernel. Truth about the work — assumptions, evidence,
decisions, scope, escalations — lives in an append-only event log under `.ailedger/tasks/`, not in
markdown notes. Record it through the CLI. Do not create a parallel record in a file.

The methodology itself is not repeated here. It lives in `cognitive/RULES.md` and the skills beside
it, and the kernel serves the parts of it that apply to your role when you build context.

## Your first act: take your brief from the ledger

Do not orient yourself by reading the repository. Build your context and work from what comes back:

```bash
ailedger context build --task ledger-selfhost --actor claude-impl \
  --work W1 --cognitive-root cognitive --output /tmp/manifest.json
```

No task is named here on purpose. Every id this file has ever hardcoded went stale — one pointed at a
finished task, another at a task that had been archived that same day — and a fresh session that
takes its brief from an archived task starts from a record nothing will accept a write against. Find
the live one; do not trust a name in a document.

That manifest is your brief. It carries the goal, the claims your work item depends on, the
decisions and evidence attached to them, the active constraints, discarded approaches, open
escalations, the stop conditions, and the skills your role is allowed to use. Omit `--work` to get
the whole task instead of one item's slice.

This is not a convenience. It is the same mechanism that briefs a launched agent, and it is the
difference between working from what the task has settled and re-deciding it. `status` and `history`
are for inspecting the record, not for orienting in it.

## Where the ledger lives

```bash
ailedger --help                  # installed as a global tool; no path, no --root
ailedger status --task <id>
```

`ailedger` finds its ledger by walking up from the working directory for a `.ailedger` directory, so
no command needs `--root` and one issued from a subdirectory reaches the ledger in front of it rather
than a per-user path. The lesson store is found the same way, at `<home>/lessons`. Every mutation
still requires an explicit `--actor`.

If `ailedger` is not on your PATH, add `$HOME/.dotnet/tools` to it. If it is missing or stale, run
`sh scripts/install.sh` — that packs, reinstalls and prints the commit it was built from. Reinstalling
matters more than it looks: the installed tool and the built solution are two separate artifacts, so a
green build and a green suite say nothing about whether `ailedger` has your change. That silence cost
this repository real confusion twice in one day and is `Backlog/kernel-version-stamp.md`.

Only pass `--root` to reach a ledger that is not the one above you, which in practice means a test
fixture.

## Record a claim before you act on it

Anything you assume and then build on is a claim. Record it first, then earn it.

```bash
ailedger claim add --task ledger-selfhost --actor claude-impl --id C20 \
  --statement "The adapter discards the provider session on failure" \
  --consequence "A failed run cannot be resumed and the work is redone"

ailedger evidence add --task ledger-selfhost --actor claude-impl --id E20 \
  --source-type source-read \
  --citation "src/AILedger.Providers/Adapters/AgentAdapterBase.cs:104" \
  --summary "The catch block returns a result with a null session identity" \
  --supports C20

ailedger claim resolve --task ledger-selfhost --actor operator \
  --id C20 --status validated --evidence E20
```

A claim cannot become `validated` or `rejected` without an evidence record that names it by
direction — `--supports` or `--refutes`. Resolution needs the `resolveClaim` capability, which an
operator and a planning lead hold but an implementation lead does not; resolve as an actor that has
it.

`--source-type` is free text; this repository uses `source-read`, `local-probe`, `test-run` and
`live-run`. Repeat `--evidence` once per record.

Record approaches you considered and discarded, too:

```bash
ailedger alternative record --task ledger-selfhost --actor claude-impl --id ALT10 \
  --statement "Cache the manifest between runs" \
  --rejected-because "The manifest is role-filtered per run, so a shared cache leaks context"
```

## Work items and scope

Only an operator may add work. A work item's directory scope is occupied while the item is live, so
two agents cannot claim the same area:

```bash
ailedger work add --task ledger-selfhost --actor operator --id W10 \
  --title "..." --owner claude-impl --depends-on C20 --scope src/AILedger.Core
```

An item claiming more than one `--scope` is refused unless it also names an alternative recording
why those areas were not split into separate items. Holding two areas is one agent taking what two
could have held. The kernel does not judge that choice; it refuses to let it go unrecorded:

```bash
ailedger work add --task ledger-selfhost --actor operator --id W11 --title "..." \
  --scope src/AILedger.Cli --scope src/AILedger.Storage --not-split-because ALT10
```

## Completing work

A work item is worked by a run, verified by a verifier run, optionally reviewed by a code reviewer,
and only then completed. The kernel imposes that order — it is not a convention:

- `work complete` is refused until two runs against the item have completed: one whose subject held
  a working role, and one whose subject was a verifier. The refusal names which is missing — "no
  completed run" for the first, "verifier run" for the second.
- A code reviewer cannot start on a work item at all until a verifier run has completed against it.
- Runs against one item are sequential. A second cannot start while one is still active.

So completing W10 is three commands, not one:

```bash
ailedger provider launch --task ledger-selfhost --actor operator --subject claude-impl \
  --run R10 --work W10 --provider claude --cognitive-root cognitive
ailedger provider launch --task ledger-selfhost --actor operator --subject claude-verify \
  --run R12 --work W10 --provider claude --cognitive-root cognitive
ailedger work complete --task ledger-selfhost --actor operator --id W10
```

One operator-only flag waives both required runs. Its reason is required, and it stays in the log:

```bash
ailedger work complete --task ledger-selfhost --actor operator --id W12 \
  --without-verification "Documentation-only change; no verifier run was warranted"
```

Use it when an operator has decided the runs are not warranted, not when they are inconvenient. The
log records that decision permanently, and it is read as one.

An item that will never be completed is released instead, which hands its areas back. This is
operator-only, the reason is required, and it is refused while a run on the item is active or an
escalation against it is open — settle those first, withdrawing the escalation if it will never be
answered. An item already stale or completed cannot be abandoned:

```bash
ailedger work abandon --task ledger-selfhost --actor operator --id W11 \
  --reason "Superseded by a narrower split; the two areas are now separate items"
```

If `work add` is refused for overlapping scope, the holder is still live: finish it, abandon it, or
narrow your own scope. Do not mark work done that was not done.

## Interrupting the operator

Escalate only for a business decision or a true unknown, and only when the answer is not in this
repository. A business decision carries at least two options and a recommendation naming one. A true
unknown carries evidence of the attempt that failed to answer it.

```bash
ailedger escalation raise --task ledger-selfhost --actor claude-impl \
  --id X10 --kind business-decision \
  --question "Ship the narrow fix or rework the adapter?" \
  --option "Narrow fix" --option "Rework" --recommend "Narrow fix" --work W10

ailedger escalation raise --task ledger-selfhost --actor claude-impl \
  --id X11 --kind true-unknown \
  --question "Which account owns the provider credentials?" --evidence E20
```

An open escalation on a work item blocks completing it, which is the point: the question is
answered before the work is called done.

Only the operator resolves an escalation. Anything settleable from this repository, settle yourself.

## Launching another agent

`provider launch` runs `codex` or `claude` inside the governed task and hands it a manifest filtered
by the subject's role. An operator can dispatch on another actor's behalf with `--subject`, which is
how a role holding no run authority — researcher, worker, verifier, code reviewer — is launched:

```bash
ailedger provider launch --task ledger-selfhost --actor operator --subject claude-review \
  --run R13 --work W10 --provider claude --cognitive-root cognitive
```

A code-reviewer subject is refused here on the same grounds as `run start`: the work item must
already carry a completed verifier run.

The launching process closes the run. An agent inside a run must never call `run complete`,
`stage transition`, or complete or block its own work item.

## Dispatching work to other agents

If you are coordinating rather than implementing, dispatch through the kernel, not through your own
harness's subagents. This is the difference that matters most in practice:

- An agent launched with `provider launch` has the manifest as its only context. It cannot work
  outside the ledger, so everything it decides and every piece of evidence it finds lands in the
  record. Its run, its provider, its version and its role are all recorded.
- An agent spawned by your harness is invisible here. It may do excellent work and the ledger will
  show no run at all, so nothing it learned survives and no rule can reach it.

Both were measured on the same day in this repository. Codex, launched through the kernel and
briefed by nothing but the ledger, produced eleven claims, thirteen evidence records, seven
decisions and six discarded alternatives. Eight harness-spawned agents the same day produced work
the ledger records as zero runs.

So the brief for a dispatched agent is not a prompt — it is the ledger. Write the claim, the
constraint and the accepted decision first, then launch. The agent reads them as its manifest.

```bash
ailedger provider launch --task ledger-selfhost --actor codex-lessons \
  --run RL1 --work W3 --provider codex --executable /path/to/codex \
  --working-directory $PWD --cognitive-root cognitive --timeout-seconds 1800 &
```

Background it and `wait`; a launch blocks until the agent finishes. Give it a generous timeout —
two agents and your own session on one machine is slow, and a run that dies at its timeout comes
back `Cancelled`, which reads like a governance failure and is not one.

A coordinating lead should not also be implementing. Own the plan, the findings and the
documentation; give the code to the agents you dispatch.

## Identifiers when more than one agent is writing

Claim, evidence, decision and alternative ids are one flat namespace per task, and nothing allocates
them. Two agents composing `E1` at the same time will collide, and the loser's write is refused as a
duplicate — it is easy to believe you recorded something you did not.

Take a prefix of your own before you write anything, and keep to it. If you dispatch agents, give
each one its prefix in a constraint so the manifest carries it.

```
operator      C1, E1, D1      the task's own framing
lead          LC1, LE1, LD1   the coordinator's findings
codex-lessons XC1, XE1, XD1   one dispatched agent
```

Read before you write when others are live: `status` shows every id currently taken.

## What the next piece of work is

`docs/stage-engagement-design.md` is the design for closing the gap between this kernel and the
methodology in `cognitive/`. The short version, so you know whether it applies to you: the kernel
already contains the pipeline's eleven phases as stages and drives nothing from them, so the skills
reach every agent and none of them fire.

The decisions are in the ledger as LD11 through LD15 and the constraint naming the document is K19,
so `context build` will surface them. Read the decisions first and the document second — the ledger
is authoritative and the document is the prose that does not fit in a record yet.

## Changing the kernel

Every rule is written twice: in `CommandHandler` (command time) and in `TaskTransitionValidator`
(replay). Change both together.

They are not symmetric. Command-time rules may tighten over time. Replay-time rules may not — replay
must accept every history that was ever legal. Adding a rule only to the validator has twice made a
live task permanently unreadable. If a new rule keys on a field that older events do not carry, it
is safe by construction; otherwise it does not belong in the validator.

Run `dotnet test` before reporting work done.
