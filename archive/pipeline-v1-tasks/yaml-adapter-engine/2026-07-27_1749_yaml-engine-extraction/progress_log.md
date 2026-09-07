# Progress Log — YAML Engine Extraction

Handoff document. Read this first, then `prompt_contract.md` for the original scope and
`review/` for the three independent review passes.

**Status:** phases 1–2 complete, reviewed, green. Not pushed. Branch
`feat/engine-extraction`, 5 commits ahead of `dev`. Phase 3 not started.

**Read issue 1 in section 4 before anything else.** The test suite is not
deterministic — roughly 2 failures per 25 runs — and the cause is a real product
defect, not test noise.

---

## 1. Where things stand

| Commit | What |
|---|---|
| `5e5ca52` | Phase 1 — verbatim move of engine + tests out of the monorepo |
| `b0e0b87` | Phase 2 — reshape into concept architecture |
| `dae3cd3` | Dependency-rule fix (from verifier-1) |
| `35bb3e6` | Review findings; corrected three false claims in the docs |
| (this)    | Contracts split by kind (Enums/Interfaces/Models/Exceptions); Logic subdivided by sub-concept |

Build: 0 warnings. Tests: **699 passed / 0 failed / 0 skipped** — identical to the monorepo
baseline at every gate.

**The load-bearing guarantee: all 113 engine type bodies are byte-identical to the
monorepo.** Not one line of behaviour changed across the whole extraction. This was verified
mechanically (not by inspection) three separate times — take everything after the
`namespace …;` line, strip blank lines, assert it appears verbatim in the phase-1 tree.

**Preserve this property until phase 3 deliberately ends it.** It is the only reason a
6,000-line diff was reviewable, and it is what lets you assert the package behaves exactly
like the code running in production today.

---

## 2. THE HOST BOUNDARY — do not reinvent this

**This is the single easiest thing for a new agent to get wrong.**

The engine is deliberately incomplete. Large parts of what makes the YAML collector work in
production live in the **host**, not here, and the engine is written to *expect them to be
supplied*. If you find yourself about to implement batching, S3 access, checkpoint
persistence, resume scheduling, or an egress pipeline inside this repo — stop. It already
exists in `cymulate-integration-adapters`, it is battle-tested, and duplicating it here
would both violate rule 0 and fork logic that must not diverge.

### What the host provides

The bridge adapter (`Collectors/YamlCollector/`) imports these from
`Cymulate.Integration.Adapters.Shared` and the SDK:

| Host capability | Namespace | What the engine does instead |
|---|---|---|
| Record egress — byte-capped NDJSON, multipart upload, memory-pressure handling | `Shared.DataPipeline.Egress` | Calls `IExecutionSink.PublishBatchAsync` / `PublishStreamAsync` and lets the host decide how bytes leave |
| Run orchestration, recovery, resume | `Shared.Orchestration.Collectors{,.Recovery,.Triggers}` | Surfaces `OperationResult.RetryAfter` and pagination state; the **host** schedules the resume |
| Checkpoint persistence | adapter `Checkpointing/` | Calls `SetPaginationState` / `SetCursorRecoveryState` / `SetControlState`; the host stores them |
| Outer resilience strategy | `Shared.Resilience{,.Policies}`, `Cymulate.Http.Package.DefensiveToolkit` | Owns only *per-request* retry/backoff described by the YAML |
| Collector event envelopes | `Shared.Events.CollectorEnvelopes` | Nothing — engine never builds an envelope |
| Definition fetch from S3 | adapter `Execution/S3YamlDefinitionLoader.cs` | Accepts a definition; never fetches one |
| Trigger payload parsing | adapter `Payload/` | Accepts `inputs` + `credentials` dictionaries |
| Adapter contract, `IAdapterExecutionContext` | `Cymulate.Integration.Sdk` | Never references it — **rule 0** |
| `IHttpClientFactory`, `ILogger<T>` | DI | Constructor-injected; engine assumes a configured factory |

### What the engine owns

Everything the YAML definition describes: authentication, retry/backoff, pagination,
cursor recovery, templating, response mapping, XML→JSON, and multi-stage workflows.
Per the adapter's own csproj comment: *"the YAML engine still owns all HTTP behaviour (auth,
retry, backoff, pagination) per the YAML definition."*

### The seam

`IExecutionSink` (`Sinks/Contracts/IExecutionSink.cs`) is the contract between the two.
`IsbExecutionSink` in the adapter is the production implementation and implements **all**
members including the three that are default no-ops here. Any change to that interface is a
breaking change to the host — relevant because phase 3 plans to split it.

**Rule 0 is what keeps this honest:** the engine has zero `Cymulate.*` package references.
Verified in `35bb3e6`. If a task seems to require adding one, the work belongs in the
adapter, not here.

---

## 3. Consumer migration — namespaces changed

The adapter still uses a `ProjectReference` into the monorepo copy. When it switches to the
package, **every one of these usings breaks.** The monorepo is untouched by this branch, so
this migration has not been done and is not scheduled.

| Old | New |
|---|---|
| `…Engine.Auth` | `…Engine.Authentication` |
| `…Engine.Retry` | `…Engine.Resilience` |
| `…Engine.Loader` | `…Engine.Definition` |
| `…Engine.Validation` | `…Engine.Definition` |
| `…Engine.Mapping.Transforms` | `…Engine.Mapping` |
| `…Engine.Models` | split — see below |
| `…Engine` (root) | `…Engine.Execution` (`IIntegrationEngine`, `IntegrationEngine`, `OperationResult`), `…Engine.Sinks` (`IExecutionSink`), `…Engine.Diagnostics` (`HttpTraceEntry`), `…Engine.Mapping` (`XmlToJsonConverter`, `XmlShaping*`) |
| `…Engine.{Pagination,Templating,Workflow,Mapping}` | unchanged |

`Models/` no longer exists. Its 40 types went to the concept that consumes each one:
`Definition` (document model), `Authentication`, `Pagination`, `Resilience` (all error/retry
config), `Mapping` (response/transform config), `Workflow` (stage/merge/poll config).

---

## 4. Issues that must not be overlooked

### Runtime defects — real, pre-existing, NOT introduced by this work

All three need method-body changes, which is why they were left alone. None is a regression;
all exist in production today.

1. **Temp NDJSON spill files: orphaned on failure, path-traversal vector, AND a confirmed
   live flaky test.** `Execution/Logic/IntegrationEngine.cs:1259-1265, 1349-1359`.
   **This is the highest-priority item in this document.** Three distinct problems in one
   block of code:
   - Failure results carry no `ResultFilePath`, so the spill is unreachable and permanent —
     a long-running pod accumulates them until the disk fills.
   - `integrationName`, a definition key, flows **unsanitized** into `Path.Combine`. A key
     containing `../` escapes the results directory.
   - The filename is `{integration}_{operation}_{DateTime.Now:yyyyMMdd-HHmmss-fff}.ndjson`.
     Two runs of the same operation in the same millisecond get the same path, and
     `new StreamWriter(path, append: false)` then truncates one of them.

   **The collision is not theoretical — it is failing tests today.** While reorganising
   folders I saw the suite fail intermittently, then reproduced it: ~2 failures in ~25 runs.
   Captured via `--logger trx` over repeated runs:

   ```
   Execution.AnnotateAndConstTests.Annotate_StreamedNonObjectElements_RawWrapperIsStamped
   Assert.StartsWith() Failure
     String:         "{"$raw":""x""}"
     Expected start: "{"sourceType":"delta","id":1"
   ```

   The test read another test's spill content. Three test files exercise `ResultFilePath`
   (`AnnotateAndConstTests`, `EngineBehaviorLockTests`, `ModelRecordEdgeTests`) and they use
   generic vendor names like `Vendor`; xunit parallelises test classes, so two land in the
   same millisecond and collide.

   **Consequence for phase 3:** the 699-test suite is the *only* safety net once
   byte-identity stops applying, and it is currently non-deterministic. Fix this before
   phase 3, not during. Suggested: `Guid.NewGuid().ToString("N")[..8]` in the filename,
   `Path.GetInvalidFileNameChars()` sanitisation, `DateTime.UtcNow`, and delete the spill in
   the `finally` unless it is being returned in a successful result.

   To reproduce: `for i in $(seq 1 15); do dotnet test … --logger "trx;LogFileName=/tmp/trx/r$i.trx"; done`
   then grep the trx files for `outcome="Failed"`. A single run proves nothing.
2. **`_definitions` is an unsynchronized `Dictionary` with a public mutator.**
   `IntegrationEngine.cs:50/93/177`. `InjectDefinition` writes while `ExecuteOperationCoreAsync`
   reads. The engine's shape says DI singleton. A concurrent write during a read on
   `Dictionary` can produce a torn bucket chain and loop forever inside `TryGetValue`.
   One-word fix to `ConcurrentDictionary`.
3. **`HmacAuthenticator` does not compute an HMAC.** `Authentication/Logic/HmacAuthenticator.cs:119`
   is `SHA256(secret ‖ nonce ‖ timestamp)` — a digest of a concatenation. Correct for the
   Cortex XDR scheme, so behaviour today is right. The hazard is that the class name, the
   config type (`HmacConfig`), the YAML discriminator (`hmac`) and the "only sha256 is
   implemented" comment all read as "this is a keyed MAC". It is also the **only one of eight
   authenticators with zero test coverage**.

### Architecture findings

4. **The primitive tier is not acyclic.** `Pagination/Logic ↔ Resilience/Logic` is a real
   2-cycle (`BodyCursorPaginator` → `DelayResolver.GetRegex`; `EngineFailureClassifier` →
   `CursorRecoveryTracker`), plus `Definition/Logic → Mapping/Logic`. This is now documented
   honestly in `ARCHITECTURE.md` rather than implied away. Phase 3 should decide whether
   `Pagination ↔ Resilience` is a misdrawn boundary or an accepted coupling.
5. **Nothing enforces any architecture rule.** The dependency rule lives in prose. The audit
   is a script that was run ad hoc and never committed. And `EngineIsolationTests` — the only
   automated guard for rule 0 — stayed behind in the *adapter's* test project in the monorepo.
   **This repo currently has no automated protection for its most load-bearing invariant.**
6. **Dependency audits must go by type reference, not `using` directive.** Namespace tracks
   the concept, so `using ….Workflow;` imports both layers and a grep-based audit
   over-reports. This produced a false positive in review. Enumerate the type names declared
   in `<Concept>/Logic/` and grep for those.
7. **46 pre-existing XML-doc defects** (20 broken crefs, 20 missing `<param>`, 4 ambiguous,
   2 orphaned) are hidden because `GenerateDocumentationFile` is off. Turning it on is one
   line; fixing what it reveals means editing doc comments, which ends the byte-identity
   guarantee. Reasoning is recorded in the csproj so it is not rediscovered.

### Correction to the record

The `dae3cd3` commit message claims "6 unused engine usings removed (4 pointed at Execution
or Workflow)". **That is false** — 2 were removed. The other eight `…Execution` deletions in
that diff were authenticators being rewritten to `…Diagnostics`. Not amended, because the
review artifacts reference that SHA.

---

## 5. Open decisions — none of these should be made silently

- Runtime defects 1–3: phase 3, or a dedicated fix branch first? The path-traversal argues
  for "now".
- `Microsoft.Extensions.*` floors are `10.0.9` on a `net8.0` package — drags every consumer
  onto 10.x. A library pins the **lowest** compatible floor (`8.0.x`). Must be settled before
  the first publish; raising a floor later is fine, lowering it is not.
- `VersionPrefix` is `1.0.0`. Phase 3 narrows ~130 public types to `internal`, which is a
  compile-break. Ship `0.x` / `1.0.0-preview.N` until the surface is settled.
- No `LICENSE` file; the nuspec has no license element.
- SourceLink is configured but inert — the repo is GitHub Enterprise
  (`cymulate-corp.ghe.com`) and the SDK's bundled SourceLink only recognises `github.com`
  unless the host is declared.
- `InternalsVisibleTo`: none exists, deliberately. Phase 3 must decide explicitly.
- A4: port an equivalent of `EngineIsolationTests` to guard rule 0 here?

---

## 6. Working notes for whoever continues

**Verify the byte-identity property before and after any mechanical change:**

```bash
# every type body must still appear verbatim in the phase-1 tree
python3 - <<'PY'
import os,subprocess
E='src/Cymulate.Integration.Yaml.Engine'
names=[n for n in subprocess.run(['git','ls-tree','-r','--name-only','5e5ca52',E],
       capture_output=True,text=True).stdout.split() if n.endswith('.cs')]
hay='\n'.join(subprocess.run(['git','show',f'5e5ca52:{n}'],capture_output=True,text=True).stdout
              for n in names)
def body(t):
    ls=t.split('\n'); i=next(k for k,l in enumerate(ls)
        if l.startswith('namespace ') and l.rstrip().endswith(';'))
    b=ls[i+1:]
    while b and not b[0].strip(): b=b[1:]
    while b and not b[-1].strip(): b=b[:-1]
    return '\n'.join(b)
bad=[f for r,d,fs in os.walk(E) if not any(x in r for x in ('bin','obj'))
     for f in fs if f.endswith('.cs')
     and body(open(os.path.join(r,f),encoding='utf-8-sig').read()) not in hay]
print('differ:', bad or 'none')
PY
```

**Traps hit during this work — do not repeat:**

- `git mv` **fails on untracked files** and the script did not check the return code, so it
  wrote the destination and left the original: 47 duplicate pairs. Use `shutil.move`; git
  detects renames by similarity at commit time anyway.
- `os.walk` **re-visits directories created during the walk**. Snapshot the file list first.
- Deriving a C# type name by regex over a whole chunk picks up words from doc comments, and
  `record struct X` yields "struct". Match the declaration line only.
- Brace-counting to find type boundaries is wrong in this codebase — doc comments and string
  literals are full of `{{template}}` braces. Top-level types start at column 0 and close
  with a `}` at column 0; use that, and round-trip-verify by reassembling.
- A pattern matching `using Cymulate.Integration.Yaml.Engine…` also matches the **test**
  project's own `…Engine.Tests.…` namespaces. Exactly one file was affected; it was caught by
  the compiler, but only by luck of it being a hard error.

**Useful commands:**

```bash
dotnet test Cymulate.Integration.Yaml.Engine.slnx          # expect 699/699
git -C /Users/user/Dev/cymulate-integration-adapters status --porcelain   # must stay empty
grep -rh '^namespace' --include='*.cs' src | sort -u        # expect exactly 10
```

The monorepo is **read-only** for this work and has been throughout. Source of truth for
"what the code was": `cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine`.

---

## 7. What phase 3 is for

Defined in `ARCHITECTURE.md`. Summary: decompose `IntegrationEngine`'s 741-line
`ExecuteOperationCoreAsync` and `WorkflowRunner`, introduce `ExecutionRequest` /
`ExecutionOptions` to replace the four-overload chain, replace `loopEndedViaBreak` with a
`PageOutcome` return type, split `IExecutionSink`, and trim the public surface to `internal`.

Phase 3 is the first phase where behaviour may legitimately change — and therefore the first
where the byte-identity check stops being the safety net. The 699 tests become the only
guard. Read `review/code-reviewer-1.md` before starting; several of its findings are cheapest
to fix during that decomposition rather than after.
