# Task: Wire asset collection into the Falcon findings flow

Make the Falcon `CollectFindings` flow also publish hosts to `assets_*.json`, mirroring the
CortexXdr/DefenderVm findings flows, while leaving the findings (vulnerability) output and logic
byte-for-byte unchanged.

- Add an **independent assets stage** inside the findings flow, sequenced **assets → findings**.
- Make it **resumable** via a single unified findings checkpoint carrying a new `Stage`
  discriminator. Legacy / no-`Stage` checkpoints are treated as `"findings"` and skip the assets
  stage (no re-publish).
- **Collect all hosts when unfiltered**; when an FQL filter is present, scope by the same
  last-seen gate the findings flow already uses.
- Reuse `AssetIdsFetcher` as the host pager via an **additive, default-off** host-row capture.

Authoritative spec: `/Users/user/.claude/plans/falcon-collector-needs-to-joyful-firefly.md`.

Working dir: `/Users/user/Dev/cymulate-integration-adapters` — branch `falcon-collector-dup-data-BUG`.
