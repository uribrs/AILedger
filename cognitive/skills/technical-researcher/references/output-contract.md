# Output Contract

Load this file at step 6 of `../SKILL.md`. It defines the structure of the research document written
to `<taskPath>/research/<topic>.md`, and of the same output returned inline when there is no
`taskPath`.

This is a structure contract. It states what the finished document must contain — every section,
every required field, every permitted value — and what must not appear in it. Those are the things a
reader can check against the finished document, which is the job this file does.

It does not state how you arrive at a value. Classification and emphasis are settled in
`task-emphasis.md`, posture and confidence in `research-standards.md`, and the source order and the
research method in `../SKILL.md`. Where a requirement here rests on a rule, this file names the file
that owns the rule instead of repeating it.

The permitted values are repeated here because a contract that names a field but not its values
cannot be checked.

## Skeleton

```markdown
# <Topic>

## Research frame

- **Target:** …
- **Decision supported:** …
- **Depth:** <every depth-raising factor that applied, or `none present`>
- **Task classification:** <one of the six>
- **Documentation status:** <one of the four>
- **Constraints:** …
- **Assumptions:** …

### Emphasis derivation
<present only when the classification is `other`; omit the heading entirely otherwise>

## Findings

### 1. <Claim as a sentence, not a subject heading>

**<posture> — <confidence, where findings differ>.** …

**Implication:** <present only where the finding has a direct consequence for the work; omit the line entirely otherwise>

<citation>

### 2. …

## Cross-reference and limitations

…

## Recommendation

…

### Implement now

1. …

### Verify first

1. …
```

## Section by section

### Research frame

Seven fields, all present. An empty one is stated as empty, not dropped.

- **Target** — the system, API, product or protocol researched.
- **Decision supported** — the decision this research exists to enable, in one sentence.
- **Depth** — every depth-raising factor in `task-emphasis.md` that applied, or `none present` when
  none did. More than one can apply; name each one rather than picking the strongest. There is no
  fixed depth scale to select a level from, so this field records the attribution and not a level.
  Depth and classification are two fields because they are two axes (`task-emphasis.md`).
- **Task classification** — exactly one of: `integration`, `implementation`, `bug investigation`,
  `capability exploration`, `migration or compatibility analysis`, `other, explicitly stated`. The
  field carries one value in all six cases; when the value is `other`, the `Emphasis derivation`
  block below is also required.
- **Documentation status** — exactly one of: `public official docs available`, `official docs
  partially gated`, `official docs fully gated`, `no official docs found`.
- **Constraints** — the task, environment or repository constraints the findings had to satisfy.
- **Assumptions** — every assumption the findings rest on, stated explicitly, and every missing
  variable identified when the task was framed that you filled with one. A missing variable you could
  not fill is not an assumption; it goes to `Cross-reference and limitations`. This field is
  load-bearing: an assumption left in your head is one a downstream skill will violate without
  knowing it did.

#### Emphasis derivation

Required when, and only when, the classification is `other, explicitly stated`. Omit the heading for
the other five.

`task-emphasis.md` requires four statements, and a fifth about what was done with them. All five
appear in this block:

1. The decision this research must enable. It is already in `Decision supported` above — point at it
   rather than restating it, so the decision has one home and cannot drift between two.
2. Who carries the consequence of getting it wrong, and what that consequence is.
3. The evidence that would be acceptable.
4. The stop criterion.
5. A statement that the emphasis for this research was derived from the four above.

### Findings

Numbered `###` subsections. One claim per finding, written as a sentence that asserts something,
not as a topic heading. `1. Authentication` is a topic. `1. Tokens issued by the v2 endpoint are
rejected by v1` is a finding.

Each finding opens with its evidence posture, and its confidence where findings in the document
differ in certainty. Then the substance, then the citation for that claim.

- **Posture** — exactly one per finding, one of: `documented`, `observed`, `reported`, `inferred`.
  `research-standards.md` defines what each one means and when it may be used.
- **Confidence** — one of `high`, `moderate` or `low`, carried only where findings in the document
  differ in certainty. A `moderate` or a `low` names, in the same line, the factor that lowered it.
- **`unresolved` does not appear here.** It is the fifth posture in `research-standards.md` and the
  one a finding cannot carry: a finding asserts something and `unresolved` is the absence of a
  conclusion. An `unresolved` claim is written in `Cross-reference and limitations` instead, carrying
  its posture, no confidence label, and what would settle it.

Source, assumption and conclusion stay separately identifiable inside each finding;
`research-standards.md` defines the three and why they are kept apart.

Where a finding has a direct consequence for the work, state it in that finding under an
**Implication** line. Do not save every consequence for the recommendation.

A code sketch is optional. Where one appears it sits inside the finding or the recommendation item it
serves, never as a section of its own. `../SKILL.md` sets when to include one and what form it takes.

### Cross-reference and limitations

Three comparisons are reported here, each one explicitly:

- source against source,
- source against the task requirement,
- source against the stated constraints.

For each, say what agrees, what contradicts, what evidence is missing, and — where it is inferable —
the likely reason for a mismatch. A contradiction whose cause you can name is a different work item
from one you cannot.

Where two sources disagree, both are named, and the section says which one the recommendation follows
and why.

Also recorded here:

- **Every `unresolved` claim**, with what would settle it and no confidence label.
- **Every missing variable from the frame that could not be filled**, and what it would change.
- **The triggering claim, when the research could not settle it** — the limitation, stated here, and
  what would settle it.

State what could not be established, why it is unsettled, and what would establish it — authenticated
documentation, tenant access, a probe, a hands-on test. Where something was not found, the section
says which of `not found` and `likely exists but inaccessible` applies (`../SKILL.md`, Core Operating
Rule 7).

### Recommendation

Answers one question: **what is the most defensible next action?**

Not a summary of the findings. The findings are above; this section is the decision that follows from
them.

Then split the work in two, because downstream skills sequence off this split:

- **Implement now** — work the evidence already supports. Ordered; each item actionable on its own.
- **Verify first** — work that must not start until something is confirmed. Each item names what is
  to be confirmed and how. A probe or a time-boxed proof also carries its success criterion, its
  failure criterion and its time box. These items are specified here and executed by the work
  downstream, never by the research run itself — `research-standards.md`, *Who runs a probe*.

An item that is neither belongs in the limitations section, not here.

## Citations

Attach a citation to the claim it supports, or group citations under the section they support.
Concise labels, not long raw URLs in the body — `NASA Product Integration` and not the full path.

A citation is attached to what it proves, or it is not in the document.

## What must not appear

Several of these are also operating rules in `../SKILL.md`. They are listed here because this file is
what the finished document is checked against, and a prohibition that is not in the contract cannot
be checked from it.

- A generic recap of the topic.
- A bibliography section.
- A dump of every source consulted.
- Restatement of the prompt or the task description.
- A list of options where the recommendation should be.
- Equal-sounding language across findings whose evidence is not equal.
- Language implying completeness where the evidence is partial or uncertain.
- A confidence label attached to an `unresolved` claim.
- A numeric probability the source does not support, or confidence and likelihood written as one
  thing.
- ODNI, IPCC or GRADE cited as the authority for a posture or a confidence label. They are the models
  the vocabulary is built on, not its source.
