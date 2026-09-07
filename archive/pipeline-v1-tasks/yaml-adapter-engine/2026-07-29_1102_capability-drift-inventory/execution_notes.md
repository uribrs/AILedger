# Execution Notes — Capability Drift Inventory

Appended per dimension. Created at contract design so it exists before work starts.

---

## Contract-design measurements, recorded so execution does not repeat them

Measured 2026-07-29T11:02:27Z, `dev` at `dd25c0d`:

| what | value | how |
|---|---|---|
| original engine | 65 authored `.cs`, 11,364 lines | `find` + `cat | wc -l`, bin/obj excluded |
| original vs `5e5ca52` | **64 of 65 byte-identical** | per-file SHA256, `git show` vs disk |
| the one divergence | `Auth/OAuth2ClientCredentialsAuthenticator.cs`, 36 diff lines | `diff` |
| `5e5ca52` buildable | carries `.slnx`, `csproj`, `Directory.Build.props`, `Directory.Packages.props` | `git ls-tree` |
| current | 183 `.cs` in `src/`, 735 tests / 0 failed / 0 skipped | `find`, `dotnet test` |
| corpus | 279 `.yaml` | `find ... -name '*.yaml' | wc -l` |
| `static_value` / `credential_key` in corpus | **0 files** | `grep -rl "static_value\|credential_key"` |

### The seed finding, stated precisely

`OAuth2ClientCredentialsAuthenticator` in the vendored tree has two things HEAD lacks:

1. `var prefix = (placement?.Prefix ?? "Bearer").TrimEnd();` — HEAD has no `TrimEnd()`. The vendored
   comment says it tolerates a prefix authored *with* a trailing space so the join never double-spaces.
2. `ExtraFields` value resolution in priority order — `static_value`, then `credential_key`, then runtime
   credentials by field name. HEAD implements only the third.

What makes (2) more than a diff: HEAD's `Authentication/Contracts/Models/ExtraFieldDefinition.cs` declares
both `StaticValue` and `CredentialKey`, and `AwsSigV4Authenticator.cs:229-233` honours them. So one
authenticator honours a contract field its sibling silently ignores.

**Not yet established, and it changes the severity completely:** whether the adapter actually compiles the
vendored tree. If it compiles something else, this is a stale fork rather than a production gap. That is
A2, and it is a named success criterion.

### Why the enumeration discipline (D2) matters here

An 11,364-line read has a recall problem, and the capabilities most likely to be missed are small
defensive ones — which is precisely the class of the three drifts already on record: D18's orphaned
streaming path, and the two guards the sink-inversion retrospective records as deleted under a green
suite. So each dimension must work from a countable surface (declared YAML keys, `throw` sites, nullable
guards, checkpoint field names) and report that count, rather than resting on having read the file.

---

## I0 — baseline build / differential feasibility

_pending_

## I1 — config surface

_pending_

## I2 — guards and defensive checks

_pending_

## I3 — error paths and exact message text

_pending_

## I4 — failure behaviour, cursor recovery, checkpoint fields

_pending_

## I5 — performance characteristics

_pending_

## I6 — host contract

_pending_

## I7 — improvements

_pending_

## I8 — synthesis

_pending_
