# Constraints

## Architecture (repo CLAUDE.md)
- Shared owns ProcessAsync sequencing, session lifecycle, egress, DONE payload shape; collector owns vendor logic, pagination, checkpoint formats, flow exception classification.
- Do not duplicate transport retries (DefensiveToolkit/Polly) or token refresh (CredentialProviderAuthenticator) in the collector — collector only re-anchors cursors.
- Publishing through `CollectorNdjsonPublisher`; output names remain `findings_*.json` (correlated records) — no new target naming.
- Resume is mandatory and test-backed; long waits externalize via `AdapterResult.PartialResult` (never sleep in-process beyond the in-process server-delay threshold).

## Vendor (verified, official PDFs in FalconCollector/FalconDocs/OfficialDocs)
- `after` tokens expire 120s after the call (Discover AND Spotlight); never persist or resume a cursor — checkpoints carry watermarks only.
- Sort enums: Discover `last_seen_timestamp`, Spotlight timestamps only (no aid sort). Both scrolls must set an explicit ascending sort as their re-anchor axis.
- Page limits: Discover ≤1000, Spotlight ≤5000; repo operating points 1000 / 2500. Aid list of 250 per request is repo-proven; do not exceed without new evidence.
- Rate limit 100 req/s sustained / 6000 burst per CID; OAuth tokens ~30 min.

## Egress
- `MaxBytesPerBatch` 50 MiB hard; a single record must fit or publish fails fast — chunk cap must keep worst-case record far below it (~2000 findings ≈ well under with apps stripped).
- Hash (W4): streaming, computed during write, no extra buffering; logged on the existing publish-completion line; covers the logical object (all multipart parts).

## Memory (K8s pods)
- Peak working set bounded by one Spotlight API page + one Discover page + egress buffer; never buffer a whole aid batch or a whole host's findings.

## Testing / tooling
- xUnit + Moq + FluentAssertions; central package versions in Directory.Packages.props; `InternalsVisibleTo` for internals.
- Lane/segment tests retire with their machinery; new tests: traversal, group-by-aid, chunking, zero-finding emission, checkpoint round-trip, re-anchor recovery, strip/sort record shaping.
- LocalAdapterRunner must run the new flow; review `--falcon-recovery-simulation` scenarios (lane/segment-coupled ones retire or re-target).
- Parser repo: tests green for BOTH input shapes (legacy split + correlated); follow existing test conventions (`tests/test_crowdstrike_assets_findings.py`).

## Process
- Docs sync required: FalconDocs/CollectorDocs (01-collection-strategy etc.), Collectors/README.md reference notes, ai/skills files describing lanes/segments.
- Major collector version bump (`CollectorVersion` in csproj); checkpoint format version bump, no cross-version resume.
- No "Legacy" in identifiers — name for behavior.
