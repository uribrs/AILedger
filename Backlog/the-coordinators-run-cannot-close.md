the coordinator's own run cannot be closed

filing a `PromptContract` or an `OrchestrationPlan` requires an active producer run. those two
documents are the coordinator's — `CLAUDE.md` says a coordinating lead should own the plan, the
findings and the documentation and give the code to the agents it dispatches.

so the operator starts a run, files both, and then cannot close it.

    ailedger run start --task T --actor operator --run R2 --provider none      # accepted
    ailedger artifact record --run R2 --kind PromptContract ...                # accepted
    ailedger artifact record --run R2 --kind OrchestrationPlan ...             # accepted
    ailedger run complete --run R2 --status completed
    error: Run 'R2' cannot be recorded as completed without a provider session
           identity; a completed run must stay resumable.

`RunRules.cs:118-127` explains the rule and it is a good rule: a completed run is one someone may
resume, resume needs the exact provider session, and the identity lives only in the adapter until
completion records it. terminal failures are exempt because a run that died before its session
existed has nothing to record.

an operator-held run has no session at any point in its life. it is not a provider run.

## what the task is left holding

three bad options.

- close it `failed`, and the log says a run failed when it filed two artifacts successfully.
- close it `cancelled`, which is what `2026-09-08_1048-refusal-journal` did, and the log carries a
  cancelled run that did its job. cancelled runs read as governance failures.
- leave it active, and Archive refuses the task — a task with active runs cannot be archived — so
  the problem returns at closeout instead of at Design.

none of the three is a true record, which makes this a recording defect rather than an
inconvenience.

## why the obvious fix is wrong

requiring the contract to come from a dispatched agent's run means dispatching an agent to write the
brief that agent's siblings will be dispatched with. the coordinator holds the reasoning; handing it
to a subagent to transcribe adds a launch, a manifest and a provider session to produce a document
the coordinator already wrote.

## the shape

the honest reading is that `run` models a provider session, and filing a document is not one. two
candidates:

- **let the two coordinator artifact kinds be filed without a run**, the way `UserRequest` already
  is. `ArtifactRules` already refuses a producer run on a user request — "a user-request artifact
  must be operator-authored and cannot name a producer run" — so the pattern exists and is one
  predicate wider.
- **exempt a run with no provider from the session requirement.** narrower in code, worse in
  meaning: it invents a second kind of run rather than admitting that filing is not running.

the first is smaller and says the true thing. a contract and a plan are authored, not executed.

## cost

one predicate in `ArtifactRules`, plus its replay twin, plus the stage arms that ask for a *current*
artifact of each kind — those already look at artifacts rather than runs, so they are unaffected.

command time may tighten; replay may not. every history that filed a contract against a run must
keep replaying, so the twin accepts both shapes and only the command-time copy stops requiring the
run.
