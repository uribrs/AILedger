# Assumptions

Every entry carries a status. An OPEN assumption must be resolved to VALIDATED or REJECTED, with
recorded evidence, before the step that depends on it is reported complete.

---

## A1 — The 25-file `YamlCollector.Test` suite can be pointed at the new project with only namespace and reference edits — OPEN

This is the whole parity oracle, so it is the highest-value assumption in the task and the cheapest
one to break.

**Risk:** the suite reaches `internal` types through `InternalsVisibleTo`. A greenfield reshape that
splits, renames or re-scopes those types forces real test edits — and an edited oracle stops being an
oracle.

**Resolve by (S0, before any new code is written):** classify all 25 files by what each binds to —
public type, internal type, Shared type, engine type. Record the counts. If a material number bind to
internals whose shape will change, say so and let the operator decide whether the reshape or the
oracle gives way. Do not silently rewrite tests to fit new code.

## A2 — Runtime discovery of an assembly outside `Collectors/` works on the `IsCollector` stamp alone — OPEN, and NOT RESOLVABLE IN THIS TASK

**What is established:** each concern's `Directory.Build.props` injects the flag; `CLAUDE.md:90` says
this is how the host discovers adapters; a repo-wide grep over `*.cs`/`*.csproj`/`*.props`/`*.json`/
`*.yaml`/`*.yml` finds **zero** in-repo readers, so the consumer is the external hosting platform.

**What is NOT established:** that the platform keys only on the flag and not additionally on a path,
folder name, or assembly-name prefix. Nothing in this repo can answer that — the reader is not here.

**Consequence, and it must be stated in the report rather than glossed:** the `IsCollector=true`
requirement is *reasoned*, not *verified*. Discovery is exercised only by
`run-local-yaml-smoke.ps1`, which needs `pwsh` (absent on darwin) and the ISB repo (absent).

**Resolve by:** running that smoke on a box with both, in the follow-up ISB task. Until then no claim
may be made that the new assembly is discoverable.

## A3 — The engine package's public surface is sufficient for the ported host code — VALIDATED

Established earlier this session, by execution rather than reading: the host was compiled against
`Cymulate.Integration.Yaml.Engine 1.0.0-preview.1` after rewriting engine `using` lines in 5 files,
the build succeeded, and two live vendor collections ran through it (crowdstrike-falcon, defender-vm).

Scope of the claim, stated precisely: this validates the **engine package**, not the host code. See
F0 in `state.json`.

## A4 — `UnitTests/` mirrors the concern folders, so the new test project goes in `UnitTests/YamlAdapter/` — VALIDATED

Measured: `UnitTests/` contains `Collectors`, `Exclusions`, `Indicators`, `Shared`, plus
`Collectors.Tests.Infrastructure` and its own `Directory.Build.props`. Mirroring is the established
convention, so a peer concern gets a peer test folder.

## A5 — `ai/` is gitignored in this repo — VALIDATED

Measured: `git check-ignore -v` on the existing task dir exits 0; `git ls-files` under `ai/` returns
0 files. Artifact criteria are therefore "current at completion", never "inside a commit".

## A6 — No in-repo build or CI machinery hardcodes a `Collectors/` path that a peer folder would break — VALIDATED

Measured: the only `.github/workflows` files are `slack-notification.yaml` and
`slack-notification-open-prs.yaml`; neither mentions `Collectors`, `IsCollector`, or a csproj glob.
There is no Jenkinsfile, Dockerfile, or pipeline definition in the repo.

**What this does not cover:** the external packaging/deploy pipeline, which is not in this repo. That
belongs to the deferred ISB task, together with A2.

## A7 — The two PowerShell harnesses are usable as parity apparatus — REJECTED, on three independent grounds

1. No `pwsh` or `powershell` on this machine (`which` finds neither).
2. Both hardcode `$IsbRepo = 'C:\Users\YoniOren\source\integrations\IntegrationServiceBus'`, another
   developer's Windows path; the ISB repo is not on this machine.
3. **Both are already stale against the current layout.** Their `$bridgeProj` points at
   `Collectors\YamlCollector\Cymulate.Integration.Adapters.Collectors.YamlCollector\...csproj` — a
   nested directory that does not exist. The csproj sits one level up. This is a pre-existing defect,
   not one this task introduces.

**Consequence:** they are given a `YamlAdapter` code path (S4) but are **not executed**, and no
parity claim may rest on them. Ground 3 gets reported to the operator either way.

## A8 — A differential live vendor run is a meaningful parity check for the host code — REJECTED

`YamlLocalRunner` bypasses the adapter: `Program.cs:114` constructs `IntegrationEngine` directly and
`Program.cs:71` loads the definition itself. It never touches `YamlCollectorAdapter`, discovery, the
Shared orchestration pipeline, checkpointing, or the sinks — which is precisely the code this task
ports.

So a live run through it exercises the engine package and nothing this task changes. It is worth
running as a **regression guard on the package reference** and is labelled as such; it is not parity
evidence. The originally proposed old-vs-new live differential is dropped for measuring the wrong
thing, not for cost.

## A9 — Parity as the operator defined it is achievable within this task — REJECTED

The operator's kill criterion for `YamlCollector` is behavioral parity. The host-code behaviours most
at risk — runtime discovery, the registry/Mediator path, `PartialWaitRequired` scheduling, sink
publication — are exercised only by the ISB-host smoke and the dockerized E2E, neither runnable here
(A7), and ISB is explicitly out of scope by operator decision.

**Therefore this task can deliver parity evidence at the unit level and must stop short of the kill.**
The report must say so plainly rather than implying the criterion was met. This is a scoping fact
surfaced during contract design, not a failure discovered at the end.
