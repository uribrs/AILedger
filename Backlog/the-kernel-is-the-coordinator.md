# The kernel is the coordinator

**Backlog ID: 65. Priority: unranked. Status: open. Kind: change.**

Created 2026-10-06 at Uri's request, directly, without the kernel. No governed task, ledger mutation
or implementation was made for this entry.

## Problem

The model in the coordinating seat does not follow the process. The kernel binds it only where the
kernel refuses. A launched agent behaves because it cannot do otherwise. Claude and Codex show the
same pattern, so the cause is the seat, not the model.

Measured over 109 tasks, 2026-09-05 to 2026-10-06:

| | Launched agents (both providers) | Coordinating session (as `operator`) |
|---|---|---|
| Forbidden lifecycle or waiver commands | 0 in 1,148 run transcripts | 157 waivers from outer Claude, 205 from outer Codex; 0 typed by Uri |
| Refusals | 293 over 1,600 runs (0.18 per run), mostly format and id mistakes | 779; about 60% for rules the manual already states |
| Reaction to a refusal | n/a | stopped and reported 7% of the time |
| Process corrections from Uri | n/a | about 43, no decline over the month |
| Share of coordinator Bash calls that are `ailedger` | n/a | 57% |

Where the kernel refused, behaviour changed: stage waivers per week went 158, 138, 1, 0, 0 once the
arms were gated. Where it did not refuse, the coordinator skipped the step: the verifier, recon,
reading the brief, task-13, watching runs.

The coordinating job is routing. `workflow-coordinator` already calls itself pure routing, and
`nextActionContract` already computes what is missing next. Routing is mechanical, so it belongs in
code.

## Change

The kernel holds all state and decides every next step. Each LLM is a function call: one role, one
manifest in, one typed artifact out, no lifecycle authority. No conversational model coordinates.

```
 Uri ──request──► KERNEL (state machine, code only)
                    │
   intake ─► recon ─► plan ─► work ─► verify ─► review ─► done
     │        │        │       │        │         │
   agent    agent    agent   agents   agent     agent     ◄── manifest in, typed artifact out
                    │
 Uri ◄── escalations, contract approval, push approval, waivers
```

### Rules

1. **No LLM touches lifecycle.** No model may issue stage transition, run start or complete, work
   complete or abandon, waivers, claim resolution or escalation resolution. The driver issues these
   from typed outputs.
2. **Every output has a schema.** The kernel validates it. An invalid output is retried a bounded
   number of times, then escalated. Free text never drives a transition.
3. **Verdicts are fields.** Verifier and reviewer outputs carry `PASS`/`FAIL` and a findings list. A
   `FAIL` creates repair work items. Repairs are not capped: every real defect stays in the queue. A
   repeated finding of the same class sends the item to a re-plan run, because repeated instances
   usually share one cause (the closeout-synthesis truncation and parser-retirement WS7 chains).
4. **`operator` is provably human.** Remove operator capabilities from every non-human actor.
   Operator commands need an interactive confirmation that no agent can answer.
5. **Uri talks to the kernel.** An optional chat front-end may translate Uri's words into those
   commands. It holds no other authority.

### Uri's involvement

| Step | Command | Frequency |
|---|---|---|
| Start | `ailedger new "request"` | once per task |
| Contract | `ailedger approve contract` | once per task |
| Escalation | `ailedger answer X1 "..."` | about 1 per task (105 over 109 tasks) |
| Waiver | interactive confirmation | about 5 a month since 2026-09-13 |
| Ship | `ailedger approve push` | once per task |

Expect more escalations at first. Without a model that can waive or work around, every unforeseen
situation reaches Uri.

## Pieces

| # | Piece | Exists today | Size |
|---|---|---|---|
| 1 | Driver loop: read `nextActionContract`, launch the next run, wait, read the typed result, repeat | contract computation, `provider launch`, run outcome declarations | large; the core |
| 2 | Typed verdict on VerifierOutput and CodeReviewOutput | dispositions only; `VerifierOutputDocuments.cs` says they are "not an overall verdict" | medium |
| 3 | FAIL findings become repair work items; a repeated finding class triggers a re-plan run | none | medium |
| 4 | Strip operator capabilities from non-human actors; interactive confirmation for operator commands | role capabilities | small to medium |
| 5 | `new`, `approve contract`, `answer`, `approve push` | `task open`, `escalation resolve` | small |
| 6 | Bounded retry of a schema-invalid output, then escalate | schema admission on filing | small |

**Replay rule:** the verdict field and every new event must be optional on old events. Add the rule to
the command-time handler. Add it to the replay validator only when it keys on a field older events do
not carry.

## First slice

Build the driver for verify → review → close only, on work items whose code already exists. It shows
whether the driver removes the babysitting before the rest is built. Estimate, direct: about half a
day.

Full set, direct: about 2–4 days including tests (estimate). Run one cross-model review of the diff
before merging. In the month measured, the review step caught about 142 bugs in kernel code.

## Risks

- **Rigidity:** a situation the state machine does not model becomes an escalation.
- **Kernel defects reach every task.** About 142 were found in kernel code this month.
- **Brainstorming has no seat.** Uri's pre-scope exploration happens outside and enters as the request.
- **Intake is still judgment.** The intake and recon agents can mis-frame the task; recon and brief
  failures at intake were a recurring correction theme. Contract approval is Uri's check on it.

## Not measured

- Whether the driver reduces Uri's attention in practice. The first slice is the test.
- Whether escaped defects fall. The skills-era vs kernel-era escape comparison on
  cymulate-integration-adapters (task `2026-10-06_1026-skills-vs-kernel-escapes`) is undecided until
  the kernel-era windows close on 2026-10-18.
