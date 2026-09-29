# Task-12 client trial

Implementation and installation are technical checkpoints. Tasks 7–12 client acceptance remain
pending. This first narrow trial is a **read-only deterministic package inventory audit**, with
real process interruption and recovery; it is not a model evaluation.

The verified installed identity is **2.0.178 from 142bab5**; see the
[installation receipt](installation.md) and [retained recovery example](examples/recovery.md).

From the `codex/structured-findings-contract` worktree, on macOS outside an enclosing sandbox:

```sh
ailedger version
python3 tools/EpisodeProbe/run.py /Users/user/.local/bin/ailedger /tmp/ailedger-task12-client-trial
```

The output directory must be new. The script copies committed frozen Axonius/Falcon/S3/current
snapshots through task 11's existing probe, prepares an explicit `read-only-audit` objective from
the Axonius snapshot, and exercises seven offline cases. It never modifies original histories or
launches a model. Initial source hashes and the final source hash must match. Temporary fixtures
can disappear on reboot; retain the resulting JSON when inspecting a trial, or rerun into a new
root and accept its new identity. Never relabel an old package/root/hash.

Inspect these concrete outputs:

1. `audit-handoff.json`: exact objective, non-goals, output/checks, read grants, budgets, source
   inventory and omission risks. The audit counts versioned records and declared omissions; it
   explicitly leaves semantic sufficiency unknown.
2. `cancel/interrupted-result.json`: actual SIGTERM, partial authored result and its submission
   receipt. `cancel/resume-result.json` must contain that **same** checkpoint receipt and one new
   final receipt. The source record count/digest must remain bound to the original package.
3. `host-kill/interrupted-result.json`: a hard-killed host with `unknown` outcome and no invented
   successful ending. The script stops the whole disposable process group, confirms that fact in
   reconciliation, and resumes. Inspect `reconciled-result.json` and `resume-result.json`; the
   reconciliation says operator attestation and the interrupted invocation remains unknown.
4. `timeout/run-result.json`: retained partial output with an exhausted elapsed budget; resume
   cannot reset the deadline. `failed/run-result.json` retains the provider's nonzero exit.
5. `retrieval/run-result.json`: one authorized omission read and one follow-up invocation, with
   actual query/reply identity. `sandbox/run-result.json`: denied authority-file read, file write
   and socket open. `inventory/run-result.json`: exact execution retry without another launch.
6. `report.json`: installed build identity, case outcomes, preserved source digest and explicitly
   missing provider usage. A complete process is not an acceptance verdict.

For manual interruption of another locally authorized offline audit, use the generated host file
and body as a shape example, **not a reusable grant for another package**:

```sh
ailedger episode run --authority /absolute/authority.json --store /absolute/results \
  --scratch /absolute/scratch --body-stdin < /absolute/start.json
# Interrupt after a partial submission. For SIGTERM/Control-C, the host performs bounded cleanup.
ailedger episode inspect --authority /absolute/authority.json --store /absolute/results --request-id audit-1
ailedger episode resume --authority /absolute/authority.json --store /absolute/results \
  --scratch /absolute/scratch --body-stdin < /absolute/start.json
```

For a hard host kill, first stop any remaining provider using the exact executable path recorded
in `invocation`. Then reconcile explicitly; do not attest merely because the host is absent:

```sh
ailedger episode reconcile --authority /absolute/authority.json --store /absolute/results \
  --request-id audit-1 --confirm-provider-stopped
```

Keep the original principal, request IDs, package, executable hash and limits for resume.
A stale input or expired deadline requires stopping and obtaining a new bounded intent; it cannot
be bypassed by editing the grant. Partial data stays inspectable. Inspect pending-file counts,
unknown usage/inventory, process outcome, authored result and acceptance independently.

## Ordinary use and later model proposal

Uri can apply the same deterministic inventory script to a newly curated, authorized read-only
package by pinning its versions, setting the audit objective/checks and issuing a host authority
file for that exact package/executable. Inspect whether early output was useful, recovery avoided
reconstruction, omissions stayed visible, and any remaining JSON/CLI preparation was burdensome.
Do not infer cognition improvements from the scripted audit.

No real model episode is needed to verify this increment. If a cognitive trial is wanted later,
a concrete proposal is: one read-only Axonius package assessment, one fixed model/provider, one
partial checkpoint followed by an intentional interruption, at most two invocations and ten
minutes, with a proposed **$1 total hard cap**, zero source writes, task-10 reads only, and independent
inspection of retained no-dedup rationale and environment limits. That is **not authorized here**.
The installed offline bridge does not provide a billable adapter or enforce a dollar cap for one;
a separately reviewed provider/spend boundary is a prerequisite, not an implicit grant or claim of
current support. Do not enable network or route through fictitious governed histories to run it.

Acceptance remains Uri's decision after inspecting an actual client trial. Stop at task 12.
