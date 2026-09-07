# Falcon Spotlight Long-Haul Assessment

Analyze the current CrowdStrike Falcon Spotlight vulnerabilities collector implementation for mega-tenant historical pulls that can traverse tens of millions of records across multi-hour or multi-day runs.

Produce a fact-based engineering assessment covering OAuth/token lifecycle, pagination, failure handling, checkpointing, throughput behavior, persistent 401 classification, root cause hypotheses, and minimal changes for safer long-haul collection with partial publishing and resume.

## Current Evidence Update

The production incident is now classified as `Persistent401AfterRefresh`: a fresh OAuth token was issued successfully, the failed Spotlight request was replayed, and the replayed request still returned HTTP 401 from `crowdstrike-api-gateway`.

The local `FalconDocs/spotlight.pdf` changes the resume design constraint: Spotlight `after` tokens are documented as expiring 120 seconds after a call, so any voluntary cooldown longer than roughly two minutes must resume from the persisted watermark/date anchor instead of attempting cursor reuse.
