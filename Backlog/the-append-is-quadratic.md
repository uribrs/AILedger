every mutation rewrites the whole event log

`FileGovernedTaskService.cs:465` — the append path copies the entire existing log into a temporary
file, writes the new line, flushes to disk, and renames over the original.

so a task's N-th command copies N-1 events. over a task's life that is O(N²) bytes written, with a
full `flushToDisk` on every one.

## where it stands today

    ledger-learning              575 events   394 KiB   ~110 MiB copied over the task's life
    decompose-command-handler    333 events   378 KiB    ~61 MiB
    stage-arms                   336 events   348 KiB    ~57 MiB

invisible at this size — a 400 KiB copy with an fsync is a few milliseconds and nobody notices.

the cap is 10,000 events and 64 MiB, not the 1,000 / 16 MiB the README claims. at the current average
line size of about 1.2 KB, 10,000 events is a 12 MB log. each command then copies and fsyncs 12 MB,
and the task writes roughly 60 GB over its life to record 12 MB of truth.

## why the lines are fat and getting fatter

`artifact.recorded` carries its content inline. one verifier output in the decompose task is 20,371
characters. that is the right design — it is what makes the log self-contained and worth committing —
but it means the average event line grows as artifacts become the normal way work lands. the
quadratic term and the constant are both moving in the wrong direction at once.

## the shape

append in place. open with `FileMode.Append`, write the line, `flushToDisk`. on POSIX a single
write under the size of a pipe buffer is atomic, and the task mutation lock in
`TaskMutationLock.cs` already guarantees one writer.

what the full-copy pattern buys is atomic replacement — a crash mid-write cannot leave a partial
line. that property is worth keeping, and it can be kept much more cheaply: on read, if the final
line does not parse, drop it. an append-only log with a torn tail is the one corruption shape that
is trivially recoverable, because a torn line is by definition the last one and by definition was
never acknowledged to the caller.

`RecoveryTests.cs` is 457 lines and already the right place to pin that.

## what this is not

not a compaction path. the refusal message at the byte cap is correct and should stay: a task id is
embedded in every event, there is no rewriting, and the answer to a full log is to archive it and
open a successor that depends on the claims it validated. this entry does not change that. it makes
the run up to the cap cost linear instead of quadratic.

## why it is worth doing before it hurts

because the symptom is a slow CLI, and a slow CLI is the thing most likely to make someone stop
using the kernel for small work. that is the failure this repository can least afford — the door is
already voluntary. every 100 ms added to `claim add` is an argument for not recording the claim.

## cost

one `FileStream` construction changed, one guard on read, and the recovery tests extended. no
schema change, no rule change, no twin. existing logs replay unmodified.
