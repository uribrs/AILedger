# Task: FalconCollector internal refactor / cleanup

Pure internal cleanup of the FalconCollector
(`src/Cymulate.Integration.Adapters/Collectors/FalconCollector`). The branch
accumulated oversized classes, methods with 15–18 positional parameters, and
duplicated helper logic. Tidy it up without changing behavior or the
public/SDK-facing surface.

## What to achieve

- Bring classes under ~400 lines where reasonable (best-effort).
- Replace spread-out parameter farms with DTOs/records in the repo house style.
- Extract helper classes where they improve readability/SRP and kill duplication.
- Decide whether `Flows/SharedFlows` should hold more cross-flow logic, and home
  the genuinely cross-flow helpers there.

## What must NOT change

- Behavior (existing FalconCollector.Test suite is the contract).
- Public/SDK surface: `FalconCollector`'s `IAssetsCollectorAdapter` /
  `IFindingsCollectorAdapter` methods, `ProcessAsync`, `ResumeAsync`,
  `CanResumeFrom` keep their signatures and behavior.
- Internal call sites MAY change freely (this is how param farms get removed).

## Out of scope (leave alone to avoid churn)

`FalconCheckpointState.cs`, `FalconFindingsAssetsStage.cs`,
`FalconCollectorFlowRunner.cs`, `FalconResumeRunner.cs`.
