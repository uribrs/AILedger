# Decisions

## Operator decisions — settled, not to be re-litigated

- **D1 — `YamlCollector` dies only once behavioral parity is satisfied, not before.** The operator
  will keep merging `dev` in so drift stays observable.
- **D2 — Greenfield means rewrite the structure, port the load-bearing files close to verbatim.**
  Production scars and their comments are carried over, not rediscovered.
- **D3 — ISB is out of scope.** Operator's words: "that comes later." No dispatch, routing, or
  platform registration work.
- **D4 — Business logic stays as-is for now, gated at the YamlAdapter entrypoint and outside the
  engine.** The engine package is untouched; the `assets`/`findings` parameterization is deferred.

## Design decisions

- **D5 — `YamlAdapter/` is a peer concern, not a subfolder of `Collectors/`.** Three capability types
  exist (17 indicator, 7 collector, 1 exclusion); a YAML-defined integration is not inherently a
  collector.
- **D6 — The concern's props stamp `IsCollector=true`, not a new flag.** The external host discovers
  adapters by that metadata (`CLAUDE.md:90`) and no in-repo code reads it, so a novel flag would be
  invisible. Folder identity and capability identity are deliberately decoupled.
- **D7 — Only the collector capability is built now; the folder is shaped so exclusions and indicators
  can join later.** Rule 6 of the operator's principles — a seam is earned by a second implementation,
  not anticipated.
- **D8 — The topic vocabulary stays declared on the host side, where it already lives.**
  `YamlCollectorAdapter.cs:74` (`SupportedTopics`), `YamlCollectorCheckpointState.cs:24,28`
  (`AssetsStage`/`FindingsStage`), `WorkflowPublishSink.cs:35` (`IsFindings`),
  `YamlOperationRunner.cs:272-273` (`Counters["assets"]`/`["findings"]`). The engine's own hardcoding
  is a redundant second declaration removed in a later task.
  - Residual constraint, recorded and **not fixed here:** the engine's `StageCountsValidator` rejects
    any counter name other than `assets`/`findings` at definition-load time, so the host cannot
    introduce a third topic until the engine is parameterized. It does not need to — production has
    two.

## Process decisions

- **D9 — Of the 9 uncommitted trial files: keep `nuget.config` and `Directory.Packages.props`; revert
  every change under `Collectors/YamlCollector/`.** The parity oracle must be what production actually
  runs — the vendored engine via `ProjectReference`. Modifying the baseline before measuring against it
  destroys the measurement.
- **D10 — The `Tools/.../YamlLocalRunner` `--vendor` change is kept.** It replaces a hardcoded
  `credsDoc.RootElement.GetProperty("crowdstrike")`, is independent of this task's subject, and is
  useful regardless of outcome.
- **D11 — The parity gate is the 25-file `YamlCollector.Test` suite, measured, plus a green solution
  build.** These are the only host-code oracles executable on this machine.
- **D12 — The live vendor differential is dropped.** Not for cost: `YamlLocalRunner` bypasses the
  adapter entirely (`Program.cs:114` constructs `IntegrationEngine` directly), so it measures the
  engine package rather than the ported host code. A run may be kept as a package-reference regression
  guard, labelled as such and never as parity evidence.
- **D13 — The PowerShell harnesses get a `YamlAdapter` code path but are not executed**, and no claim
  rests on them. Their pre-existing stale `$bridgeProj` path is reported to the operator either way.
- **D14 — The report must state plainly that the kill criterion is not met by this task**, naming
  runtime discovery and the ISB-host path as the unverified surface and the smoke run as what would
  verify it. Scoping honestly up front beats discovering it at the end.
- **D15 — Drift control while both trees live:** `YamlCollector` is bugfix-only, and the parity gate is
  re-run after each `dev` merge. Two live trees is exactly how production got ahead of the engine on
  `OAuth2ClientCredentialsAuthenticator` — two capabilities added a day after extraction, found months
  later only by an audit.
- **D16 — This repo's conventions govern inside this repo.** The engine repo's numeric rules are not
  imported; divergences are noted rather than silently resolved either way.
