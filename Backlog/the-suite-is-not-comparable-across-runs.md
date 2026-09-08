two tests decide their result from the sandbox, not from the code

Three runs on `2026-09-08_1428-run-cost` reported the suite:

    R19  worker    526 passed   2 failed
    R20  verifier  526 passed   2 failed     independently reproduced
    R21  repair    534 passed   0 failed     after a change touching neither test

The repair added six test cases. Six plus 526 is 532, not 534. The other two are
`KernelVersionTests.ACleanTreeAtADifferentCommitWarns` and `ADirtyTreeAtADifferentCommitStillWarns`,
which had failed in both earlier runs and passed in the third — with no change to their code and none
to what they test (C31, E43, E44).

## why

They are the only two tests in the suite that create a real git commit. Both open with:

    Assert.True(InitRepositoryWithOneCommit(home.Path), "git could not create the fixture repository");

and that helper runs `git init`, two `git config` calls and `git commit --allow-empty` inside the test
process. A run whose sandbox refuses `git commit` fails them; a run whose sandbox allows it passes
them. Nothing about the kernel is being measured either way.

## why the assertion is right and should stay

`KC4`, from the task that shipped the version stamp, records that this test used to `return` without
asserting when git setup failed — so the only test proving the warning can fire was also the only
test that could pass while proving nothing. Making the fixture part of what is asserted was the fix,
and it was correct. Failing loudly is better than a green tick over nothing.

The problem is not that they fail. It is that they fail **for a reason unrelated to the tree**, and
three agents in a row recorded the count without establishing why. Every one of them handled it
correctly — excluded the two, compared against a clean-HEAD baseline, and did not report a
regression. None of them asked what the two were.

## what it costs

The suite's counts are not comparable between runs. `526/2 here and 526/2 at clean HEAD, therefore no
regression` is a sound argument only when both halves ran under the same permissions, and nothing
records what those were. A genuine one-test regression in a run that also gained git permission would
show as `534/0` and read as an improvement.

This is the `kernel-version-stamp` lesson pointing at itself: that task found four separate tests
that passed while proving nothing, and the repair for one of them created a test whose result depends
on the harness.

## the shape

`KernelVersion.Warning` is already pure and takes the head reader as a `Func<string?>` — its own
comment says "Pure so the fire path can be proved without a git or a filesystem", because the defect
that shipped with the feature was unreachable by any test that only asserted silence against this
machine's own repository.

So:

- prove the fire path against the pure function, with no git and no filesystem. That is what it was
  made pure for, and those assertions run identically in every sandbox.
- keep **one** git-backed test as an integration check, and have it state plainly in its failure
  message that git could not commit in this environment — so the failure names the environment rather
  than looking like a product defect three runs in a row.
- do not add a skip. A skipped test is a green tick over nothing, which is the thing `KC4` was
  written about.

## cost

Two tests moved onto the pure function and one left where it is. No production change.
