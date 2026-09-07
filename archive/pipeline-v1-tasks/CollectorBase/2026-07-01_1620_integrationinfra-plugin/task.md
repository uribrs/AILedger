# Task: Plug IntegrationInfra into CollectorBase (replace deleted Shared)

The `Shared` project was deleted from CollectorBase. Its solution entry + 4 consumer ProjectReferences now
dangle. Rewire all consumers to the `Cymulate.IntegrationInfra` NuGet (1.0.0-preview.2, local feed) via
namespace remaps + type renames, and drive CollectorBase.slnx to a clean build + passing CollectorExecutor
tests. This is the first real end-to-end exercise of IntegrationInfra by a real consumer.

CollectorBase is NOT a git repo — the deliverable is a green build + passing tests, not a commit.

## Consumers (5 projects)
CollectorExecutor, Runner, Strategies (references CollectorExecutor → package inherited transitively),
Tests/Collectors.Tests.Infrastructure, Tests/CollectorExecutor.Test. 22 `.cs` files reference Shared.

## Shape of the change
- Solution/csproj plumbing: drop Shared (slnx + 4 ProjectReferences), add the IntegrationInfra package
  (+ central version, + local nuget feed), bump Cymulate.Integration.Sdk 3.1.8→3.2.0 (package's floor).
- Code: mechanical namespace remaps + type renames across the 22 files (no logic changes). Full map in
  `prompt_contract.md` / `decisions.md`.

## Non-negotiable
Do NOT edit IntegrationInfra (merged + packed). If a Shared type the consumers use turns out to be missing
from IntegrationInfra (a gap beyond the already-carried contracts), STOP and surface it — do not work around it
in CollectorBase.
