# Code Review — Falcon policy-plane restructure

**Scope reviewed:** `git diff b14d6d64` over `src/Cymulate.Integration.Adapters/Collectors/FalconCollector/`
plus the untracked `Flows/Policies/FalconPolicyEdge.cs`, the Falcon unit-test project, and
`FalconDocs/CollectorDocs/06`/`07`.

**Change type:** feature logic + persistence format + vendor IO, inside a collector that owns
checkpointing and resume. **Risk: High** — persisted artifact formats, retry/recovery interaction,
network fan-out, and a published customer-facing payload contract. Reviewed at high-risk depth.

**Standards held to:** repo `CLAUDE.md`; `ai/skills/collector-flow-patterns`, `collector-recovery`,
`collector-execution-and-recovery`, `collector-tests`, `collector-shape-and-layers`, `collector-review`.
`IntegrationInfra` (`src/IntegrationInfra/Ingestion/GuardedObjectStore.cs`,
`Kernel/Exceptions/DataPipelineException.cs`, `Conversation/AdapterHttpClient.cs`) read directly where the
code makes claims about it.

**Build:** `dotnet build` of the collector project — clean, 0 warnings.
**Tests: RED.** `Failed: 6, Passed: 346, Skipped: 2, Total: 354` (14m 7s) on the working tree as reviewed.
See B0 — I found this after writing the rest, and it is the first thing to deal with.

---

## Blocker

### B0. The branch does not pass its own test suite: six failures, five of them caused by a production default flip that left the tests it invalidates untouched, and one on the recovery-budget backstop

```
Failed Cymulate.…FalconCollectorConfigurationBuilderTests.Build_WhenPreventionPolicyEnrichmentIsOmitted_DefaultsToEnabled
Failed Cymulate.…FalconCorrelatedFindingsTests.Policy_EnrichedHost_AppearsInEveryChunkOfAMultiChunkHost
Failed Cymulate.…FalconCorrelatedFindingsTests.Policy_ZeroFindingHost_StillCarriesTheEnvelope
Failed Cymulate.…FalconCorrelatedFindingsTests.Policy_AssignmentFailure_SkipsSpotlightAndPublishesNothing
Failed Cymulate.…FalconCorrelatedFindingsTests.Policy_OneAssignmentLookupPerStagedPage_CarryingThatPagesAids
Failed Cymulate.…FalconTwoPhaseFindingsTests.ResumedLegThatPublishesNothing_LeavesTheCoordinateUnchanged_SoTheNoProgressBudgetAccumulates
```

**B0a — the shipped default of `EnablePreventionPolicyEnrichment` is now specified twice, contradictorily.**

`Processing/Configuration/FalconCollectorConfiguration.cs:179` flips the default `true → false`.
`FalconCollectorConfigurationBuilderTests.cs:214` asserts it is `true`, in a test *named*
`Build_WhenPreventionPolicyEnrichmentIsOmitted_DefaultsToEnabled`:

```
Expected cfg!.EnablePreventionPolicyEnrichment to be True, but found False.
```

Neither `FalconCollectorConfigurationBuilderTests.cs` nor `FalconCorrelatedFindingsTests.cs` appears in
`git diff b14d6d64 --stat` at all — they were not touched. So the repo now states the default is enabled (in
the test that exists to pin it) and disabled (in the code), and nothing reconciles them. A reader cannot tell
which is intended, and the intent matters: with enrichment off by default the entire policy plane this change
builds is inert in production and every host publishes
`device_policies.collection_status: "disabled"`.

The four `FalconCorrelatedFindingsTests.Policy_*` failures are the same cause. None of them sets the flag, so
enrichment no longer runs and they assert behaviour that no longer happens:

```
Policy_OneAssignmentLookupPerStagedPage…: Expected deviceBodies to be equal to {"{"ids":["h1","h2","h3"]}"}, but found empty collection.
Policy_EnrichedHost_AppearsInEveryChunk…: Expected deviceCalls to be 1 …, but found 0.
Policy_ZeroFindingHost_StillCarriesTheEnvelope: … target element has type 'Null'   (no envelope on the host)
Policy_AssignmentFailure_SkipsSpotlightAndPublishesNothing: Expected result.Success to be False, but found True.
```

Note which invariants those four were protecting: the **call budget** (one join per bounded unit), the
**envelope-always-present** rule, and **essential-assignment failure publishes nothing**. Those are three of
`collector-tests`' hard rules and three of the load-bearing properties of this very change. They are now
unasserted, silently, on the default configuration.

`collector-tests` has a rule pointing straight at this: *"Enabling enrichment by default changes every
existing flow fixture… A flow test failing on an unrouted URL is a harness gap, never a reason to weaken
production behaviour."* The reverse move needs the same treatment — the fixtures must state the mode they are
about.

**Fix.** Decide the default, once, and make the repo say it once:

- If off-by-default is intended (the new test helpers say it is —
  `FalconCollectorTests.cs:1776-1780` and `FalconConcurrencyHarness.cs:629-631` both describe `false` as
  "the shipped default"), then rename and invert
  `Build_WhenPreventionPolicyEnrichmentIsOmitted_DefaultsToEnabled` → `…DefaultsToDisabled`, and add
  `["enablePreventionPolicyEnrichment"] = "true"` to the four `Policy_*` fixtures the same way
  `AssetsPolicyEvent`/`FindingsPolicyEvent` already do. Keep one test that asserts the disabled default
  end-to-end, so the shipped configuration is covered rather than only the opt-in one.
- If it is not intended, revert `FalconCollectorConfiguration.cs:179`.

Either way this is mechanical. What is not acceptable is merging with the two statements in disagreement.

**B0b — `ResumedLegThatPublishesNothing_LeavesTheCoordinateUnchanged_SoTheNoProgressBudgetAccumulates` is
red, and the observed value is the budget-wiped shape.**

```
Expected persisted["_resilience.recovery.lastProgressCoordinate"] to be "2:200:200" …
  but "" has a length of 0
```

This one is not fixture drift from the flag: the test seeds `SeededPlane.PolicyDisabledTerminal` explicitly,
and its own remarks name what it guards — *"the gate that should have stopped the prod-eu 2026-08-09 run at
about deferral three. It reached fifteen with `consecutiveNoProgressCount` still reading 1, so it could never
fail fast."* The earlier assertions in the test pass (the leg fails, nothing is published); only the persisted
coordinate is wrong, and it is **empty**, not the `5:200:200` the test was written to catch.

Empty is significant. In IntegrationInfra, `AdapterRecoveryBudget.Write`
(`FaultGovernance/Logic/AdapterRecoveryBudget.cs:110-111`) writes `progressCoordinate ?? string.Empty`, and
`Clear`/`ClearEpisode` set every budget key to `string.Empty`. So the two candidate mechanisms are:

1. the deferral reached `Write` with a **null** coordinate, or
2. the episode/whole budget was **cleared** on a leg that published nothing.

Both reinstate the incident this test exists to prevent: an empty stored coordinate cannot equal the next
leg's non-empty one, so `consecutiveNoProgressCount` resets to 1 on every deferral and the run defers without
bound. Mechanism (2) would be the more serious of the two.

I am not claiming which it is — I did not prove it, and I deliberately did not mutate a shared working tree to
find out. Two cheap steps that settle it:

- Relax the assertion order (or dump every captured snapshot) so
  `_resilience.recovery.consecutiveNoProgressCount` is visible. If it is also `""`, the budget was cleared and
  this is mechanism (2).
- Establish regression vs pre-existing without touching the tree:
  `git worktree add /tmp/falcon-base b14d6d64 && dotnet test … --filter "FullyQualifiedName~ResumedLegThatPublishesNothing"`.
  The test name does not appear among the diff's `+`/`-` lines, so the test body is unchanged — but
  `FalconTwoPhaseFindingsTests.cs` changed by ~1,179 lines including the shared `SeedFrozenGeneration` /
  `SeededPlane` / `Checkpoint` helpers this test consumes, so the fixture is a live possibility and worth
  ruling out first.

This is also the mechanism M4 depends on, so the two should be looked at together.

---

### B1. The bisection budget is sized for exactly one bad AID; with two or more it abandons most of a page, and those hosts are then published as *definitively having no Prevention policy*

`Flows/Policies/FalconDevicePolicyClient.cs:83` (`MaxAdditionalRequests = 32`), `:150-204`
(`ResolveBatchAsync`), `:408-424` (`BisectionBudget`).

**Problem.** `TryReserveSplit` charges 2 per split, so the whole search is capped at 16 splits / 33 requests.
The doc comment sizes that against *one* culprit: "isolating a single culprit costs two calls per halving, so
32 funds 16 halvings". That arithmetic is right and the bound is correctly enforced — but it only holds when
every non-culprit sibling succeeds and therefore does not split. With *k* unattributable culprits in *n* ids
the number of internal nodes that must split is ≈ `k·log2(n/k)`: for `k=10` in a 1,000-AID staged page that
is ~66 splits against a budget of 16.

The recursion is depth-first, left-biased (`:198-201`), so the budget is spent drilling the left spine to a
singleton and then partially unwinding. Everything in the un-probed subtrees is returned by
`FalconAssignmentOutcome.AllRejected(aids)` (`:180`) as `RequestFailed` — i.e. the bulk of the page. The
vendor would have answered for those hosts in any clean subset; they are never asked.

**Impact.** Two compounding effects:

1. **Data.** A `RequestFailed` AID reaches emission with no edge, and
   `FalconStagedPolicyComposer.AttachAsync` (`Flows/Findings/TwoPhase/FalconFrozenKeyList.cs:117-122`) maps it
   to `FalconPolicyEdge.None()` → `collection_status: "complete"`, `prevention: null`. That is a positive
   claim that the host has no Prevention policy, emitted for most of a page, on evidence that the vendor
   refused to give. See M3, which is the same wound from the other side.
2. **Cost.** In the single-culprit case the search costs ~20 extra `POST /devices/entities/devices/v2` calls
   for that page, every run, for as long as the stale AID lives in Discover. That is the *good* case.

The premise is realistic, not hypothetical: Discover retains AIDs the devices API no longer resolves — the
change's own `FalconAccessProber` comment (`Processing/Validation/FalconAccessProber.cs`, the 404 catch) says
exactly that, and cites a live tenant. A long-lived tenant will have more than one per 1,000-host page.

**Fix (algorithmic, not a bigger budget).** Charge budget for *refusals*, not for splits, and converge by
removal rather than by exhaustive halving:

1. On an unattributable refusal, binary-search for **one** culprit (the current recursion, but only down the
   refusing side).
2. Record it as `VendorRejected`, remove it from the request set, and re-issue the **whole remaining set** as
   one batch.
3. Repeat until the batch succeeds or the refusal budget is spent.

That is `O(k·log n)` refusing calls and it terminates on the real answer instead of on the budget. Keep an
absolute call ceiling as the runaway guard for the "refuses every subset" case the current comment describes —
that case is what a budget is genuinely for.

Also worth adding while you are in here: a run-scoped set of AIDs already isolated as `VendorRejected`, held
on `FalconPolicyEnricher`, excluded from later batches. It costs one `HashSet` and removes the repeat cost
across pages.

**Refactor or patch:** local patch to `ResolveBatchAsync` + `BisectionBudget`. No structural change needed.

---

## Major

### M2. `Compose` converts a staged `resolved` edge into `definition_status: "not_found"` whenever the definition lookup misses — a definite negative claim built on missing evidence

`Flows/Policies/FalconDevicePoliciesEnvelope.cs:103-113`; triggered by
`Flows/Findings/FalconFindingsFlow.cs:724-731`.

**Problem.** `Compose` short-circuits only on `Unavailable`. For any other staged status it discards the
staged status and re-derives from the lookup:

```csharp
return definition is null
    ? ForAssignment(assignment, PreventionDefinitionStatus.NotFound, definition: null)
    : ForAssignment(assignment, PreventionDefinitionStatus.Resolved, ...);
```

`NotFound` is defined at `FalconDevicePoliciesEnvelope.cs:11` as "a successful 2xx definition response omitted
the requested ID", and it yields `collection_status: "complete"`. So a staged edge that this run *resolved*,
whose definition is simply not reachable in this process, is published as "the vendor confirms this policy
definition does not exist" — with a `complete` collection status asserting the answer is whole.

The flow walks into this deliberately. `FalconFindingsFlow.cs:724-731`, when the policy generation's
`definitions.json` is gone, logs "Resolved edges will emit definition_status=not_found for this leg" and calls
it "a visible downgrade in the emitted record rather than a wrong claim". It is a wrong claim: `not_found` and
`unavailable` are two different published values and the envelope already has the right one. The same path
fires on a partial rehydrate (`FalconPreventionPolicyCache.Rehydrate` skips blank ids and pre-existing keys)
and on a policy id the sweep resolved but the persisted artifact lost.

This is the case `collector-flow-patterns` names as a hard rule: *"Cache only outcomes that stay true for the
run — never an outage — so a transient failure cannot harden into a permanent 'not found'."* The cache obeys
it; the composer re-introduces the same defect one layer later, from the absence of a cache entry rather than
from a stored one.

**Fix.** Make the staged status authoritative when it is stronger than the lookup. In `Compose`:

```csharp
if (definition is null)
{
    // A staged `resolved` we can no longer back up is an outage in our own definition plane, not a
    // vendor statement that the policy does not exist.
    return ForAssignment(
        assignment,
        edge.DefinitionStatus == PreventionDefinitionStatus.Resolved
            ? PreventionDefinitionStatus.Unavailable
            : PreventionDefinitionStatus.NotFound,
        definition: null);
}
```

That makes the envelope `partial`/`unavailable` for the lost-definitions leg, which is the truth, and leaves
every other path unchanged. It also retires the "silently downgrading a staged claim at emission" worry that
`FalconFindingsFlow.cs:713-717` currently only comments about.

**Refactor or patch:** four-line patch, plus deleting the "visible downgrade" justification at
`FalconFindingsFlow.cs:726-729`. Add a test: staged `resolved` edge + cold cache → `unavailable`/`partial`.

---

### M3. A declined or unaccounted-for AID is emitted as `collection_status: "complete"`, `prevention: null` — indistinguishable from a confirmed "this host has no policy"

`Flows/Policies/FalconPolicyEnricher.cs:114-140`;
`Flows/Findings/TwoPhase/FalconFrozenKeyList.cs:107-137`.

**Problem.** Both flows funnel a rejection into `FalconDevicePoliciesEnvelope.Empty()`, whose XML doc
explicitly claims the successful cases only ("unmanaged host, an AID the device endpoint did not return,
`resources: null`/`[]`…"). The code knows this is a gap and says so in two long comments: *"This envelope has
no vocabulary for 'declined' — its states are complete / partial / disabled"* and *"Distinguishing a decline
from an absence IN THE EMITTED RECORD needs an envelope state that does not exist yet."*

`collector-flow-patterns` states the opposite as a hard rule: *"An enrichment envelope is always present and
self-describing (schema version + collection status…), so a missing relationship, a disabled capability, and a
failed lookup are distinguishable downstream."* A failed lookup is currently not distinguishable. Doc 06 §6.5
rule 8 tells consumers to "branch on `collection_status` before trusting `prevention: null`" — which is
precisely the branch that cannot work here.

**Impact.** A downstream consumer reading `complete` + `prevention: null` correctly concludes the host is
unprotected. Under B1 that is emitted for most of a page. The only durable evidence to the contrary is a log
line and the manifest's `rejected`/`unresolved` counters — neither of which reaches the parser.

**Fix.** The state that "does not exist yet" is one enum member and one wire string, and
`schema_version` (already `1`, already published) exists to absorb it:

```csharp
/// <summary>The assignment lookup could not answer for this host. Not an absence.</summary>
public static JsonObject Declined() => BuildEnvelope(StatusPartial, prevention: null);
```

Attach it from both call sites when the AID is in `outcome.Rejected`, and add a sentence to doc 06 §6.2/§6.5
so a consumer treats `partial` + `prevention: null` as "unknown", not "none". `FalconStagedPolicyPage` already
persists the rejection per AID (`FalconStagedHostPage.cs:263-274`), so the findings path has the fact it needs
at emission — it is currently thrown away at `FalconFrozenKeyList.cs:112-119`.

If you consciously accept shipping without this, say so in doc 06 rather than only in code comments — the
consumer is the party who cannot see the comment.

**Refactor or patch:** local patch across three files + one doc paragraph.

---

### M4. The policy stage is one non-resumable unit: every leg re-reads every staged host page and re-issues every device-entity call, and it registers no progress against the deferred-recovery budget

`Flows/Findings/TwoPhase/FalconHostSpooler.cs:386-467` (`RunPolicyStageAsync`).

**Problem.** The loop at `:396` walks all `manifest.PageKeys`, and the manifest — the only durable record that
the stage ran — is rewritten once, at `:459`, after the last page. There is no per-page skip: a stage that
dies at page 95 of 100 re-does pages 0-94 on the next leg, including a full `ReadHostPageAsync` per page
(`:401`, a multi-MB object) and a fresh `POST /devices/entities/devices/v2` per page (`:411`). The edge
objects are keyed positionally so re-writing them is *correct*; it is the whole cost that is re-paid.

Worse, the stage advances no substrate counter. `SpoolAsync`'s own doc notes "Phase 1 advances no page, so it
must report activity", and the policy stage likewise only calls `progressContext.ReportHeartbeat` (`:430`).
`collector-recovery` — Recovery Budget — is explicit that the budget bounds *consecutive no-progress*
deferrals against the coordinate `CurrentPage:ProcessedItems:ProcessedFindings`, and that "a flow that never
advances them cannot signal progress, so every defer looks stuck". So a policy stage that fails late,
repeatedly (the 5xx-on-page-90 shape the spooler's own `MaxConsecutiveServerErrorReanchors` comment describes
for Discover), cannot converge: each leg restarts the stage from zero, no progress is registered, and the
budget terminates the run. This is now on the critical path *before* Spotlight
(`FalconFindingsFlow.cs:216-224` gates on it), so it blocks the expensive half of the run.

**Fix.** Skip a page whose edge object already exists, and recover that page's counters from it rather than
from the vendor:

```csharp
FalconAssignmentOutcome? existing = await _staging
    .TryReadPolicyEdgePageAsync(manifest.GenerationId, pageIndex, cancellationToken)
    .ConfigureAwait(false);

if (existing is not null)
{
    counters.Record(existing);
    continue;   // no host-page read, no vendor call
}
```

This is safe against the mode-flip case: enrichment-off takes the early return at `:363` and writes no edge
objects at all, so an existing `edges_NNNNNN.json` can only have come from an enrichment-on stage under this
same generation. It removes both the host-page read and the vendor call for already-covered pages, which is
what makes a retried stage converge.

If you want the stage to also register *forward* progress for the budget, the honest signal is the count of
covered pages; but the skip above is the part that matters, because it makes each leg strictly cheaper than
the last.

**Refactor or patch:** ~10-line patch in `RunPolicyStageAsync`. Add a test: kill the stage mid-loop, resume,
assert the device endpoint is called only for the uncovered pages.

---

### M5. Every contract-violation exception this change adds is unmapped in `FalconFlowExceptionClassifier`, which the same file documents as meaning three blind retries then terminal

`Processing/FalconFlowExceptionClassifier.cs:46-131` (`_ => null` at `:129`).

**Problem.** The change adds a substantial number of `InvalidOperationException` throw sites on the flow path:

- `FalconStagedHostPage.cs:153,161` (host line unusable), `:312,403` (edge/definitions line unusable),
  `:458,468` (unrecognised wire value), `FalconStagedJson.ParseObject:431`;
- `FalconFrozenKeyList.cs:100-105` (missing edge page), `:230-235` (missing host page);
- `FalconFindingsFlow.cs:216-224` (Phase-2 gate), `:678-691` (policy-contract mismatch);
- `FalconStagingArea.cs:384-391` (manifest over the control-artifact ceiling);
- `FalconDevicePolicyClient.cs:583,783,792,811,828,834,886` (vendor contract breakage).

None matches an arm. `DataPipelineException` derives from `InvalidOperationException`, not the reverse, so the
arm at `:127` does not catch these. The file's own remarks state the consequence: *"An unclassified exception
falls to `UnknownFlowRetryPolicy`, which retries three times on fixed 30/60/120-second delays… So an unmapped
permanent fault is retried… Every exception type a flow can actually raise therefore needs an arm here."*
`FalconCollector.cs:318-319` and `Processing/Resilience/FalconResilienceStrategyFactory.cs:18-33` confirm the
wiring.

Every one of these is deterministic and non-retryable. They will each burn ~3.5 minutes of the execution
window and then publish terminal — the precise failure the object-store arms were added in this same file to
remove ("burned four attempts over 3.5 minutes on a permission fault no retry could fix").

**Fix.** Give the collector's own contract violations a type and an arm. Cheapest version that respects the
repo rule against message matching: introduce one exception in `Exceptions/`, e.g.

```csharp
internal sealed class FalconStagedPlaneCorruptException(string message) : Exception(message);
```

throw it from the staged codecs, the composer, the Phase-2 gate and the contract-mismatch guard, and add:

```csharp
FalconStagedPlaneCorruptException corrupt =>
    new FlowExceptionHandling(corrupt.Message, "FALCON_STAGED_PLANE_CORRUPT", ErrorSeverity.Error, IsRetryable: false),
```

Do the same (separate code) for the device client's vendor-contract throws — `FALCON_VENDOR_CONTRACT` — since
those are a different operational story. Note the existing rule from `collector-execution-and-recovery`: do
**not** chain the original as `InnerException` when the point is to route past
`RetryableTransportFailurePolicy`; `FalconDevicePolicyClient.cs:580-584` already gets this right and says why.

**Refactor or patch:** one new exception type + two classifier arms + mechanical throw-site changes.

---

## Minor

### m6. `SweepDefinitionsAsync`'s short-page terminator counts post-deduplication rows, contradicting its own comment

`Flows/Policies/FalconPreventionPolicyClient.cs:97,119-127`.

```csharp
int before = definitions.Count;
...  // CaptureSweptDefinition appends only when seen.Add(id) succeeds
int received = definitions.Count - before;
if (received < (int)SweepPageSize) { return ...; }
```

The comment asserts *"this counts what the vendor RETURNED, not what survived deduplication: treating a page
of duplicates as short would stop the traversal early on the very response shape that proves the order is
unstable."* `definitions` is only appended through `seen.Add` (`:148-153`), so `received` **is** post-dedup and
a page of all-duplicates yields `received == 0` → early return with `SinglePage: pages == 1` → `false`. The
one case the comment set out to handle is the one that misbehaves. Practically harmless today (a tenant has a
handful of policies and never reaches page 2), but the code and the reasoning disagree, which is how this bites
later.

**Fix:** count what the reader yielded. Have `CaptureSweptDefinition` return an `int` for rows seen, or take a
`ref int received`, and compare that against `SweepPageSize`.

While there: `CaptureSweptDefinition` allocates a `Dictionary<string, JsonObject>` per resource purely to reuse
`CaptureDefinition` (`:145-146`). Split `CaptureDefinition` into a `TryRead(JsonElement) → (string id,
JsonObject def)` helper and drop the per-element dictionary.

---

### m7. Two staged codecs silently coerce a non-object payload to `null`, where every sibling codec throws

`Flows/Findings/TwoPhase/FalconStagedHostPage.cs:324` and `:408`.

```csharp
var assignment = record[AssignmentProperty] as JsonObject;   // non-object → null → "no policy"
var definition = record[DefinitionProperty] as JsonObject;   // non-object → null → confirmed not-found
```

`FalconStagedHostPage.DecodeLine:159-163` throws for exactly this shape on the `host` property, and both
codecs' XML docs promise "a malformed line aborts the read… composing anyway would emit a policy claim nothing
supports". These two lines do the opposite, and both failure modes land on a *definite* published claim
(`complete`/`prevention: null`, and `definition_status: not_found`). Distinguish `absent` from
`present-but-wrong-kind`:

```csharp
JsonNode? raw = record[AssignmentProperty];
JsonObject? assignment = raw switch
{
    null => null,
    JsonObject obj => obj,
    _ => throw new InvalidOperationException($"… line {lineNumber} carries a non-object assignment ({raw.GetValueKind()}); the staged policy plane is unusable.")
};
```

---

### m8. Forward/backward compatibility of the two persisted formats is asymmetric, and one direction silently discards a completed freeze

- `FalconPhase1Manifest.cs:233` uses `JsonStringEnumConverter` for `FalconStageState`, and `TryParse:289-300`
  swallows `JsonException` → `null`. An unrecognised state name (a future build's manifest read by an older
  one during a rolling deploy) therefore makes the **entire manifest read as absent**, and
  `FalconStagingArea.TryReadManifestAsync:400-406` reports "present but unusable; treating Phase 1 as
  incomplete" → full Discover re-spool. The comment at `:230-232` argues names beat ordinals because "an
  inserted enum member cannot then silently reinterpret an already-written manifest" — true, but an unknown
  *name* is a hard parse failure, which is the more expensive outcome.
- `FalconStagedJson.ParseDefinitionStatus`/`ParseRejection` (`FalconStagedHostPage.cs:452-471`) go the other
  way and throw on an unknown wire value.

Both are defensible in isolation; together they mean the same class of version skew costs a silent 97k-host
re-scroll in one artifact and a hard exception in the other. Pick one rule and state it. My preference: keep
the throw for the edge page (it is a claim about data), and add an explicit `ledgerVersion` check to the
manifest read that distinguishes "newer than this build understands" (log loudly, treat as absent, re-spool)
from "unparseable" — so the operator sees which happened. `TryParse` should also catch
`NotSupportedException`, which `System.Text.Json` can raise independently of `JsonException`.

---

### m9. `IsPreLedger` declares a 6.3.3 manifest's enrichment mode "unknowable" although the artifact records it — and the same-release default flip makes every in-flight run across the deploy fail terminally

*(See also B0a: the default flip is also what breaks five tests.)*

`FalconPhase1Manifest.cs:340-353`; `FalconFindingsFlow.cs:666-691`;
`Processing/Configuration/FalconCollectorConfiguration.cs:179`.

The predecessor manifest carried `policyEnvelopeSchemaVersion` and **`policyEnrichmentEnabled`**
(`git show b14d6d64:…/FalconPhase1Manifest.cs:65-66`). The new record drops both properties, so
`System.Text.Json` ignores them and `IsPreLedger` reports the mode as "recorded no enrichment mode at all,
so the mode its already-published hosts were enriched under is unknowable"
(`FalconFindingsFlow.cs:682-684`). The value is sitting in the artifact.

`EnablePreventionPolicyEnrichment` also flips `true → false` in this change
(`FalconCollectorConfiguration.cs:179`), so a 6.3.3 generation enriched under the old default meets a 6.3.4
leg configured off — a genuine mismatch either way, which is why the *behaviour* is the same whether or not
the field is read. But two things follow:

1. **The error message is factually wrong** for a 6.3.3 artifact, and it is the message an operator gets while
   a customer run has just failed. Read `policyEnrichmentEnabled` back (keep the property, `[JsonPropertyName]`
   only, no other use) and say "resolved with enrichment ON" instead of "unknowable".
2. **Deploy note:** every in-flight findings run that has published an object or checkpointed a Phase-2
   position fails terminally on the first leg after this ships
   (`LegacyManifest_WithPublishedWork_FailsInsteadOfMixingPolicyContracts` asserts exactly this, so it is a
   conscious and tested decision — I am flagging the operational consequence, not the design). Combined with
   M5 that failure burns 3 blind retries first. Consider draining in-flight findings runs, or shipping the
   default flip in a separate release from the ledger gate.

---

### m10. `unavailableDefinitions` double-counts across pages

`Flows/Findings/TwoPhase/FalconHostSpooler.cs:419,453`.

`HydrateDefinitionsAsync` deliberately never caches an outage, so a policy id that fails on page 1 is
re-requested on page 2 and, if it fails again, counted again. The total lands in the definitions ledger
entry's `unresolved` (`:453`) and in the completion log (`:467`). Accumulate a `HashSet<string>` of
unavailable ids instead of summing per-page counts.

---

### m11. The separate policy generation folder never delivers the benefit it is justified by

`Flows/Findings/TwoPhase/FalconStagingPaths.cs:84-114`;
`FalconHostSpooler.cs:378-390`.

`FalconStagingPaths.PolicyFolder`'s remarks justify the separate plane by: *"a re-spool discards the host
generation wholesale, and taking the definitions with it would re-pay the vendor sweep for data that did not
change."* But `RunPolicyStageAsync` unconditionally mints a fresh `pgen_` (`:378`), immediately prunes every
other one (`:382-384`), and then re-sweeps (`:388-390`). Nothing ever rehydrates from a *prior* policy
generation before sweeping. So on the re-spool path the sweep is re-paid anyway, and the plane's separation
buys nothing — while costing a second key space, a second prune arm
(`FalconStagingArea.DeleteAbandonedPolicyGenerationsAsync`), `TryGetPolicyGenerationId`, and a manifest field.

Two coherent resolutions; pick one rather than leaving the justification unearned:

- **Keep the plane and earn it:** before sweeping, list `PolicyAreaPrefix`, take the lexically newest
  `pgen_`, `TryReadPolicyDefinitionsAsync` it, `RehydrateDefinitions`, and skip the sweep when it came back
  whole. Then prune. (Watch the staleness question: a definition can change between runs, so this wants a
  freshness bound.)
- **Or drop it:** put `definitions.json` under the host generation, delete the second prefix, the second prune
  arm and the `policyGenerationId` manifest field. Per the operator's cost-awareness rule this is the smaller
  system, and the sweep the doc itself calls "one small vendor sweep" is cheap enough that positional-key
  invalidation is not a real cost.

---

### m12. `OpenEncodedStreamAsync` removes `GuardedObjectStore`'s in-memory write guard without removing the memory

`Flows/Findings/TwoPhase/FalconStagedHostPage.cs:60-93,240-284,351-381`;
`FalconStagingArea.cs:184-191`.

The remarks say handing over a stream "removes that ceiling as a failure mode rather than betting the page
stays under it". Accurate about the ceiling; the page is still fully materialised in a `MemoryStream`, whose
doubling growth transiently holds ~2× the payload on top of the `JsonObject` graph it was serialised from.
`GuardedObjectStore.WriteAsync` (IntegrationInfra `Ingestion/GuardedObjectStore.cs:277-289`) refuses an
oversized in-memory payload precisely so "a caller cannot quietly buffer a large artifact on the way out" —
this path is that caller, routing around the check while doing the thing it checks for.

I am not asking for a redesign: `ObjectWriteRequest.FromStream` wants a readable stream and a `MemoryStream`
is the pragmatic answer. Just make the comment say what was achieved (the *store's* refusal is avoided; the
buffering is not), so nobody later reads it as "staged pages no longer buffer". If the peak matters, the real
fix is an incremental writer that streams host lines to the store as they are encoded.

Related, lower value: `WriteLineAsync` issues two `await`ed writes per host against a `MemoryStream`
(`:95-99`). Synchronous `Write` on a `MemoryStream` is free of the state-machine cost; not worth changing
unless this shows up in a profile.

---

### m13. Duplicated `<summary>` block; the first one documents a different method

`Flows/Policies/FalconPolicyEnricher.cs:436-468`.

Two consecutive `<summary>` elements sit above `IsRecoverableSweepFailure`. The first (`:436-445`) documents
`IsTransientDefinitionOutage`, which then has no doc at all (`:472`). No build warning today because
`GenerateDocumentationFile` is off, but the file is dense enough that a reader will attribute the wrong
contract to the wrong predicate. Move the first block down to `:472`.

---

### m14. Two small inconsistencies in `FalconStagingArea`

`Flows/Findings/TwoPhase/FalconStagingArea.cs:493-554` vs `:574-637`.

- `DeleteAbandonedGenerationsAsync` still carries its own `_pruner is null` check, batching and
  `catch (Exception ex) when (ex is not OperationCanceledException)`, duplicating the `PruneAsync` helper that
  was extracted for the new arm. Migrate it; one place to get the swallow-but-log rule right.
- `WarnIfNotRelativeToBase` is applied in `TryFindCompletedGenerationAsync` and
  `DeleteAbandonedGenerationsAsync` but not in `DeleteAbandonedPolicyGenerationsAsync` (`:590-595`). Its whole
  purpose is to make a store with a foreign listing convention diagnosable in five seconds instead of leaking
  forever; the policy arm is the one where a silent leak was called out as the reason it exists.

---

## Test coverage gaps

Against `collector-tests`' ordering (contract invariants → recovery → publishing). Note that B0a has
*already* silently removed four of the change's own invariant tests from the default configuration — the gaps
below are on top of that.

1. **B1's actual shape is untested.** `Rejection_404WithEmptyResources_LeavesEveryAidUnattributedAndCounted`
   exercises the split with **two** AIDs, where the budget is never a constraint. Nothing covers *k>1*
   culprits in a large batch, budget exhaustion, or the resulting `RequestFailed` fan-out — and nothing asserts
   the **call budget**, which `collector-tests` names as a hard rule ("assert the call budget: one join per
   bounded unit"). A test that stubs a vendor refusing any batch containing one of three bad ids out of 64, and
   asserts both the request count and that the 61 good ids keep their assignments, would have caught this.
2. **M2 is untested.** No test drives a staged `resolved` edge against a cold/absent definitions artifact and
   asserts the emitted `definition_status`. That is the assertion that pins the fix.
3. **M4 is untested.** No test interrupts `RunPolicyStageAsync` mid-loop and asserts that the resumed stage
   skips covered pages. `PolicyStageFailure_CostsTheStageNotTheGeneration` (around
   `FalconTwoPhaseFindingsTests.cs:3490`) proves the generation survives, which is a different property.
4. `FalconFlowExceptionClassifierTests` covers the `DataPipelineException` split well (the real
   `GuardedObjectStore` driven down both paths — good, keep it), but nothing asserts that the flow's own
   contract-violation exceptions are classified, which is M5.

---

## Areas reviewed and found sound — stated so you do not go looking

- **Stream read-once and disposal in `FalconDevicePolicyClient`.** `StreamedResponse` is a plain holder
  (`RawResponse` + `ContentStream`); IntegrationInfra's own doc
  (`Conversation/docs/Streaming-via-Http-Package-Session.md:68`) makes "dispose `RawResponse` when done" the
  contract, and `AttemptBatchAsync:237-257` does exactly that in a `finally` on every path including
  `ThrowClassifiedFailure`. `ReadDiagnosticBodyAsync` uses `leaveOpen: true` so it does not close the stream
  early; `ReadPayloadAsync` and `ReadDiagnosticBodyAsync` are mutually exclusive per response, so the body is
  read exactly once. The `ArrayPool` rent/return in `ReadPayloadAsync:545-604` is correct across the growth
  path. The incremental `Utf8JsonReader` state machine (`DeviceEntityAccumulator.Consume:667-771`) rolls back
  correctly on a partial value and tracks `_topLevelProperty` at the right depth.
- **Request reuse across retries.** The session assembly carries a `CloneRequestEnvelope`, so re-sending the
  `HttpRequestMessage` with its `StringContent` through the Polly chain is handled. Not a hazard.
- **Object-store read discipline.** No slot is held across a `yield return` anywhere in the new code.
  `ReadHostPageAsync` takes a slot via `OpenReadAsync` and releases it before returning;
  `FalconFrozenKeyList.EnumerateAsync:238-245` composes *after* that stream is disposed and *before* the first
  yield, with a comment saying why; `TryReadPolicyEdgePageAsync`/`TryReadPolicyDefinitionsAsync` go through
  `ExistsAsync` (`StatAsync`, no slot) + `ReadNdjsonLinesAsync` (no slot, by design — IntegrationInfra
  `GuardedObjectStore.cs:202-232`). The claims in the comments match the sibling repo's source.
- **The message match in `FalconFlowExceptionClassifier.ClassifyIngestionGuardFailure`** is proportionate, and
  I checked it rather than taking the comment's word. `DataPipelineException` is `sealed`, derives from
  `InvalidOperationException`, adds no members, and every Ingestion throw site passes a message and no inner
  exception — so there is genuinely nothing else to discriminate on from the consumer side.
  `Ingestion__MaxConcurrentReads=` appears in exactly one production throw
  (`GuardedObjectStore.cs:374-380`, reached for both the depth semaphore and the memory-pressure gate — both
  read contention, both correctly retryable), and the neighbouring ceiling/listing throws carry different
  keys. The residual-is-non-retryable default is the right prior. The reasoning about the marker being a
  documented configuration key rather than prose is sound, and the paired test that drives a real
  `GuardedObjectStore` down both paths is what makes the coupling payable — keep it.
  The better option, if you want the rule honoured rather than paid for: `DataPipelineException` is a public
  type in a repo you own, and a nullable `Reason` enum property on it is a source- and binary-compatible
  addition. That is a small Infra change plus a package bump, and it retires the coupling for every collector
  rather than just this one. Worth doing when Infra is next touched; not worth blocking this change for.
- **Cross-flow envelope identity is genuinely tested, not incidentally true.**
  `FalconCollectorTests.R5_BothFlowsEmitCanonicallyIdenticalDevicePolicies` drives both flows end to end over
  one route table, asserts the envelope is the fully-resolved shape first (so the comparison is not two
  `disabled` envelopes agreeing vacuously), asserts `sweepCalls == 1` so the findings side really came from
  the sweep and not the per-id fallback, and then compares `GetRawText()` including member order. That is the
  right test and it closes the drift channel the restructure opened. Structurally both paths do end at
  `FalconDevicePoliciesEnvelope.Compose`, so the identity is by construction and not by coincidence — with the
  caveat in M2, which is the one place the two paths can still diverge (assets composes against a live cache,
  findings against a possibly-cold one).
- **Bounded-unit discipline.** Assets materialises exactly one Discover page and enriches it before
  publishing (`Flows/Assets/FalconAssetsScrollRunner.cs:296-315`, with the comment
  `collector-flow-patterns` asks for); findings resolves one staged page per device-entity call and explicitly
  rejects the aid-batch as the unit (`FalconHostSpooler.cs:406-411`). One join per bounded unit, never per
  record. The run-scoped cache is not checkpointed, and `FalconPreventionPolicyCache`'s remarks argue that
  correctly.
- **Cache thread-safety.** `FalconPreventionPolicyCache` is a plain `Dictionary`, and `TryGetDefinition` is
  read from Phase 2 while the pump fans out — but every write (`SweepDefinitionsAsync`,
  `HydrateDefinitionsAsync`, `RehydrateDefinitions`) happens in `EnsurePolicyPlaneAsync`, strictly before
  Phase 2 begins. Read-only concurrent access to a `Dictionary` is safe. Fine as written; if anyone later
  moves definition hydration into Phase 2, this becomes a race, so it is worth one line of comment on the
  field rather than a `ConcurrentDictionary`.
- **Path safety.** `FalconStagingPaths.RequireSegment` rejects `/` and `..` for both id spaces from one
  implementation, and generation ids arriving from persisted state go through it. `AssertNotParserVisible` in
  the `FalconStagingArea` constructor is the right place for a cross-repo coupling this repo cannot compile
  against. `TryGetGenerationId` correctly returns null for the whole policy subtree.
- **Ledger design.** Every `FalconStageLedgerEntry` field is O(1) in the traversal, the `Degraded` state is
  derived from the counters rather than passed in, and `WarnIfManifestNearsReadCeiling` puts a 75% tripwire on
  the one member that grows (`PageKeys`) against the real `MaxControlArtifactBytes`. `EdgesContradictMode`'s
  `(state is Disabled) == enrichmentEnabled` reads oddly but is correct in all four combinations.

---

## Summary

The restructure is well-reasoned and unusually well documented, and the parts that are easy to get wrong —
stream disposal, read-slot nesting, bounded-unit call budgets, cross-flow payload identity, keeping the
manifest write ahead of the policy work — are right. The findings cluster in one place: **what gets published
when the policy answer is missing.** B1, M2 and M3 all end at the same emitted bytes — a host asserted to have
no Prevention policy, or a policy definition asserted not to exist, on evidence the collector never obtained.
Each has a small, local fix. M4 and M5 are operational: a stage that cannot converge under repeated failure,
and deterministic faults riding a blind retry path the same file was edited to remove.

Merge order I would use:

1. **B0** — the suite has to be green, and B0a is mechanical once the default is decided. B0b needs the
   ten-minute triage above before anyone can say whether it is a fixture or a live regression on the
   deferred-recovery budget; do not merge past it either way.
2. **B1** — turns a routine amount of Discover staleness into most of a page published as unprotected.
3. **M2 / M3** — small patches, and they are what make the "always self-describing" envelope claim true.
4. **M4 / M5** — operational; safe to land in a follow-up if you would rather, provided M4's non-convergence
   is understood before this runs against a large tenant.

Everything under Minor is optional for this merge, except m6 and m7, which I would take now because they are
three lines each and both currently disagree with their own comments.
