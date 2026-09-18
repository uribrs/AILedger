namespace AILedger.Cli.Help;

internal static class CliHelpText
{
    public const string Text = """
        AILedger 2.0 governed task CLI

        Global options: --root PATH        (default: platform local application data/AILedger/tasks)
                        --lesson-root PATH (default: platform local application data/AILedger/lessons)
        Every mutation requires an explicit --actor ID. Repeat list options once per value.

        version            (no options)   what this build was made from
        task open          --task ID --actor ID --title TEXT --goal TEXT [--tag TAG]
        status             --task ID
        who                --task ID   (actors, roles, live runs, role coverage, occupied areas)
                           Role coverage names, per role, the actors assigned to it and whether a
                           completed run has carried it, which is the staffing a stage arm requires.
        history            --task ID [--follow] [--since VERSION]
        retrospective build --task ID [--coordinator-session ID --coordinator-transcript PATH]
                           (what governance did on one task and what it cost)
                           Counts, durations and the causal chains the log can join, with no score,
                           grade or overall number anywhere, and a notMeasured list naming what this
                           task's record cannot answer. It is a read: run it after the fact, and
                           never from inside the archive transition.
                           coordinatorLoop measures the coordinating loop itself: each session's
                           wall clock and the part of it with no agent running, the delay from a run
                           finishing to the next disposition and the next dispatch, and every
                           avoidability verdict with the two sequences it was derived from. History
                           written before a session existed reports unbracketedHistory; no bracket is
                           ever inferred from a time gap. Every measure is reported for the task and
                           partitioned by coordinating actor, because the point is to tell one
                           coordinator's record from another's on the same task.
                           The two coordinator options are the only way the harness transcript is
                           read, and it is read only from the harness's own transcript directory. A
                           path outside it is refused by the containment check; the transcript's own
                           session identity is then checked against the session named. An outside,
                           missing, unreadable or mismatched one reports an absence naming the control
                           that refused it — never a zero and never an estimate.
        retrospective record --task ID --actor ID --id ID --title TEXT --body-stdin
                           Records a workflow retrospective only after the task reaches Archive,
                           then has no live work item, then has no active run, in that check order.
                           It takes no --run because it is filed after closeout, when no run is active.
        closeout evidence  --task ID --actor ID
                           Reports every assurance revision in event order with its digest, producer,
                           coverage and applicability; work-item runs; claims, evidence and challenges;
                           completeness counts; and loose review files matched to filed bodies.
                           It gathers facts and makes no finding, repair, severity, lesson or grade judgement.
        closeout status    --task ID --actor ID [--index-database PATH]
                           Reports every closeout eligibility check independently, lesson publication,
                           canonical log digests, and either the configured index path's presence or
                           an explicit not-configured result.
        task cleanup plan  --task ID --actor ID [--output FILE]
                           Writes a complete retention manifest without deleting task material. Every
                           file carries its digest, length, decision, reason, durable replacement when
                           one exists, and diagnostics lost by an authorised removal.
        task cleanup apply --task ID --actor ID --plan FILE
                           Applies exactly one manifest after rechecking eligibility, task identity,
                           state and canonical-log snapshots, every file, and path/link containment.
                           Intent and per-file receipts make interruption resumable and repetition idempotent.
        actor attach       --task ID --actor OPERATOR --target ID --role ROLE [--capability CAP]
        context build      --task ID --actor ID [--work ID [--also-work ID ...]] [--cognitive-root PATH] [--output FILE]
                           Serves the actor its brief, then records a context.built event naming the
                           skills it carried and their content digests. A repeat build of the same
                           skill set for the same actor appends nothing and leaves the version where
                           it was, so it is a read that conditionally records audit evidence, not a
                           read with no effect on the ledger.
        artifact record    --task ID --actor ID --id ID --kind KIND --title TEXT --body-stdin
                           [--work ID [--also-work ID ...]] [--run ID] [--supersedes ARTIFACT-ID]
                           New assurance output inherits its run's full membership when work flags
                           are omitted. Supplied work flags assert exactly that set. Legacy output
                           still requires --work; task-wide kinds must not use work flags.
        artifact show      --task ID --id ID [--json]
        artifact list      --task ID [--work ID [--also-work ID ...]] [--kind KIND]
                           Work selection intersects original coverage. Metadata retains historical
                           coverage and reports applicable members separately.
        claim add          --task ID --actor ID --id ID --statement TEXT [--consequence TEXT]
                           [--from-lesson LESSON-ID]
        claim resolve      --task ID --actor ID --id ID --status STATUS [--evidence ID]
                           [--superseded-by CLAIM]   (required when --status superseded)
        evidence add       --task ID --actor ID --id ID --source-type TYPE --citation TEXT --summary TEXT
                           [--supports CLAIM] [--refutes CLAIM]
        decision propose   --task ID --actor ID --id ID --statement TEXT --rationale TEXT
                           [--depends-on CLAIM] [--supersedes DECISION] [--from-lesson LESSON-ID]
        decision resolve   --task ID --actor ID --id ID --status accepted|superseded
        challenge raise    --task ID --actor ID --id ID --target-type TYPE --target-id ID --reason TEXT
                           [--evidence ID]
        challenge dispose  --task ID --actor ID --id ID --status supported|rejected|withdrawn
        work add           --task ID --actor ID --id ID --title TEXT [--owner ID]
                           [--depends-on CLAIM] [--scope PATH] [--not-split-because ALT-ID]
                           [--cognitive-root PATH]
                           [--without-brief REASON] [--with-stale-brief EVIDENCE-ID]
        work complete      --task ID --actor ID --id ID [--without-verification REASON]
        work block         --task ID --actor ID --id ID --reason TEXT [--escalation ID]
        work unblock       --task ID --actor ID --id ID
        work abandon       --task ID --actor ID --id ID --reason TEXT
        escalation raise   --task ID --actor ID --id ID --kind business-decision|true-unknown
                           --question TEXT [--work ID] [--option TEXT] [--recommend TEXT] [--evidence ID]
        escalation resolve --task ID --actor ID --id ID --status resolved|withdrawn [--resolution TEXT]
        alternative record --task ID --actor ID --id ID --statement TEXT --rejected-because TEXT
                           [--replaced-by DECISION] [--from-lesson LESSON-ID]
        lesson mark        --task ID --actor ID --source ID --repo NAME
                           --kind validated-claim|rejected-claim|rejected-alternative|
                                  resolved-escalation
                           --class refuted|untested|drifted
                           --verify COMMAND --do-not TEXT
                           --lesson-actor researcher|executor|verifier|recon
                           --verify-expects present|absent   (for a runnable --verify)
                           [--lesson-kind domain|workflow] [--audience ROLE]
                           [--tag TAG] [--supersedes LESSON]
        lesson recheck     --repo NAME [--id LESSON-ID] [--confirm VALUE]
                           [--working-directory PATH] [--timeout-seconds N]
                           Without --confirm it lists the stored verifies and runs nothing. With
                           --id and the confirmation printed beside that row it runs that one
                           command and reports whether the direction it recorded still holds. A
                           read: it writes no event, no projection and no store row, and only an
                           operator asking for it runs it.
        constraint add     --task ID --actor ID --id ID --statement TEXT --source TEXT [--scope TEXT]
        constraint supersede --task ID --actor ID --id ID
        run start          --task ID --actor ID --run ID [--work ID] --provider NAME [--session ID]
                           [--subject ID] [--coordinator-session ID]
                           [--also-work ID ...] [--candidate SHA256] [--verifier-run ID]
        run complete       --task ID --actor ID --run ID --status STATUS [--session ID]
        session start      --task ID --actor ID --id ID --harness NAME [--harness-session ID]
                           Brackets one coordinating conversation, so the runs it dispatches are its
                           children and the loop can be measured as a loop. One open session per
                           actor. --harness-session is the harness's own id for the conversation and
                           is what a transcript is checked against; a session opened without it
                           reports its token cost as an absence rather than trusting a path.
        session complete   --task ID --actor ID --id ID
                           Closes the bracket. An open session has no duration, so its wall clock and
                           its idle time are reported absent rather than measured against a clock the
                           projection does not take.
        stage transition   --task ID --actor ID --stage STAGE [--reason TEXT]
                           [--without-prerequisites REASON] [--serial-because ALTERNATIVE-ID]
        provider launch    --task ID --actor ID --run ID --provider codex|claude [provider options]
        provider resume    --task ID --actor ID --run ID --provider codex|claude --session EXACT_ID [provider options]

        Provider options: --work ID --subject ID --executable PATH --working-directory PATH --model NAME
                          --timeout-seconds N --add-dir PATH --cognitive-root PATH --output-schema VALUE
                          --without-brief REASON --with-stale-brief EVIDENCE-ID
                          --coordinator-session ID --cause EVENT-ID
                          --also-work ID (repeatable) --candidate SHA256 --verifier-run ID

        Assurance selection uses --work A --also-work B --candidate SHA256. Every member is explicit;
        duplicates and --also-work without --work are refused. Repeated --work retains last-value
        behavior. --candidate also opts a singleton into frozen assurance. Only verifier/reviewer
        roles may use it. A reviewer requires --verifier-run naming a completed verifier over the
        identical candidate and members. SHA256 is 64 lowercase hexadecimal characters: the kernel
        checks identity equality, while the coordinator checks actual candidate bytes externally.
        Frozen assurance requires a fresh provider launch; provider resume refuses it. Manual start
        must omit --session and records the new session at completion. Legacy singleton resume stays
        available. Providers receive every member scope (including every scope of a singleton) plus
        the ledger; file scopes grant their parent directory. New bundles refuse implicit repository
        root grants; use narrower scopes or an explicit authorized directory request.

        preflight batch    --task ID --actor ID --body-stdin [--cognitive-root PATH]
                           JSON providerLaunch members accept workItemId, coveredWorkItemIds,
                           candidateId, verifierRunId, runId and provider. New assurance requires
                           runId/provider. Preview checks one snapshot, never reserves work or
                           observes candidate files, and does not guarantee a later launch.

        --cause EVENT-ID on a launch names the coordinator record that prompted the dispatch — the
        finding it answers, the decision it carries out. It is what lets a dispatch that answered
        something be told from one that answered nothing, and it is the only input to that measure:
        no causal link is ever inferred, and none is backfilled. --timeout-seconds is recorded on the
        run as well as given to the provider, so a run that ended at its limit can be told from one
        that failed on its own merits.

        work add and provider launch are refused until the acting actor has built its context on the
        task and the skills it was served still say what they said then. --cognitive-root is what the
        digests are recomputed from, and a root that cannot be read is refused rather than waved
        through: a brief nobody can check is not a current brief. context build itself is never
        gated, so a fresh task is always openable.

        Two doors open that gate, because an absent brief and a stale one are different failures.
        --without-brief REASON is the operator's decision to proceed with no brief at all: only an
        operator may pass it, a blank reason is refused, and the reason is recorded as its own event
        before the work item or the run. Evidence cannot carry this case, because there is no
        evidence that an unread brief was read. --with-stale-brief EVIDENCE-ID is for a brief that
        exists and is no longer current: any actor that may run the command may pass it, and the
        kernel checks that the evidence record exists on this task and that there is a brief for it
        to be about — never whether the reason is a good one, the same contract as
        --not-split-because. Passed by an actor with no brief at all it is refused, and the refusal
        says to build context or to use the operator door.

        --subject dispatches a run for another actor: only an operator may pass it, and the run is
        authorised by --actor while the subject does the work, receives the manifest filtered by its
        own role, and owns the run's provenance. It is how a role that holds no run authority — a
        researcher, worker, verifier or code reviewer — is launched at all.

        A work item is completed only after two runs have completed against it: one whose subject
        held a working role, and one whose subject was a verifier. --without-verification REASON is
        the operator's override for both, and the reason goes in the log.

        stage transition --reason TEXT says why the stage moved, and which direction the move takes
        decides whether it is required. A transition that goes back in the pipeline — Repair to
        Verification, Execution to Design — is refused without it, and a blank reason is refused the
        same way. A transition that goes forward does not take it at all: passing --reason on a
        forward move is refused rather than ignored, so the flag never becomes decoration. Direction
        is the declared order of the stages, in which Repair follows Verification: Verification to
        Repair is forward and needs no reason, while Repair back to Verification does. The reason is
        recorded on the transition itself rather than as a separate event, and it stays in the log,
        so a later reader sees why the task went back and not only that it did. It is independent of
        --without-prerequisites; an operator moving backward past an arm passes both.

        stage transition --without-prerequisites REASON is the same shape for a stage: it is the
        operator's override for the arm guarding the target stage. Only an operator may pass it, the
        reason is required and a blank one is refused, and the reason is recorded as its own event
        before the transition. A later reader therefore sees which arm was skipped and why, rather
        than only that a transition happened. The Archive arm additionally requires an eligible
        lesson-bearing mark; the waiver does not skip that gate.

        stage transition --serial-because ALTERNATIVE-ID names an existing alternative explaining why
        an execution that could have been dispatched concurrently was run serially instead. Entering
        Verification is refused when the record shows a serial execution and nothing says why. The
        kernel concludes serial from the runs, not from a declaration: two or more work items that
        each carried a completed working run, holding disjoint scopes, with no two of those runs
        overlapping in time. Disjoint scopes mean the items could have been held at the same time, so
        running them one after another was a choice. The kernel does not judge that choice — running
        serially is allowed, running serially unrecorded is not — in the same way --not-split-because
        refuses an unrecorded choice not to split. The flag takes an alternative id rather than free
        text, because an alternative carries a statement, a rejection rationale, an actor and a
        timestamp, and free text carries none of them. The alternative must already exist: file it
        with alternative record first, then name it here.

        --serial-because and --without-prerequisites are not the same size. The waiver skips every
        arm on the target stage at once; --serial-because answers this one arm and leaves the others
        standing. Prefer the narrower flag: it is cheaper, and it leaves the record saying what was
        decided rather than that a gate was stepped over.

        --not-split-because names an existing alternative explaining why a work item claims more
        than one --scope area instead of being split into separate items. It is required only for a
        multi-area item: holding two areas is one agent taking what two could have held, and the
        kernel refuses to let that choice go unrecorded rather than judging whether it was right.

        work abandon releases an item that will never be completed, and hands its directory areas
        back for another work item to claim.

        lesson mark declares that one record is worth carrying into later tasks. Archiving mints a
        lesson only from marked sources, so a task with no marks teaches the next one nothing.
        --source names the record: a validated claim, a rejected alternative, or a resolved
        escalation. --supersedes names the lesson this one replaces, which keeps the older lesson out
        of a later task's recall without deleting it. Only an operator or a lead may mark: a mark
        decides what every later task inherits, which is scope authority rather than execution.

        --class says what kind of failure the lesson records: refuted, a belief that evidence
        contradicted; untested, one the work never produced evidence either way for; drifted, a
        decision that changed or was abandoned during execution. It is required, because a lesson
        that does not say which of those it is cannot later be judged stale. --repo names the
        repository the lesson came from and is required for the same reason: a lesson recalled in
        another repository is evidence about a system the reader may not be looking at. --tag is
        repeatable and carries what the lesson is about.

        --verify is the command that re-establishes the lesson today: a grep, a test filter, a path
        check. A lesson whose verify no longer resolves is stale on its face, which is the whole
        point of carrying one. When the citation cannot be checked by running anything, say so
        instead: "none" followed by a separator and the reason. A bare "none" is refused, and so is
        an invented command, because a fabricated verify reads as evidence to every later recall.

        --verify-expects says which way the verify has to come out for the lesson to still hold:
        present, the command succeeding, or absent, the command failing. It is required whenever the
        verify is a runnable command, because a verify was never required to be able to fail: a grep
        for a symbol that exists in both the defective and the repaired state passes either way, so
        running it re-establishes nothing and a lesson whose defect has since been fixed is recalled
        as current. It is refused on a verify that records there is nothing to run, which has no
        direction to state. lesson recheck is what reads it.

        --lesson-kind says what the lesson is about: domain, a fact about the software the task was
        building, or workflow, a fact about how this kernel and its pipeline behave. Absent reads as
        domain. It is separate from --class, which says what kind of failure the lesson records, and
        from --kind, which names the record it was minted from.

        --audience is repeatable and names the roles the lesson is addressed to — operator,
        planning-lead, implementation-lead, researcher, worker, verifier, code-reviewer. A lesson
        carrying an audience reaches only those roles' manifests. A lesson carrying none reaches
        every role, which is what a lesson with nothing role-specific to say means. The vocabulary
        is the role's, not --lesson-actor's: the audience is who has to read the lesson and
        --lesson-actor is which cognition established it.

        lesson recheck reads the lessons the store holds for one repository and reports whether the
        direction each recorded still holds. --repo is required rather than defaulted to the whole
        store because a verify carries repository-relative paths: another repository's lessons run
        from this working directory would all report as no longer holding, on the evidence that its
        files are not here. A row with no verify, no direction, or a verify that records there is
        nothing to run carries a reason instead of a confirmation, because there is nothing to run
        for it. A command that neither succeeds nor fails inside its deadline is reported
        indeterminate, which is not evidence either way. The report is a read for an operator, not a
        gate: nothing on the recall, context-build or stage-transition path runs it, and its exit
        code does not depend on what it found.

        Without --confirm the command lists the selected rows, each with the verify it would run and
        a confirmation value, and starts nothing. Running one takes --id naming that single row and
        --confirm carrying the value printed beside it. A verify is a caller-supplied field on a
        mark, so the store is a list of shell commands written by whoever marked the lesson, and an
        operator who asks for a repository-wide sweep approves the sweep rather than the commands it
        would discover. Passing the confirmation back is evidence that the text on screen is the
        text that will run. Without a matching one — a wrong value, no --id, more than one --id, or
        a row that cannot be rechecked — the command is refused before any process is created.

        --do-not says what must not be re-assumed without new evidence. --lesson-actor says which
        cognition established the lesson — researcher, executor, verifier or recon. It is spelled
        out rather than reusing --actor, which is the id of the actor issuing the command: the two
        vocabularies do not line up, an operator or a code reviewer establishes no lesson, and one
        option cannot carry both. All three are required, because a row missing any of them cannot
        be re-checked, cannot say what it forbids, or cannot be attributed.

        task open --tag is repeatable and says what the new task is about. Recall then hands the task
        the lessons carrying at least one of those tags, newest first, instead of the most recent
        lessons whatever their subject. A task opened without a tag recalls exactly as it did before
        tags existed, which is why a tag is worth giving: an untagged task is handed whatever was
        learned last, and the lesson earned for the work in front of it stays behind. A tag is
        trimmed, an empty one is refused, and repeating the same tag is refused rather than ignored.

        --lesson-root selects the store that lessons cross repositories in. Archiving a task
        publishes the lessons it minted there, and opening a task recalls from it as well as from
        this root's own archived tasks, so a lesson earned in one repository reaches the next task
        in another. A recalled lesson is stale operational evidence about a system that may have
        changed since; it is prior evidence to re-establish, never a settled fact.

        history --follow keeps printing, one JSON line per event, as each event is appended, until it
        is interrupted. It is how an operator watches several governed agents work in one feed rather
        than re-running the command to find out what landed. --since VERSION suppresses everything up
        to that task version, so a feed can resume where a previous one stopped without repeating it;
        the version an event produced is its position in the log, which status reports as "version".
        """;
}
