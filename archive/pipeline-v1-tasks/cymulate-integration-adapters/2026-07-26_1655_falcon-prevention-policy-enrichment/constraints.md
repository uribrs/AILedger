# Constraints

## Product contract

- Prevention Policy only.
- Enrichment is nested in each Discover host.
- Both `CollectAssets` and `CollectFindings` use the same host envelope.
- A successful absence of policy never drops an asset.
- No standalone policy entity or flow.
- No alert/detection collection or policy-to-finding attribution.

## Runtime contract

- One device assignment lookup per bounded asset page/findings AID batch.
- Definition lookups are deduplicated through a run-scoped cache.
- Assignment uncertainty prevents publication/checkpoint for the unit and uses
  existing Falcon recovery.
- Definition unavailability publishes partial and checkpoints.
- Authorization and malformed contracts never masquerade as empty policy data.
- Existing cursor, watermark, recovery, finding shaping, publish, and checkpoint
  behavior remains intact.

## Architecture

- Reuse Shared session/auth/retry/rate-limit/publishing/recovery mechanisms.
- Keep vendor logic inside FalconCollector.
- Policy logic lives under `FalconCollector/Flows/Policies/` — a dedicated
  policy space is mandatory, not optional.
- One shared enrichment component serves both flows; no standalone policy flow
  and no per-flow duplicate of the enrichment logic.
- Both `CollectAssets` and `CollectFindings` invoke enrichment on every run;
  policies are gathered whenever assets are gathered.
- Each flow contributes only its bounded unit and call site.
- No new generic policy abstraction or Shared infrastructure without proven
  necessity.
- Small focused methods/classes; preserve codebase conventions; no long
  procedural methods and no speculative abstraction.
- No new `HttpClient`, token manager, retry loop, or attempt counter.
- Extend `FalconUrls` for endpoint construction.
- No checkpoint format bump unless persisted state changes.

## Safety

- Preserve dirty-worktree/user changes.
- Work on the prepared branch `feature/falcon-prevention-policy-enrichment`.
- Do not stage, commit, or push unless separately requested.
- Never run the full FalconCollector test suite; filter to change-relevant
  classes.
- Never expose or persist lab credentials or tenant secrets.
- Do not use probe raw finding records as the production output contract.

