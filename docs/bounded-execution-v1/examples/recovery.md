# Installed task-12 interruption and recovery example

Captured from **2.0.178  from 142bab5** on 2026-09-29 using the installed executable,
a disposable Axonius snapshot and an offline scripted inventory provider. This is mechanical
evidence; client acceptance and semantic sufficiency remain unknown. No model episode ran.

| Inspection | Execution | Process | Authored result | Reconciliation required |
|---|---|---|---|---|
| [After hard host/process-group kill](interrupted-result.json) | unknown | unknown | partial | yes |
| [After explicit reconciliation](reconciled-result.json) | unknown | unknown | partial | no |
| [After bounded resume](resume-result.json) | succeeded | succeeded | reported_complete | no |

All three report `acceptance: not_assessed`. Reconciliation records operator attestation that
the provider stopped; it does not invent an observed exit for the interrupted invocation.
The resumed process's success is recorded separately. The same committed checkpoint survives
all three inspections, including its original timestamp, content and receipt:

- Execution: `67d3fb64a2bb2a96014a088a3e317586d22ca1199d7cb44244c6d2fbb84c069b`.
- Checkpoint submission: `ES_8902801f65914ff4b5f91f900cc425e2`.
- Checkpoint content SHA-256: `3587876adf74e7ac78dd0aa58407f57e1717cb02d4bf2515a7e536cb8ec37bb4`.
- Final submission: `ES_3837599168c241ceb35767da66243677`.
- Final content SHA-256: `07a1dbe8e565590d1f7f96a319e3d36f5599817d861f2e70fee11b8c12968a16`.

The final audit counted **36 versioned records and 16 explicit omissions** at package SHA-256
`8610ac4cd4cc57d440f288faa5734cb5e304d67577aaf83610822b4f87e2b2ac`.
Sequence 6 is the original checkpoint; sequence 9 is reconciliation; sequence 14 records the
checkpoint replay; sequence 17 is the one new final submission. There is no duplicate checkpoint.
The JSON includes admission identity/grants/input bytes, invocation environment, raw received
output, tool attempts, inventory and unavailable-usage records. Missing usage is not zero usage.

The [seven-case installed report](report.json) also covers cancellation, elapsed budget,
provider failure, authorized retrieval, sandbox denials and execution replay. Raw examples retain
their original temporary paths and identities; they are historical evidence, not reusable grants.
Follow the [trial instructions](../trial.md) to create a fresh fixture and interruption probe.
