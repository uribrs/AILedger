# Research Standards

Load this file at step 3 of `../SKILL.md`, while gathering evidence.

## Source order

The source order is already law in the skill body, at Core Operating Rules 2 to 4, and is not
repeated here. This file supplies the part the body does not: how the source you used turns into a
label the reader can act on.

Rule 2 tells you which source to reach for. The labels below do not tell the reader which tier you
reached: `documented` covers official and official-adjacent material alike, which Rule 2 orders as
two tiers. A posture says what kind of support exists, not where in the order it came from.

Which tier a claim rests on is the citation's job. Attach one that names the source precisely enough
to place it — `Kubernetes API reference, PodSpec` and `KubeCon talk, SIG-Node maintainer` are both
`documented` and the reader can tell them apart from the citation alone.

## Label every claim with two fields

Every finding carries an **evidence posture**. A finding carries a **confidence** as well only where
findings in the same document differ in certainty and the reader would otherwise flatten them.

Posture says what kind of support exists. Confidence says how much weight that support carries for
this particular decision. One word cannot do both jobs, and a document that tries produces findings
that look equally solid because they are all written in the same voice.

### Evidence posture — exactly one per claim

| Posture | Means |
|---|---|
| `documented` | A direct statement in official or official-adjacent material that applies to the version, edition and configuration in question. |
| `observed` | A result you obtained first-hand, within the bound in *Who runs a probe* below. Never a call made against the target's running service. |
| `reported` | A sourced third-party observation — an issue, a post, a changelog entry — that you did not reproduce. |
| `inferred` | A conclusion you drew from cited facts. The facts are someone else's; the step to the conclusion is yours. |
| `unresolved` | The evidence is missing, inaccessible, or materially conflicting, and no defensible conclusion was reached. |

Four rules on posture:

- **`documented` requires applicability, not just existence.** An official statement about a
  different version, edition or deployment mode is `reported` at best, and usually `inferred`.
- **`inferred` must show its step.** Name the cited facts and the reasoning that connects them to
  the claim. An inference the reader cannot retrace is a `documented` claim in costume.
- **Repetition does not promote.** Five community posts saying the same thing are one `reported`
  claim with corroboration noted, never a `documented` one.
- **`unresolved` is not a low confidence score.** It is the absence of a conclusion. Do not attach a
  confidence level to it; say what would settle it instead. It is not written as a finding either,
  because a finding asserts something: it goes in the limitations section of the output, as
  `output-contract.md` sets out.

### Who runs a probe

Probe ownership is settled here and nowhere else. The skill authorises no probe execution: web
research is its primary mechanism (Core Operating Rule 1), and its degraded path tells you to *state*
when hands-on testing, tenant access or authenticated documentation is required for a definitive
answer, rather than to perform it (`../SKILL.md`, Degrade gracefully).

So a probe or a time-boxed proof that would settle a question is **specified, not run**. It is
written as a `Verify first` item in the output, carrying its success criterion, its failure criterion
and its time box, and the work downstream executes it. Where `task-emphasis.md` says a probe is the
evidence for a classification, it means a probe specified on those terms, not one run by the research
itself.

This makes `observed` rare. Use it only for a result you saw yourself without acting on a live,
third-party or production system. The skill is silent on local, side-effect-free checks; that silence
is not authorisation to touch a running service, and a claim resting on such a call is outside what
this skill has been given.

### Confidence — `high`, `moderate` or `low`

Use it where findings in the document differ in certainty. Skip it where they do not; a confidence
label on every line carries no information.

Confidence is set by the evidence, not by how convinced you are:

- Applicability of the source to the exact version, edition and configuration.
- Quality of the source.
- Corroboration, or its absence.
- Recency against the version in question.
- The size of the inferential gap between the cited fact and the claim.

**Any `moderate` or `low` must name the factor that lowered it.** A bare `moderate` tells the reader
nothing they can act on. "Moderate — the vendor documents this for v4 and the target is v6" tells
them what to check.

### Keep confidence separate from likelihood

Confidence is about the basis of a judgment. Likelihood is about whether something will happen.
`High confidence that this is unlikely` is a coherent statement; collapsing the two is not.

Do not attach numeric probabilities to a technical claim unless the source supports a quantitative
estimate. An invented percentage reads as measurement.

## Separate source, assumption and judgment

Keep these three visible and distinct in every finding:

- **What the source said** — attributed, and at the posture above.
- **What you assumed** — the gaps you filled to make the source usable here.
- **What you concluded** — your judgment, marked as yours.

A finding that blends the three cannot be checked by the reader, and cannot be repaired later when
one of the three turns out to be wrong.

## Explain material uncertainty

Where uncertainty could change the recommendation, say what is uncertain, why, and what would
resolve it. A gap named this way is a work item for the next run. A gap left silent reads as
completeness.

## Provenance of this vocabulary

The five postures and the three confidence levels are **local convention for this repository**. They
are not an implementation of any external standard, and no cross-domain standard exists to
implement: the frameworks below use materially different, domain-specific scales that are not
interchangeable.

What is established outside this repository, and what this file is modelled on:

- **ODNI ICD 203** — analytic standards requiring source-quality description, explained uncertainty,
  and separation of underlying information from assumptions and judgments; and keeping confidence in
  a judgment distinct from the likelihood of an event.
- **IPCC uncertainty guidance** — confidence derived from evidence and agreement, held distinct from
  probabilistic likelihood.
- **GRADE** — certainty in a body of evidence assessed against defined criteria rather than asserted.

The principle these three share is what this file adopts: a small defined vocabulary, calibrated and
used consistently; certainty tied to the quality and quantity of evidence; material uncertainty
explained; sourced fact kept apart from assumption and judgment. The words themselves are ours.

Do not cite ODNI, IPCC or GRADE in a research document as the authority for a posture label. They
are the models for the approach, not the source of the scale.
