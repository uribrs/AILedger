# Task

Build an S3 access capability that any adapter can reach through guarded Infra mechanics, and use it
to convert the Falcon findings collector to a two-phase design driven by a frozen key list.

Cross-repo: four repos, all branched to `feat/s3-capability-falcon-two-phase`.

## Why

The Falcon findings collector duplicates assets. The Discover scroll both **filters**
(`last_seen_timestamp:>=`) and **sorts** (`sort=last_seen_timestamp.asc`) on the same **mutable**
field, and slow per-page Spotlight enrichment sits inside that scroll. Cursor lag accumulates;
sensor check-ins bump `last_seen`, relocating already-read hosts *ahead* of the cursor; they are
served again, once per check-in, for the run's duration.

Measured on prod-eu-west client `6a4bc78d35b3233a6da1a815`, run `wr_f61759a7`, 35 shards / 46.1 GiB:

- 1,600 of 5,479 assets replayed — **29.2%**
- 1,350 twice, 242 three times, 8 four times
- ~48% of finding rows re-emitted
- 100% of replays had their final copy's `last_seen` inside the collection window
- ignition once cursor lag fell under ~90 minutes

The assets flow is unaffected only because it has no enrichment in its loop and finishes in ~8s.

## What gets built

1. **Contract** — an object-store contract in `Cymulate.Integration.Client/Contracts/`, sibling to
   `IAdapterDataPublisher`. CRUD plus lazy prefix listing and batch delete.
2. **Guarded façade** — a new concern folder in `IntegrationInfra` (sibling to `Emission/`) owning
   read-side limits. No AWS dependency.
3. **Concrete implementation** — S3 in `Cymulate.IntegrationServiceBus.Infrastructure.AWS`, beside
   the existing `S3AdapterDataPublisher`.
4. **Falcon two-phase collector** — Phase 1 Discover to a middle-ground folder inside the run's
   `storageUrl`; Phase 2 iterate the frozen key list, Spotlight per aid-batch, correlate, publish
   final envelopes to `storageUrl`; then delete completed pages and the middle-ground folder.
5. **Parser hardening** — replace order-dependent `dropDuplicates(["id"])` with a deterministic
   freshest-wins window over `updated_timestamp`.
6. **Centralized documentation** — one design document, referenced from all four repos.

## Scope boundary

The capability must serve four real collectors with three correlation topologies. Falcon is first,
but the design must not preclude the others:

| topology | collectors | needs |
|---|---|---|
| passthrough | Qualys | nothing |
| grouped_probe | Tenable.io, Defender VM | budgeted entity store, remove-on-use, residue drain |
| key_driven_probe | **Falcon** | frozen ordered key list, batch-addressable |

Defender VM additionally needs side-input loading and durable per-key prior state (delta feed).

Topologies are C# strategy classes with typed options. A YAML mechanism is explicitly **deferred**
until this is in production.

## Out of scope

- Porting the fix to AgentService's `FalconFindingsCollector` (same defect, separate task).
- A YAML-declared correlation descriptor.
- Deciding out-of-process correlation; this build defaults to in-process.
