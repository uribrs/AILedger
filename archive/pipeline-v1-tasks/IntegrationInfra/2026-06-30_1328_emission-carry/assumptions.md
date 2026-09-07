# Assumptions

- **A1 (VALIDATED):** Emission's source = `DataPipeline/Egress/*` (18 files incl. Multipart/Ndjson/Telemetry
  subfolders) + `Glossary/CollectorGlobalDefaults`. Confirmed by source survey + concern README.

- **A2 (VALIDATED):** `CollectorGlobalDefaults` is the only Glossary type Egress uses (15 refs); it depends
  on `System.Text` only (no other Glossary drag). Confirmed by grep.

- **A3 (VALIDATED):** Kernel rewires = `...Shared.Exceptions` → Kernel.Exceptions,
  `...Shared.DataPipeline.Telemetry` → Kernel.Telemetry. Both targets exist in Kernel (commit 9630194).

- **A4 (VALIDATED):** D8 naming neutralization applies — Emission is generic egress infra. The 3 Collector*
  type names → Adapter*. Wire-safe (held values unchanged). Headline naming decision for PR review.

- **A-pkg (OPEN — build-driven, non-blocking):** `Microsoft.Extensions.Configuration(.Abstractions)` may
  need a PackageReference (the source Shared.csproj had `Microsoft.Extensions.Configuration.Abstractions`).
  Resolve via build; add PackageReference + central PackageVersion at floor-resolved version (MS.Extensions.*
  = 10.0.9 per source central versions). If a package/version can't be resolved, STOP and surface.

- **A-json (OPEN — build-driven, non-blocking):** Whether Egress references `DataPipeline/Json` (Kernel.Json)
  — not seen in the usings survey. Confirm at build; if it does, rewire to Kernel.Json.

- **A-test (OPEN — scope detail, non-blocking):** Target the pure/testable units (formatters, options records,
  MultipartPartPlanner partitioning, the defaults, pure byte-budget/NDJSON logic). The DI/streaming-coupled
  publisher entry may resist clean unit tests — don't force brittle tests; note rather than force.

- **A-reshape (VALIDATED → constraint):** The ISink seam / static-entry kill / assets-findings rewire is
  deferred (a reshape needing operator decision); carry verbatim and flag.
