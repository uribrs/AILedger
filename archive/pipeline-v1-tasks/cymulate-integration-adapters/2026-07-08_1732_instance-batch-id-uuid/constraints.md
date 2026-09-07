# Constraints

- Folder segment `batch_{page:D6}` and ALL path logic (`BuildBatchSegment`, `StripBatchSegment`, `ResolveBaseUrl`, `RestoreBase`) stay byte-identical — zero storage-layout change.
- UUID must be deterministic: v5(fixed namespace GUID, `"{pristine base URL}/batch_{page:D6}"`). No randomness anywhere.
- Derivation input is the pristine base URL from `ResolveBaseUrl` (post-strip, post-trim) — never the live scoped `storageUrl`.
- Namespace GUID constant is frozen forever; doc-comment must say so (changing it re-mints every issued id).
- Announced value format: lowercase hyphenated UUID string (`Guid.ToString()` "D" format).
- .NET 8 only — no new packages; implement RFC 4122 §4.3 v5 via SHA-1 with correct namespace byte order (Guid mixed-endianness swapped before hashing and after).
- `RestoreBase` still removes the `instanceBatchId` key; dud-page behavior unchanged.
- Out of scope: folder-name changes, checkpoint changes, ISB/consumer work, version bumps.
- Test runs filtered to the four named suites only — no suite sweeps.
- C# style: small methods, mirror neighboring code, no speculative abstractions.
