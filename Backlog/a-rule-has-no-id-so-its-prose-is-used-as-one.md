a rule has no id, so its prose is used as one

`RefusalRecord` carries the kernel's message and no rule identity. Everything that needs to ask
"which rule was this?" therefore reads the prose and normalises it. `RefusalRuleKey.Of` is that
normalisation: take the first line, replace every quoted literal with `'X'`.

It is a good answer to a question that should not have to be asked, and on
`2026-09-20_0817-read-the-refusal-back` it produced three separate defects in one work item.

## what it cost, measured

| finding | round | cause | how it failed |
|---|---|---|---|
| RR1, Major | code review of increment 1 | the new diagnostic lines name records **unquoted**, so normalisation never reaches them | one rule refusing one actor sixteen times became sixteen keys |
| RR2, Minor | code review of the repair | `'[^']*'` matches across a newline, so a quoted literal spanning one is **severed** by a first-line cut | the interpolated text survives into the key |
| RR3, Blocker | code review of increment 2 | an **apostrophe in prose** is read as an opening quote, and the pairing runs one quote out of step | `...on another actor'X'alice'X'bob'.` — both ids leak in |

Every one is silent, and every one fails in the same direction: the key splits, `RepeatedKeys` keeps
only rows whose `Repeats` is above zero, and the rule leaves the retrospective rather than merely
undercounting. That is the flattering direction the normalisation exists to prevent.

Two of the three were caught by a code reviewer, not a verifier, and not by the suite — the tests on
that path used fixed strings containing no identifiers, so they passed either way.

## the shape

Emit a stable rule id at the throw site and record it on `RefusalRecord`.

    throw new GovernanceException(RuleId.EvidenceDirection, $"Evidence '{id}' does not …");

Then `RefusalRuleKey` is deleted rather than patched again, measure 9 groups on a field, the
repetition counter keys on the same field, and no wording can mislead either.

## why it was not done inside that task

`RefusalRecord` is written on a failure path by every command in the kernel, and the field is
optional-and-trailing or it breaks every journal ever written — the same constraint that put
`KernelIdentity` last. That is a change with its own blast radius and its own assurance, not a fourth
patch to a regex.

`D13` on that task accepted the third patch because the defect was live and silent, measured its
blast radius and opened this row in the same breath. **That measurement was taken with the wrong
instrument** and is corrected here: `E27` counted the *corpus* — 836 rows already written — and found
one key changing. A latent defect is not visible in what has already fired. Measured over the *code*,
27 of 427 `GovernanceException` messages carry a possessive and **two** mis-key today:
`LessonMarkRules.cs:201`, which the corpus saw, and `RunDispatchRules.cs:45`, which it could not
because that rule has never fired. The delivered lookbehind fixes both.

`D13` also says, in the code comment above the pattern, that it is the last patch there.

## what must not be lost

The normalisation is *correct* for what it can see. The defect is not in the regex; it is that a
rule's identity is being read out of a sentence a human wrote for another human. Any fix that makes
the sentence easier to parse is the fourth patch.
