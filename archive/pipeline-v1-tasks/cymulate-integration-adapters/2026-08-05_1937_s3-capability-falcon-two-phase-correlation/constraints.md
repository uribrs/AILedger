# Constraints

## Layering (settled — do not redesign)

- Direction is `ISB concrete AWS impl → Client contract → Infra guarded façade → collectors`.
- `IntegrationInfra` MUST NOT reference any `AWSSDK.*` package. It has zero AWS deps today and that
  boundary is load-bearing; `Emission/` is cloud-agnostic and the read side must stay so.
- Collectors MUST NOT construct S3 clients or reference `AWSSDK.*` directly. They reach storage only
  through the Infra façade.
- Concrete S3 lives only in
  `IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.AWS`.

## Naming

- The new contract MUST NOT be named `IAdapterCapability`. That name is taken in
  `Cymulate.Integration.Client/Contracts/IAdapterCapability.cs` and means something else — a marker
  interface for capability discovery (`ICollectorCapability`, `IIndicatorCapability`, …).
- Name it as a functional sibling of `IAdapterDataPublisher`; put request/result models beside
  `PublishRequests.cs` in a parallel file.

## Reuse (do not reimplement)

- `Services/S3ClientFactory.cs`, `Services/S3CredentialProvider.cs` — client construction and creds.
- `S3UrlParser.ParseObject(url, defaultRegion)` → `S3ObjectUrlInfo(Bucket, Region, Key)` — the
  `storageUrl` decomposition already exists.
- `Emission/MemoryPressureOptions` — reuse; do not introduce a second pressure model.
- `IQueryCacheObjectStore` (Application.Query) — prior art for lazy `IAsyncEnumerable` listing and
  batch delete. Its listing is deliberately lazy because a bucket can hold more keys than a pass
  should hold in memory. Same reasoning applies.
- Register in the AWS project's existing `DependencyInjection.cs`.

## Falcon behaviour

- The `aid:[...]` Spotlight filter MUST STAY. It manufactures asset affinity, which is what makes
  each response self-complete and allows per-page emission. Removing it would require buffering
  ~250M findings.
- Phase 1 (Discover) MUST contain no per-page enrichment.
- Phase 2 MUST iterate an immutable key list, never a live scroll.
- Output page keys MUST be derived deterministically from the spool page, never from a running
  counter. This is what makes re-processing overwrite instead of duplicate.
- Deletion is garbage collection, never the cursor. Correctness must not depend on a delete having
  happened.
- `AidBatchSize` must come down from 250. Evidence: ~5.6 MB of findings per asset at p50; each
  250-aid batch produced a 1.2–2.2 GiB output object. Record the chosen value's reasoning.

## Envelope compatibility (byte-level)

Preserve every emission invariant established by
`tasks/cymulate-integration-adapters/2026-07-02_1705_falcon-correlated-findings-redesign`:

- Record schema `{aid, chunk, isLastChunk, findingsInChunk, host, findings[]}`.
- Chunk cap ~2000 findings per record.
- Strip `apps`, `suppression_info`, `host_info` from findings at emission.
- Sort `remediation.entities` canonically at emission — this fixed parser `entities[0]`
  nondeterminism and removing it silently regresses the parser.
- Zero-finding hosts still emit one envelope.

## Known trap (cannot be caught by unit tests)

- `AWSSDK.SecurityToken` must be an **explicit** `PackageReference` — it is not a transitive
  dependency of `AWSSDK.S3`.
- `AwsAlcAssemblyResolver` must be registered **before** the S3 client is constructed.
- The STS reflective load fails only inside the adapter's isolated `AssemblyLoadContext` under EKS
  IRSA. A passing unit test proves nothing about this path.

## Process

- Scope + pseudo code for all four repos FIRST, with an operator review gate before implementation.
- One worker per repo; verifier AND code-reviewer for every repo.
- A coordinator reports status periodically and reconciles each worker's assumptions against the
  others'.
- Do not commit or push without explicit instruction.
- Flag token/context pressure rather than silently degrading.

## Documentation

- One centralized design document is the single source of truth.
- Each of the four repos gets a short reference to it, not a duplicated copy.
- The mutable-field invariant must be recorded centrally:
  **never place per-page enrichment inside a scroll that is filtered and sorted on a mutable field.**
  It also exists in Defender VM's `api/machines` (`$filter=lastSeen ge`), safe today only because
  that stage is fast and standalone.
