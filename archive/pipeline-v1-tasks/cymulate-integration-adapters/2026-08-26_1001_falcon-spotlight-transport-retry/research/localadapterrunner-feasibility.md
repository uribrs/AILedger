# LocalAdapterRunner — feasibility map

**Question:** can the LocalAdapterRunner exercise the Falcon Spotlight in-flow transport retry (mid-body
stream death → re-issue → exhaustion → `FalconTransportFailureException` → unbudgeted 5-minute deferred
recovery) end to end, against its mock vendor, through the real collector, real emission and real
resilience wiring — and at what cost?

**Date:** 2026-08-26. **Method:** read-only static read of the runner, the Falcon collector, the working-tree
diff for the retry work, and `IntegrationInfra`'s transport classifier. No builds, no test runs, no
executions.

---

## Verdict

**Feasible with caveats — roughly a day, ~270–410 lines, fully offline, no CrowdStrike credentials.**

Everything downstream of the fault is already real and already wired: the runner drives Falcon through the
genuine `ProcessAsync` → `AdapterBusEntrypointRunner` path with the genuine
`FalconResilienceStrategyFactory.Create(...)` attached, a deferred `PartialResult` is *observable and then
actually resumed* by `LocalScheduledWaitRunner`, and Phase 2 staging works locally against a filesystem
`IAdapterObjectStore`.

The two gaps are both in the mock, not the runner:

1. the existing Falcon mock serves the **assets** flow only — its Spotlight route returns a hardcoded empty
   page, so there is no findings traversal to break;
2. it writes every response as a fully-buffered byte array followed by `Response.Close()`, so it cannot
   currently drop mid-body.

The mid-body seam itself is available — `HttpListenerResponse` exposes `Abort()`, and the mock already owns
`OutputStream` directly — so this is a mock-authoring job (a Discover/Spotlight fixture generator plus a
drop-on-Nth-request switch), not an architecture problem.

**Single empirical risk:** whether the client-side exception text from an aborted `HttpListener` response
matches the substrate's marker list. Could not be verified statically. Cheap to check first.

---

## Runner shape

- `Tools/…/LocalAdapterRunner/Program.cs:16` — entry point; single `Main`, one big try/catch.
- `Program.cs:88` — `CliOptions.Parse(args)`; interactive picker at `:97` when `args.Length == 0`.
- `Cli/CliOptions.cs` — all flag parsing; **the file a new flag would touch**. Exemplar flag
  `FalconPartialPageSimulation` is declared at `:35`, parsed at `:261`, projected at `:384`. Help text lives
  separately at `Cli/HelpPrinter.cs:32`.
- `Bootstrap/LocalAdapterRunnerConfiguration.cs:55-84` — configuration sources.

  > **Gotcha worth knowing.** `AddEnvironmentVariables()` is added at `:79`, *before* the hardcoded
  > project-local file at `:82`. So `appsettings.local.json` **beats** environment variables for any key it
  > defines. `Collectors__FalconCollector__Credentials__apiEndpoint=…` will **not** override the value in
  > that file — you must edit the file. It is gitignored
  > (`src/Cymulate.Integration.Adapters/.gitignore:490`), so edits are zero-diff.

- `Collectors/CollectorConfigurationLoader.cs:44-57` — how a config key is born:
  `Collectors:<name>:Credentials:*`, then `:Settings:*`, then flat keys, merged into the
  `Dictionary<string,string>` handed to `SetConfiguration`.

  **CLI flags do not become collector config keys.** The only key injected from options is `flowName`
  (`Program.cs:400`). So `spotlightTransportMaxRetries` needs no new flag — it goes in
  `Collectors:FalconCollector:Settings`.

- `Collectors/CollectorRegistry.cs:34-40` — Falcon descriptor: `ExecutionMode.ProcessAsync`, factory
  `new FalconCollector(logger, context)`.
- `Program.cs:410-448` — the ProcessAsync branch. Builds the `PlatformEvent` from `RunMetadata`
  (`Collectors/CollectorPlatformEventFactory.cs:56`, which carries `storageUrl`), then hands off to
  `LocalScheduledWaitRunner`.

---

## Falcon mock + existing simulations

`--falcon-recovery-simulation` **no longer exists** — CLAUDE.md is stale on this point. Only
`--falcon-partial-page-simulation` remains, plus the unrelated `--checkpoint-drift-simulation`,
`--isb-completion-simulation` and `--simulate-pod-death`.

### The exemplar simulation, in full shape

`FalconSimulation/FalconPartialPageSimulationRunner.cs`:

- `:30` — starts the mock server.
- `:32-39` — builds a **private** `ServiceCollection`: http factory, no-op encryption,
  `LocalFileAdapterDataPublisher`, throttling options, config. Note it registers **no
  `IAdapterObjectStore`** — fine for assets, fatal for findings.
- `:42-53` — constructs the real `FalconCollector` and calls `SetConfiguration` with
  `apiEndpoint = server.BaseUrl` and `flowName = AssetsFlow`. This is exactly the seam a findings variant
  would reuse.
- `:58` — `collector.ProcessAsync(BuildPlatformEvent())`. Called **directly**, bypassing
  `LocalScheduledWaitRunner`, so a deferral here prints but never resumes.
- `:104-123` — writes `summary.json` with `success` / `message` / `assetPageRequests`.
- Wired in from `Program.cs:293-305`, before collector selection.

### The mock server

`FalconSimulation/LocalFalconPartialPageMockServer.cs`:

- `:10` — `HttpListener` on a free loopback port (`:188`). `http://`, not `https://` — the TLS layer is
  absent, but the client-visible failure shape (premature EOF on the body stream) is reproducible without
  it.
- `:60-78` — routing is a flat if-chain on `AbsolutePath`:
  - `/oauth2/token` → static token
  - `/discover/combined/hosts/v1` → `HandleHostsAsync`
  - `/spotlight/combined/vulnerabilities/v1` → **hardcoded** `{"meta":{"pagination":{"total":0}},"resources":[]}`
  - everything else → 404
- `:95-99` — fault injection today is **status-code only**: `HandleHostsAsync` returns a 403 body once the
  cursor reaches `after-1`. That is the entire mechanism; there is no stream-level fault vocabulary.
- `:87-91` — special-cases `limit=1`, which is what satisfies `FalconAccessProber`'s probes
  (`Processing/Validation/FalconAccessProber.cs:39`, `:151-157`).

---

## Realness of the path

| concern | answer | citation |
|---|---|---|
| real entry point | **Yes.** Falcon is `ExecutionMode.ProcessAsync`; the runner calls the collector's own `ProcessAsync`, which builds the `AdapterBusEntrypointDefinition` and delegates to `AdapterBusEntrypointRunner.RunAsync`. No flow is invoked directly. | `Collectors/CollectorRegistry.cs:37`; `Program.cs:420`, `:451`; `Collectors/FalconCollector/FalconCollector.cs:324` |
| real resilience strategy | **Yes.** `ResilienceStrategy = FalconResilienceStrategyFactory.Create(_logger)` is part of the definition, so `FalconTransportFailureException` hits the new `transport-failure` branch and returns `RequestDeferredRecovery(…, UseRecoveryBudget: false)`. | `FalconCollector.cs:321`; `Processing/Resilience/FalconResilienceStrategyFactory.cs:91-108` |
| deferral observability | **Fully observable — and better than observable: it resumes.** `LocalScheduledWaitRunner` matches `AdapterResultStatus.PartialWaitRequired`, logs `RequestedResumeAfter` / `AppliedDelay` / `WaitReason` / `Message`, **compresses any wait > 1 minute to 1 minute**, then does `InitializeAsync → ResumeAsync → ShutdownAsync` in a loop. A 5-minute unbudgeted deferral becomes a 60s local wait and a real resume leg. | `Collectors/LocalScheduledWaitRunner.cs:44`, `:76-95`, `:108-116` |
| real emission | **Yes.** The collector runs the substrate's real `ResultsBatchPublisher` / NDJSON path; only the terminal sink is local. `SimpleAdapterExecutionContext.PublishAsync` emulates a successful publish so byte/row counts stay meaningful. | `Publishing/LocalFileAdapterDataPublisher.cs:9-12`; `Execution/SimpleAdapterExecutionContext.cs:47-58` |
| output location | Local directory, `logs/<yyyyMMdd-HHmmss>/`, with batch folders, `wire-bodies/`, `checkpoint.json` and the run log side by side. Never real S3 in any committed build. | `Program.cs:121-128`, `:270`, `:347` |
| staging store availability | **Available — Phase 2 works locally.** When no S3 store is wired, the runner registers `LocalFileAdapterObjectStore` as both `IAdapterObjectStore` and `IAdapterObjectPruner` under `logs/<ts>/_object-store`. It implements the real contract (keyed writes, `StatAsync` absence-as-null, prefix listing, prefix delete). **One blocker to clear first:** if `RunMetadata:storageUrl` starts with `s3://`, the runner *refuses to run* rather than silently substituting the local store — and the current Falcon section is `s3://cybi-data/Uri-Tests/falcon-with-policies/001`. Change it to a non-`s3://` value. Empty also fails, from the other side: `CreateStagingArea` throws when `storageUrl` is blank. | `Program.cs:243-251`; `Publishing/LocalFileAdapterObjectStore.cs:36`; `Program.cs:230-241` + `Publishing/S3ObjectStoreWiring.cs:128-130`; `appsettings.local.json:34`; `Collectors/FalconCollector/Flows/Findings/FalconFindingsFlow.cs:641-647` |

---

## Mid-body drop seam

**The plumbing allows it; the current code does not do it.**

- Both write paths buffer the entire body first, then close cleanly. `WriteJsonAsync` at
  `LocalFalconPartialPageMockServer.cs:138-146` does `Encoding.UTF8.GetBytes(json)` → set `ContentLength64`
  → one `OutputStream.WriteAsync` → `Response.Close()`. `WriteErrorAsync:148-162` is the same shape. There
  is no partial write, no flush-then-fault, no `Abort`.
- **But** the handler holds the live `HttpListenerContext` and writes through `Response.OutputStream`
  itself — it is not returning a string to a framework. So a mid-body drop is a local change inside one new
  method: declare `ContentLength64` as the *full* length, write the first N bytes, flush, then call
  `context.Response.Abort()`, which tears the connection down without sending the remainder or a
  terminating chunk.
- That the client is genuinely mid-stream (not reading a buffered body) is confirmed:
  `FalconSpotlightBatchScroller.cs:104-129` calls `GetStreamAsync(...)`, gets an `AdapterStreamResponse`, and
  hands `streamed.ContentStream` to `TopLevelJsonArrayStreamReader`. This is precisely the post-Polly region
  the pump's own remarks describe.

### The one unverified link

`IsRetryableTransportFailure` matches only `TimeoutException`, transient `SocketException` codes, or an
`HttpRequestException`/`IOException` whose **message** contains one of ten literal markers —
`"unexpected eof"`, `"response ended prematurely"`, `"connection reset"`,
`"forcibly closed by the remote host"`, `"connection aborted"`, `"broken pipe"`, `"stream was aborted"`,
`"transport stream"`, etc.

Source: `/Users/user/Dev/IntegrationInfra/src/IntegrationInfra/Kernel/Transport/HttpTransportFailureClassifier.cs:11-23`,
`:50-79`.

On .NET 8 a truncated `Content-Length` body surfaces as `HttpIOException: The response ended prematurely.`
(an `IOException`), which matches — and a raw `Abort()` typically yields `SocketError.ConnectionReset`,
which also matches. Reasonably confident, but this is message-matching against a runtime string that was
not executed, so treat it as the **first thing the new mock must prove**. If it misses, the mock switches to
closing the socket harder, or the marker list gains an entry.

---

## Cheapest credible test

Two routes. **Route B is recommended.**

### Route A — new self-contained simulation (`--falcon-eof-simulation`)

Copy the partial-page pair.

| file | change | ~lines |
|---|---|---|
| `FalconSimulation/LocalFalconMidBodyDropMockServer.cs` | new — token, Discover paging over a synthetic host set, Spotlight responding per aid batch, and a "drop the k-th Spotlight response after N bytes" switch | ~250 |
| `FalconSimulation/FalconMidBodyDropSimulationRunner.cs` | new — copy of the exemplar, but must additionally register `LocalFileAdapterObjectStore` as `IAdapterObjectStore` + `IAdapterObjectPruner`, set `flowName = FindingsFlow`, set a non-`s3://` `storageUrl`, and route the result through `LocalScheduledWaitRunner` rather than calling `ProcessAsync` bare as the exemplar does at `FalconPartialPageSimulationRunner.cs:58` | ~150 |
| `Cli/CliOptions.cs`, `Cli/HelpPrinter.cs`, `Program.cs` | flag wiring | ~12 |

**Total ~410 lines.**

### Route B — mock-only, drive it through the main path

Add just the mock server as a serve-and-block mode (`--falcon-eof-mock-server`, fixed port, ~260 lines +
~8 lines of wiring). Then run the *real* command in a second terminal, with `appsettings.local.json` edited:

```
Collectors:FalconCollector:Credentials:apiEndpoint  → http://127.0.0.1:<port>
Collectors:FalconCollector:RunMetadata:storageUrl   → local://falcon-eof/001
Collectors:FalconCollector:Settings:spotlightTransportMaxRetries → 1
Collectors:FalconCollector:Settings:aidBatchSize    → small
```

That file is gitignored, so the edits cost nothing in the diff — and the env-var route is closed anyway by
the ordering at `LocalAdapterRunnerConfiguration.cs:79-82`.

**Total ~270 lines**, and you get `LocalScheduledWaitRunner`, the ISB echo report, `checkpoint.json`
persistence, `--dump-payload` and every other flag for free rather than re-deriving them in a sim runner.

### Either way

- **Offline.** The mock issues its own OAuth token (`LocalFalconPartialPageMockServer.cs:60-64`). No
  CrowdStrike credentials, no network.
- **Runtime.**
  - Retry-only (drop once, succeed on re-issue, default `spotlightTransportMaxRetries=3`, base 2s):
    **seconds**.
  - Retry-then-defer (drop every time for one batch, `spotlightTransportMaxRetries=1`): the in-flow ladder
    is ~2s, then the deferral compressed to the 60s local cap, then a full resume leg. **~1.5–2 minutes**
    end to end.
- **What it would actually prove:**
  - the `"lost its response stream; re-issuing it"` warning with `Attempt` / `DelaySeconds`
  - the `"exhausted its in-flow transport retries"` error
  - `Stats.TransportRetries` on the successful batch
  - the strategy's `falcon-transport-failure` decision log
  - `LocalScheduledWaitRunner`'s `WaitReason` line with `RequestedResumeAfter=00:05:00`
  - a resume that continues from the frozen-key-list coordinate rather than re-spooling Phase 1

---

## Blockers

None that are hard. Three that must be handled, all cheap:

1. `appsettings.local.json:34` — Falcon `storageUrl` is `s3://…`, so the runner refuses
   (`Program.cs:243-251`). Must become non-`s3://` **and** non-empty.
2. The existing mock's Spotlight route is a hardcoded empty page
   (`LocalFalconPartialPageMockServer.cs:72-76`) — there is no findings traversal to interrupt. A real
   Discover→Spotlight fixture is the bulk of the new work.
3. `EnablePreventionPolicyEnrichment` defaults to **true**
   (`Processing/Configuration/FalconCollectorConfiguration.cs:179`), so the mock also needs
   `/devices/entities/devices/v2` and `/policy/entities/prevention/v1`
   (`Processing/Urls/FalconUrls.cs:49`, `:68`) — or set the setting false to cut two routes.

**Not a blocker but worth stating:** the local run is plain HTTP, not TLS, so it reproduces the
*stream-truncation* failure, not a TLS-layer EOF specifically. Given the retry predicate keys off exception
type and message markers rather than anything TLS-specific, that distinction does not affect what the test
proves — but the result should not be written up as "TLS EOF reproduced".

---

## Certainty

- **High** — runner shape, entry-point realness, resilience wiring, deferral observability, local object
  store availability, the `s3://` refusal, the env-var-loses-to-JSON ordering. All read directly from
  source.
- **Medium-high** — that `HttpListenerResponse.Abort()` after a partial write produces a client exception
  matching the substrate's marker list. Not executed; inferred from .NET 8 behaviour and the marker
  strings.
- **Estimate only** — line counts and the ~1 day figure.
