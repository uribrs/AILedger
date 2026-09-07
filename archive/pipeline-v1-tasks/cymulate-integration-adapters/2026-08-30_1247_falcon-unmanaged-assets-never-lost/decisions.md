# Decisions

Operator-set, not open for re-litigation:

- The premise is fixed: a collector gets ALL of the tenant's assets unless the user supplied an FQL filter. An asset Falcon returned and the collector dropped is a defect, not a design choice.
- Restore the pre-July principle using the EXISTING S3 two-phase spool. The spool is not the problem and its design is not in scope.
- Emit AID-less hosts with a UNIQUE record key. Not a null AID, not an empty string, not a shared sentinel.
- **AMENDED 2026-08-30, after `research/lab-tenant-identity-probe.md`.** The key is NOT `AidExtractor.ExtractAid`'s derived value. That derivation yields nothing for 254 of 254 AID-less assets on the lab tenant, because `TryExtractAidFromCombinedHostId` gates on a 32-hex suffix and the tenant's suffix is a 56-char base64url token. The key is the Discover combined `id`, present 303/303 and unique 303/303.
- The combined `id` is a FALLBACK AFTER `ExtractAid`, never a replacement for it — `ExtractAid(host) ?? <combined id>`, in that order. A host that gets a key from the 32-hex fallback today must keep that same key, or every such host on a tenant where that fallback fires is silently re-identified downstream (recon L2, `AidExtractor.cs:39-41` records 333 of 374 on another live tenant).
- Do NOT relax the 32-hex gate in `TryExtractAidFromCombinedHostId`. Widening it would mint keys from suffixes that mean something else on other tenants (recon L12).
- The wire shape does not change. `aid` stays the record key; what changes is that a key is always producible.
- The truncation fix ships regardless of how the drop fix lands. They are independent defects that happen to meet at the same line.
- The parser claim is settled by execution against a real artifact. A read of `Window.partitionBy` is not evidence.
- A defensive, non-silent guard in the parser is in scope. A change to its output contract is not.
- `EnablePreventionPolicyEnrichment` stays `false`. This task does not touch that flag or that branch's PR.

Proceeding on unverified beliefs:

- Proceeding on unverified: `AidExtractor.ExtractAid` yields a unique value for every AID-less Discover asset at full tenant scale (A1, A2). If wrong: two assets collide on one derived AID and the parser silently keeps one — the exact failure mode this task exists to eliminate, reintroduced in a harder-to-see place. Mitigation: a collision must be detected and surfaced, never absorbed.
- Proceeding on unverified: no Spotlight-side site other than `SeedAccumulators` assumes a non-null AID (A5). If wrong: a null-AID host reaches a site that throws, and a run that previously succeeded now fails outright — a worse outcome than the drop.
- Proceeding on unverified: the staged page size increase from carrying ~6× the assets stays under the 8 MiB in-memory write ceiling (A4). If wrong: `DataPipelineException`, which this codebase has already been bitten by twice on this exact guard.
- Proceeding on unverified: returning the vendor's `after` unconditionally cannot loop forever (A9). If wrong: an unbounded scroll, which is worse than a truncated one.
