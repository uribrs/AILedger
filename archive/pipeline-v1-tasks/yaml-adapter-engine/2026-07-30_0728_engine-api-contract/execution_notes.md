# Execution Notes

## Measured baseline — 2026-07-30T07:28:03Z. Treat as established; do NOT re-derive.

Repo: `/Users/user/Dev/yaml-adapter-engine`, branch `dev` @ `dd25c0d`, **0 porcelain lines**.
Raw data preserved in `measurements/engine_public_types.json` and `measurements/surface_reach.json`.

### Public surface today — 103 types

| count | namespace |
|---|---|
| 21 | `…Engine.Authentication` |
| 16 | `…Engine.Mapping` |
| 16 | `…Engine.Workflow` |
| 15 | `…Engine.Resilience` |
| 14 | `…Engine.Pagination` |
| 13 | `…Engine.Definition` |
| 3 | `…Engine.Execution` |
| 2 | `…Engine.Sinks` |
| 2 | `…Engine.Templating` |
| 1 | `…Engine.Diagnostics` |

Command: regex over `src/**/*.cs` for `^\s*public\s+(sealed|abstract|static|partial)*\s*(class|record|struct|interface|enum)`.

### Leg (a) — static C# reachability from the real consumer

The consumer is `Cymulate.Integration.Adapters.YamlAdapter`, which now takes the engine as a
`PackageReference` (`1.0.0-preview.1`). **13 of 103** public types are named by its production code:

`IntegrationEngine`, `OperationResult` (Execution) · `IntegrationDefinition`, `YamlIntegrationLoader`,
`SchemaValidationException` (Definition) · `IExecutionSink` (Sinks) · `WorkflowRunner`,
`WorkflowCheckpoint`, `WorkflowResult`, `WorkflowStageException` (Workflow) · `PaginationStrategy`,
`CursorRecoverySnapshot` (Pagination) · `DelaySource` (Resilience)

0 additional named by the adapter's tests; 1 additional by `YamlLocalRunner`.

That maps almost exactly onto what `CLAUDE.md` already declares the intended surface to be. The
document was right; the code never enforced it.

**This figure measures ENTRY POINTS, not the required-public set.** An early framing of "13 reachable
vs 89 not" was misleading and is retired — see leg (b).

### Leg (b) — what the consumer actually traverses

A public property cannot expose an internal type, so the deciding question is per-member: does the
consumer traverse it? Measured by member-access grep over the adapter's production sources:

- `IntegrationDefinition` → `.Operations` (7 sites), `.Workflow` (2), `.Vendor` (2), `.DisplayName` (2).
  From `Operations`: `.Topic` and `.Pagination.Strategy`. **Never** `.Authentication`, the mapping
  configs, or the retry configs.
- `OperationResult` → `.HttpStatusCode`, `.Success`, `.ErrorMessage`, `.ErrorCode`, `.RetryAfter`,
  `.PagesRetrieved`, `.IsTransportError`, `.ProcessingTime`
- `WorkflowResult` → `.TotalPublishedRecords`, `.Counters`, `.RetryAfter`, `.WaitReason`
- `WorkflowCheckpoint` → passed through and serialized by the host (`.AdapterState`, `.CursorToken`,
  `.CurrentSequenceId`, `.CreatedAtUtc` on the SDK checkpoint)

Untraversed public members on public types are narrowing candidates **together with their types**.

### Leg (c) — YAML-driven reachability, and why it argues FOR internal

Every authenticator and every paginator is constructed at exactly one site:

| implementations | sole construction site |
|---|---|
| `ApiKey`, `OAuth2ClientCredentials`, `AwsSigV4`, `Basic`, `Hmac`, `CustomHeader` authenticators | `AuthenticatorFactory.cs` |
| `Cursor`, `Offset`, `PageNumber`, `LinkHeader`, `Scroll`, `BodyCursor` paginators | `PaginatorFactory.cs` |

Command: `grep -rln "new <Type>(" src/ --include='*.cs'` for each — one file each, no exceptions.

Selection is by YAML value. These types must be **registered**, not **public**.

## Errors made while establishing the above — recorded so they are not repeated

- **`//.*` with `re.S`** deleted every source file from its first comment to EOF, so the first reach
  run reported **0 of 103**. Caught by sanity-checking one known-true case (`IntegrationEngine` is
  obviously used by the adapter). Fix: strip block comments with DOTALL, line comments without.
- Three sibling slips earlier the same session: a zsh glob failure (`*ISB*` matched nothing, so zsh
  aborted the whole command and the ISB repo was wrongly reported absent); a regex that read a primary
  constructor as a 127-line method; an ad-hoc brace counter that reported expression-bodied one-liners
  as 130 lines.

**The pattern:** a quick regex or glob is written and trusted. The check that actually works is
verifying the output against one case known to be true, before reporting anything.
