# Task: Emission carry (DataPipeline/Egress)

Carry the Emission concern into IntegrationInfra, behavior verbatim (namespace-only rewrite + Kernel
rewires + D8 naming neutralization). Emission = "deliver produced records to their destination as bounded,
validated batches" — the publish engine + egress invariants. The engine is the single authoritative JSON
validator; preserve that.

## In scope (→ src/IntegrationInfra/Emission/)
- `DataPipeline/Egress/*.cs` (18, incl. `Multipart/`, `Ndjson/`, `Telemetry/` subfolders).
- `Glossary/CollectorGlobalDefaults.cs` (egress invariants; only Glossary type Egress uses).

## Naming (D8) — neutralize generic-infra identity names
- `CollectorGlobalDefaults` → `AdapterGlobalDefaults`
- `CollectorOutputDefaults` → `AdapterOutputDefaults`
- `CollectorNdjsonPublisher` → `AdapterNdjsonPublisher`
Wire-safe: C# type names only; held values (content-type, file suffix, byte-budget) unchanged.

## Out of scope
- The ISink-seam reshape (StreamKind discriminator), killing the static entry + assets/findings hardwiring
  (DIP/OCP) — a RESHAPE requiring an operator decision. Flag, don't do.
- Other Glossary (AdapterTopics, vendor names); any behavior change.
- Mutating the source repo.

## Deliverables
1. Verbatim relocation (logic) under Emission/, namespaces rewritten, D8 renames applied.
2. Kernel rewires (Exceptions, Telemetry); Glossary type → Emission.
3. PackageReference added if build needs it (e.g. Microsoft.Extensions.Configuration.Abstractions).
4. XML docs on public members; Emission/README.md extended.
5. Tests for the testable pure units.
6. `dotnet build` clean; tests pass.
