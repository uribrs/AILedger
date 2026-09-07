# Task: Realign ISB onto Cymulate.Integration.Client

`Cymulate.Integration.Sdk` is leaving this repo. Its new home is
`/Users/user/Dev/IntegrationInfra/src/Cymulate.Integration.Client`, where it cohabits with the
`IntegrationInfra` project — an infrastructure layer that uses Client as the entrypoint into ISB.
PackageId, assembly name, folder and every namespace renamed `.Sdk` → `.Client`.

The rename breaks the mechanism by which ISB identifies adapters. That break is the point of this task.

## Phase 1 — inventory and feasibility (ACTIVE, read-only)

1. **Bidirectional drift** between ISB's SDK (`origin/dev`, 73 `.cs`) and Infra's Client (105 `.cs`),
   namespace-normalized so the rename neither masks nor manufactures drift. Reconcile the true count
   against the operator's expected ~22 and report honestly which side each change sits on.
2. **Classify** every drifted file: additive / behavior change / wire-or-serialization contract /
   cross-repo contract. Flag cross-service contracts explicitly.
3. **Resolve the 33 Client-only files** (`Query/BaseQueryAdapter`, 6 `Query/Contracts`,
   11 `Query/Models/Plan`, `Query/Models/Runtime/RawResponse`, `Query/Persistence`, 13 `Query/Pipeline`).
   The absorption copied 105 `.cs` from ISB on 2026-07-08; `origin/dev` now has 73 and none of these.
   Establish where those types live in ISB today and whether Client's copies are stale, dead, or
   conflicting.
4. **Verify the concept**: does "Client as the entrypoint into ISB" actually hold? Ground the verdict in
   Infra's `Conducting/*` source and ISB's `AdapterLoader` / `AdapterActivator` / `AdapterManager` /
   `AdapterLoadContext` / `AdapterRegistry` / `Infrastructure.Core/Decorators/*`. Name every gap.
5. **ISB change map**, file by file: delete the SDK, repoint every reference, repair adapter
   identification. Separate compiler-verified changes from silent runtime-behaviour changes and state
   plainly what is not compiler-verified.
6. **Decision evidence** for the two deferred decisions. Supply evidence; take no decision.

## Phase 2 — staged change plan (BLOCKED until operator reviews Phase 1)

Align ISB first, then define how this is verified before deployment.

## The failure this task exists to prevent

`Infrastructure.Core/Services/AdapterLoadContext.cs:123` hardcodes
`name.Equals("Cymulate.Integration.Sdk")` in `IsSharedDependency()`. Collector DLLs already in S3 were
compiled against assembly `Cymulate.Integration.Sdk`. When the host ships `Cymulate.Integration.Client`
instead, the adapter's request for the old assembly stops matching the shared list, the
`AssemblyDependencyResolver` loads the adapter's own bundled copy inside the isolated collectible ALC,
and the host's `IIntegrationAdapter` is then a different type identity than the adapter's. The
activation cast fails and ISB silently stops identifying adapters — no compile error, no deploy-time
exception.
