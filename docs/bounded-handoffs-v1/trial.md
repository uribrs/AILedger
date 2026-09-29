# Uri's task-11 trial

Installed version: **2.0.176 from 4a0f117**. See the [installation receipt](installation.md) and
[prepared current-task example](examples/current-reanchor-handoff.md).
Implementation and installation do not accept this task or tasks 7–10. No agent trial has run.
This is a read-only handoff trial; implementation and the task-12 runtime remain out of scope.

## Generate an inspectable handoff

From the requested worktree, using the installed verified executable:

```sh
ailedger version
python3 tools/HandoffProbe/run.py /Users/user/.local/bin/ailedger /tmp/ailedger-task11-trial
```

The script uses committed frozen inputs only, copies them into a new disposable ledger root,
checks their hashes, prepares all four examples, rejects spoofed body attribution and deliberately
retrieves one omitted record per package. It does not run an agent or modify a real task.
The report identifies that disposable `fixture_root`; the package's `ledger_identity` points to
its `tasks` directory. Keep that directory while trying retrieval. If deleted, regenerate the
package with a new root; do not relabel the old package or mix hashes across preparations.

Start with `/tmp/ailedger-task11-trial/current-reanchor-handoff.md` and the matching JSON envelope.
The original current task was available at version 39, last event 2026-09-28T07:43:17.544067Z,
when read on 2026-09-29. This is a snapshot of real unfinished work, not an invented trial task.
Its source task dependency excerpt is separately pinned at version 404. Inspect `D1`, `C1`,
`RPD1/RPD2`, branch/ref constraints, current technical contract and source-design attachment.
Before using it for live planning, compare current original source identities read-only; stop and
re-curate if they changed. This package is not permission to implement either task.

## Try the content, then assess it

1. Inspect the objective, non-goals, source versions, grants, budgets, preservation assessments,
   omission inventory and stop conditions. Confirm the desired read-only trial in your normal
   client session. Package data does not authorize a new paid provider launch.
2. Supply only the chosen envelope/rendering and `profiles.md` to a fresh ordinary session you
   authorize. Ask it to produce the declared cited checklist/assessment, including missing inputs,
   proposed decisions and uncertainty. Keep source material as evidence. Do not give it the
   accumulated conversation, this evaluator discussion or `evaluation.json` as an answer key.
3. Permit targeted source reads through the existing authorized `retrieve_context` tool, or its
   CLI adapter below when using the disposable operator snapshot. Do not switch a real bound
   agent to operator to defeat isolation. If the tool is unavailable/denied, record that friction;
   do not silently invent access or reconstruct a different version.
4. Preserve the actual output, any actual extra reads (reference, digest, returned bytes, reason),
   elapsed time and available provider usage once. Unavailable measurements remain unknown.
   Inspect omissions that caused reconstruction or a missed requirement. A short package alone
   is not a successful trial.
5. Compare the result to `tests/Fixtures/bounded-handoffs-v1/evaluation.json` only after the output
   is final. For historical trials, use their individual cutoff snapshots and keep later source
   logs, evaluation material and other example packages away from the recipient. These are loss
   diagnostics, not a causal experiment or proof of better judgment, cost or delivery time.

A CLI retrieval uses the exact `reference.retrieve` object from the package (copy it to
`/tmp/query.json`), at its ledger root, task and actor:

```sh
ailedger handoff retrieve --root <package-ledger-identity> \
  --task <package-snapshot-task-id> --actor operator --body-stdin < /tmp/query.json
```

Check `status`. For chunked output, retain the same version/digest and use each returned
`next_offset`; concatenate `json_chunk` strings exactly, verify SHA-256, then parse. A stale result
requires reinspection, never mixing versions. Full `data` is a typed record; do not hash arbitrary
pretty-printing and call that the original record digest. Source attachments retain their own
locator and hash; they are not fetched by `retrieve_context`.

For a newly curated authorized task, use `handoff index` with its actual recipient binding,
construct a request in the same shape as the generated `*-request.json`, and invoke
`handoff prepare --body-stdin`. Give a reason/risk/trigger for every omission and preserve all
material inputs. No automatic selector or authoring UI is claimed.

## Remaining acceptance gaps

No model has received these packages, no independent output comparison has occurred, and no
elapsed/cost/token benefit is measured. Frozen event snapshots preserve recorded candidate/ref
identities but do not contain complete historical product checkouts; availability of those bytes
must be established before behavioral verification. Current-source access and snapshot capture
were read-only; the actual current-task recipient has not tried this package. Existing tasks 7–10
client trials remain pending. Stop at task 11 after this inspection/trial.
