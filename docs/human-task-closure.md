# Human task closure

When agents have drifted beyond the useful result, the human operator can close the task
successfully without finishing the remaining workflow:

```sh
ailedger task close --task TASK --actor operator --human-override \
  --reason "The requested result is sufficient; the remaining investigation is outside my scope."
```

The command displays the exact task, ledger directory, state version, actor, reason, and counts
of interrupted runs and unfinished work. The human must choose **Confirm successful closure**
and authenticate with macOS Local Authentication (Touch ID or the account password). Cancel,
failed authentication, missing helper, and unsupported platforms leave the task unchanged.
There is no `--yes`, stdin approval, saved token, or unattended fallback. The flag requests the
human interaction; it does not grant approval. A blank reason is refused. The dialog explicitly
authorizes closure of the entire task, including work added while it is open.

The operator role alone is insufficient. Neither a provider session nor routine orchestration
can issue this operation. The confirmation creates an in-memory admission bound to the task,
actor assignment, reviewed version, and reason; serialized commands cannot carry it. Admission
expires after two minutes. The kernel records both the reviewed version and the actual version
closed under its mutation lock. Ordinary agent writes cannot invalidate whole-task consent and
keep the task alive; a changed operator assignment does require a new confirmation. If the
CLI response is lost, inspect `ailedger status --task TASK` before trying again: the closure is
one durable event and cannot be applied twice.

## What successful closure means

The task moves directly to `Archive` and reports `humanClosure.outcome` as
`succeeded_by_human_override`. The append-only event records the reason, authenticated local
account, operator actor, prior stage and version, confirmation time, interrupted runs, and
excluded work items. This is a successful human decision about the task's outcome. It is not an
independent verification result.

Existing completed work remains completed. Unfinished work becomes abandoned with the closure
reason. Active ledger runs become cancelled and open coordinator sessions end. Claims, evidence,
decisions, challenges, escalations, and assurance receipts remain intact. Failed or missing checks
are not changed to passes, no acceptance is manufactured, and no lessons are automatically minted.
`status` shows remaining debt as `unresolvedAtHumanClosure`; `closeout status` and `closeout evidence`
include the human closure. Workflow gates and lesson/retrospective requirements do not block this
operation. Cleanup eligibility remains separate: successful closure does not authorize deleting
the retained history or unresolved evidence.

Closure is terminal. All later workflow mutations are refused, including further dispatch and
agent recording. Task-13 governed sessions lose authority as well. To do more work, open a new
task with a fresh scope.

## Active processes

Kernel launchers from 4.2 onward watch for human closure and cancel their providers through the
existing process-tree cleanup path. Late results may still be retained for diagnosis; they cannot
reopen the task or turn an interrupted run into a success. Cancellation of the ledger run does not
assert physical termination. The CLI reports `processTermination: unconfirmed` when it interrupts
active runs. Check retained execution observations before treating the working directories as idle.

Already-running older launchers do not gain the watcher when the tool is upgraded. Stop those
launchers separately. Old runtimes also cannot read the new closure event, so they fail closed
on further ledger access. Detached external sessions and external services are not killed by
this operation. Task closure remains successful even while process cleanup is outstanding.

## Host boundary and packaging

The initial confirmation host supports macOS only. Other platforms fail closed until an equivalent
human authentication host is implemented. The native helper is built with Apple's command-line
tools and packaged beside the CLI. It uses Apple's
[Local Authentication API](https://developer.apple.com/documentation/localauthentication/lacontext)
with a fresh context and no Touch ID reuse interval. It exposes no mock or bypass switch.

This follows the kernel's existing trusted-host boundary: installed kernel binaries and the ledger
must be protected from agent replacement. It does not defend against arbitrary same-user code
that can replace the kernel, inject into its process, or rewrite its event log. Tests substitute the
confirmation host only through an internal test seam; the shipped CLI has no option for doing so.
