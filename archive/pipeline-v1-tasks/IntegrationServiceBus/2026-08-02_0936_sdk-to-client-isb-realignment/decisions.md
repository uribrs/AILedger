# Decisions

## Carried in from prior art (do not re-litigate)

- Physical absorption = one Infra repo, two packages, never merge Client's types into the Infra assembly.
  (`IntegrationInfra/ai/active/2026-07-08_1720_sdk-physical-absorption`)
- `Cymulate.Integration.Sdk.Samples` stays in ISB — its only consumer is ISB's `API.UnitTests`.
- The SDK concept is retired in Infra: PackageId, assembly, folder, and all namespaces are `.Client`,
  with **no** compatibility shim and **no** type forwards, declared BREAKING. (commit `0607e90`)
- Both Infra packages share one `Version` from the repo-root `Directory.Build.props`; the client's 3.x
  lineage is abandoned because the PackageId that owned it no longer exists. (commit `bce7b15`)
- This task **is** step S4 of the absorption sequence ("the host ProjectReference→PackageReference swap,
  coordinated with ISB owners"), which the absorption task deliberately left out of its own scope.

## Taken for this task

- Task directory lives in **ISB** (`ai/active/`), not Infra, because the realignment lands in ISB. Any
  Infra-side change implied by deferred decision D-A is recorded here and executed as a separate,
  explicitly authorised piece of work in that repo.
- Phase 1 is read-only and Phase 2 is gated on operator review — per the operator's "we move slow".
- The drift audit is **bidirectional**. An ISB-only or Client-only file is a finding in its own right,
  not noise to be normalised away.
- The reported drift count is whatever the evidence says. The operator's "~22" is a cross-check, not a
  target to hit.
- Closing the SiemRules 3.3.0 gap in Client is in scope for the Phase 1 **change map** as a required
  change. Implementing it is not in Phase 1's scope.
- Deferred candidate (a)(i) is presented as a **reversal of a documented BREAKING decision**, with that
  framing made explicit, so the operator is not offered it as though it were free.

## Deferred to the operator — do not decide

- **D-A** — backward-compat for collector DLLs already in S3: restore `Sdk` namespaces inside Client to
  enable a shim / rebuild-and-republish every collector / dual-shape loader in the host.
- **D-B** — how ISB consumes Client: CodeArtifact `PackageReference` (requires publishing Client, which
  has no publish history and is at pre-release `1.0.0-preview.4`) vs cross-repo `ProjectReference`
  (`Dockerfile.WebApi` COPYs the in-tree Sdk path, so the Docker build context is the constraint).
