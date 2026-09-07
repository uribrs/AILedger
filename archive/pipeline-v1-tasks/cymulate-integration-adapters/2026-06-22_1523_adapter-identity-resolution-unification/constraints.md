# Constraints

- Primary work in `/Users/user/Dev/IntegrationServiceBus`.
- Adapters-repo (`/Users/user/Dev/cymulate-integration-adapters`) changes allowed ONLY after creating a new git branch there first (currently on `dev`).
- ISB work must also be on a non-default branch; do not commit to default/working branches in either repo.
- Keep `PlatformType` as the canonical token; do not replace it.
- Do not change the `ClientId` registry axis.
- Resolution must be total (every harvested permutation maps) and idempotent.
- Unknown vendor must NOT silently match `PlatformType.Custom` (Taegis registers as Custom).
- Collision policy must be explicit and documented, not silently resolved (Cisco duplicate EnumMember; missing `Defender Endpoint`/`FortiGate` aliases).
- Prefer the simplest change satisfying the locked design; added complexity must be justified.
- Both repos must build; relevant tests pass.
- Adapters repo targets `net8.0` while installed SDK is 9.x — pin framework via csproj `TargetFramework`; never pass `-f net9.0`.
- NuGet restore needs AWS CodeArtifact auth; 401/403 on `Cymulate.*` is an auth issue, not a code issue.
- Test stack: xUnit + Moq + FluentAssertions. ISB build/test layout to be discovered from the ISB repo.
- Harvest permutations from the catalog `name` field, never the `vendor` column.
