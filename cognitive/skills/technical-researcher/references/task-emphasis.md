# Task Emphasis

Load this file at step 1 of `../SKILL.md`, after the task has been classified.

The classification you chose is not a label. It selects what evidence you go after and what evidence
lets you stop. This file says what each one selects.

## Two axes, kept apart

**The classification chooses the evidence target.** Which questions are the ones that matter, which
sources answer them, and what has to be true before the research is finished.

**The decision chooses the depth.** How far you go on that target is set by the consequence of the
decision the research supports, not by which of the six words you picked. If you cannot state that
decision in one sentence, the research has no stopping point and no depth can be set for it.

Do not let one axis set the other. A bug investigation on a cosmetic defect is shallow; an
integration behind a one-way migration is deep. Nothing in the classification tells you which.

### What raises depth

Raise depth when any of these is present, and say in the output which of them raised it. More than
one can apply; name each, rather than reporting only the strongest:

- The decision is expensive to reverse, or cannot be reversed.
- The target is new to this repository, or new in its own right.
- Official documentation is gated, absent, or older than the version in question.
- Sources disagree and the disagreement touches the decision.
- Being wrong is expensive — data loss, outage, rework across several components, a security
  boundary.

### What lowers depth

Lower depth when the decision is cheap to revisit, the target is already in use here and behaving as
documented, and one applicable official source settles the question. Stop there. Depth beyond the
decision's need is cost with no buyer.

## The five profiles

### integration

**Primary question.** Can the target and the environment it must sit in interact correctly and
safely?

**Emphasize.** Interfaces and data contracts. Authentication and authorization. Protocol and API
versions on both sides. Quotas, rate limits and other support boundaries. Error, retry, timeout and
idempotency behavior. Deployment topology. End-to-end behavior and failure behavior.

**Follow the boundary on both sides.** Research about the target alone is the standard failure of
this classification: the mismatch lives between two systems, and only one of them is being read.

**Stop when.** You have applicable interface documentation, and evidence for every boundary that
could invalidate the design. Where documentation cannot settle a high-risk interaction, a focused
probe is the evidence and more reading is not — so specify that probe as a `Verify first` item and
stop there. `research-standards.md`, *Who runs a probe*, says why you specify it rather than run it.

### implementation

**Primary question.** What exact steps and constraints realize a design that has already been
chosen?

**Emphasize.** Narrow immediately to the chosen product, the chosen version, the target environment
and the acceptance criteria. Prerequisites. Supported configuration. Required resources. Exact
procedures. The hooks that let the result be verified.

Alternatives are out of scope here. They matter only if the selected path turns out to be
unsupported or blocked, and that finding is itself the result.

**Stop when.** There is enough version-specific detail to implement without guessing, and a
traceable way to check the built result against the requirement.

### bug investigation

**Primary question.** What condition produces the observed behavior, and what evidence would show
that diagnosis to be wrong?

**Emphasize.** Actual against expected behavior. Reproducibility. Timeline and recent changes.
Telemetry, logs and environment. Competing hypotheses. Tests that separate them. Blast radius and
the conditions under which it recurs.

**Prefer the disconfirming test.** Another example of the symptom is not progress. A test that would
have failed if the hypothesis were true is progress.

**Stop when.** A reproduction exists or the observations stand in its place, one causal explanation
survives the competing ones, and the uncertainty that remains is stated rather than dropped. Where no
reproduction exists, specify one as a `Verify first` item rather than producing it here —
`research-standards.md`, *Who runs a probe*.

### capability exploration

**Primary question.** Is the technology fit for the stated use, against the stated decision
threshold?

**Emphasize.** Start broad: map the use cases and requirements onto supported capabilities,
constraints, maturity, alternatives, operational fit, security, cost and the skills it demands. Then
narrow hard to the few uncertainties that could actually change the decision.

Where documents cannot establish feasibility, the answer is a time-boxed proof — specified as a
`Verify first` item with its success criterion, its failure criterion and its time box, and run by
the work downstream. `research-standards.md`, *Who runs a probe*, says why you specify it rather than
run it. A proof without an exit criterion becomes an implementation.

**Stop when.** The evidence is sufficient for the named decision. Exhaustive product knowledge is
not the target and is a common way to overrun this classification.

### migration or compatibility analysis

**Primary question.** What changes, dependencies and transition risks stand between the current
state and the target state?

**Emphasize.** Inventory of current and target versions and configurations. Compatibility and
breaking changes. Data and schema transformation. Transitive and operational dependencies. Security
and compliance differences. Whether old and new can coexist, and for how long. Sequencing.
Representative testing. Cutover, rollback and validation.

**Research the dependency graph, not only the component being moved.** The component is usually the
part that was already understood.

**Stop when.** The current-state baseline is accurate, the critical dependencies and the target
assumptions are validated rather than assumed, and there is a staged path with a rollback.

## The residual classification

### other, explicitly stated

`other` carries no emphasis profile, and this file does not invent one. No external practice
establishes a default for a residual category, and a made-up default would read as settled when it
is not.

Instead, when you classify a task as `other`, state four things in the research output before
gathering evidence:

1. The decision this research must enable.
2. Who carries the consequence of getting it wrong, and what that consequence is.
3. The evidence that would be acceptable.
4. The stop criterion.

Then derive the emphasis from those four and say in the output that you derived it.

First check whether the work substantially matches one of the five named profiles. If it does,
reclassify. `other` is for work the five do not describe, not for work that was not examined.
