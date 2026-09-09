the timeout is set at the median run, so half the runs die at it

`--timeout-seconds` is chosen by hand at each launch. the values in use are 300, 600, 900 and 1200.
the median completed run takes 586 seconds.

so a run launched with a 600-second timeout is given about fourteen seconds of margin over the
typical run, and the ledger records the result.

## the measurement

every cancelled run's duration, sorted:

    1 ×8, 54, 112, 155, 179, 300, 300, 301, 551,
    600 ×9, 601, 850, 900 ×8, 1061, 1200 ×5, 1800, 1803

**twenty-eight of the forty-three land within three seconds of a configured wall.** the eight at one
second are operator-held runs with provider `none` — see item 24, which is about that conflation.
seven others died somewhere in the middle and are genuine failures of another kind.

what the walls are being compared against:

| | median | p90 | max |
|---|---|---|---|
| completed runs | 586s | 1150s | 3142s |
| cancelled runs | 600s | 1200s | 1803s |

the cancelled distribution is not a distribution of runs that went wrong. it is a picture of where
the walls were placed.

## why it reads as a governance failure and is not one

a run killed at its timeout comes back `Cancelled`, and `CLAUDE.md` already warns that this "reads
like a governance failure and is not one". the warning is right and it does not help: the record
still says cancelled, `self-scoring` will read it as a cost signal, and item 24 exists because the
same word covers four different situations.

worse, it compounds with the end-batched writing in the entry beside this one. an agent that records
nothing until 72% of the way through its run, killed at a wall set near the median run length, loses
its entire contribution to the record rather than its last few minutes. nineteen of thirty failed
runs and sixteen of forty-three cancelled runs recorded nothing at all.

## what it should do

the cheap half is a better default, and it is worth doing before anything clever: **1800 seconds**,
above the p90 of completed work. nothing in the ledger suggests a lower wall buys anything, and the
p90 of legitimate runs is 1150 seconds.

the honest half is that a fixed wall is the wrong instrument. the launcher now records what a run
cost, so the timeout could be derived from what comparable runs have taken — same role, same
provider, same task — with the default as the floor. that needs a population, which is item 7's
business and not this entry's.

a third option worth naming so it is not rediscovered: warn instead of kill. the process is not the
thing at risk; the record is. a launcher that, at the wall, signals the child to record what it has
before terminating turns a total loss into a partial one. that only works if something can reach the
child, which today nothing can.

## what it costs to be wrong here

twenty-eight runs. at the measured cost of a typical run — roughly 580,000 billed-equivalent input
tokens — that is the largest single line of avoidable spend in the ledger, and it is one number in a
launch command.
