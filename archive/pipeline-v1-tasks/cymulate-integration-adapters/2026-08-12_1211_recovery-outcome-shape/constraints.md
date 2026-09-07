# Constraints

## Operator, standing

- Nothing in the adapter may cap a collection. Only cancellation or pod death ends a healthy run.
- The checkpoint moves forward and is never rewound. No path may silently restart a run that has
  already published.
- Nothing is pushed and nothing is merged without the operator's say. Release and deployment are the
  operator's decisions.
- Ask before expanding scope.
- Plain English in all output. No invented shorthand, no idioms. The operator is not a native English
  speaker.

## Working discipline

- Every change carries a mutation check: disable it, confirm the intended test goes red, restore.
  Report red counts.
- Do mutations in a throwaway clone, never in the working tree.
- Do NOT run full-solution test sweeps. Run targeted test projects only.
- Never run the DummyCollector or IsbLoadTestCollector test suites. They do not terminate.

## Design

- Checkpoint shapes stay per-collector. IntegrationServiceBus supplies the vocabulary and the
  mechanism; it does not impose a uniform state shape.
- The outcome vocabulary is defined in IntegrationInfra, because both IntegrationServiceBus and the
  adapters must speak it.
- A checkpoint is state zero at resume. No layer may infer, derive, or second-guess the position.
- Position has exactly one owner: the collector. The host stores what it is given.

## Repository and branch

- IntegrationInfra: branch `fix/recovery-outcome-shape`, base `7b2550ef2b31ca070200c3298fcc244263e3bb70`.
- IntegrationServiceBus: branch `fix/recovery-outcome-shape`, base `4a12c63b7431d46f1e1c8a3bc4103127d16cd3d3`.
- cymulate-integration-adapters: branch `fix/resume-live-state-authority`, base
  `88c642299aebd82b8c281f8b20c1df0cd460acc0`.
- Do not create new branches. Do not rebase existing ones.

## Ordering

- IntegrationInfra changes land first; IntegrationServiceBus and the adapters consume them.
- Infra changes require a package build and version bump before the consumers can compile against
  them. Plan for that chain rather than discovering it.
