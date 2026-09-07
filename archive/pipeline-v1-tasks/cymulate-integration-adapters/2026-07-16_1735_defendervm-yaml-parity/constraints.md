- Generic, no identity: no vendor names/conditionals in engine code.
- Yaml-expressed: capabilities declared in the existing grammar; absent key = no-op.
- No peripheral regression: fleet yamls without the new keys byte-identical behavior;
  workflow users are qualys/insightvm-cloud/tenable.io/defender-vm only.
- Match native semantics exactly: annotate = first-position, label-wins case-insensitive,
  objects only (DefenderVmRecordFormatter.AddMetadata); wrap via mapping const + $self.
- No ISB/Shared/adapter-surface/S3-layout/done-event changes.
- Versions: never in csproj; bumps are the operator's call.
- Engine suite must be green (581 at start); tests at phase boundaries.
- recVulns fan-out and mid-stage cursor resume: OUT OF SCOPE, booked for batch 2.
- Commits per-change approval; never push without approval.
