# Lessons — 2026-08-30_1247_falcon-unmanaged-assets-never-lost (2026-08-30)

## L-29828585 — A1
belief:  AidExtractor.ExtractAid can derive a unique key for a Discover asset that states no sensor AID.
counter: Live full-inventory probe of 303 lab assets: 49 state an AID and the derivation produced a key for 0 of the remaining 254. The 32-hex suffix gate rejects the tenant's 56-char base64url token.
source:  research/lab-tenant-identity-probe.md; AidExtractor.cs:124 (verifier, 2026-08-30) (verifier, 2026-08-30)
verify:  grep -n 'suffix.Length != 32' src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/Findings/Hosts/AidExtractor.cs
do not:  do not re-assume a derived AID keys an unmanaged Discover entity without probing that tenant's combined id shape first

## L-a6917e53 — A3
belief:  Some unmanaged Discover assets already reach output under a derived AID, orphaned downstream.
counter: Zero of the 254 AID-less lab assets were derivable, so none were emitted-but-orphaned; all 254 were dropped outright at the scroller.
source:  research/lab-tenant-identity-probe.md (verifier, 2026-08-30) (verifier, 2026-08-30)
verify:  grep -n 'ExtractRecordKey' src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/Findings/Correlated/FalconDiscoverHostScroller.cs
do not:  do not assume a drop and an orphan population coexist without measuring both

## L-ee097772 — A16
belief:  A Discover combined-id record key is about 65 characters long.
counter: Measured 89 characters on real data: a 32-hex CID, an underscore, then a 56-char base64url token. Every ceiling measurement taken at 65 had to be redone.
source:  execution_notes.md artifact analysis; FalconTwoPhaseFindingsTests LabTenantSampleCombinedId (verifier, 2026-08-30) (verifier, 2026-08-30)
verify:  grep -n 'LabTenantSampleCombinedId' src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test/FalconTwoPhaseFindingsTests.cs
do not:  do not size a buffer, cap or ceiling against an assumed key length; measure the vendor's id on a live tenant

## L-fd80d22b — A17
belief:  Falcon FQL last_seen_timestamp:>= returns every host, so a date-gated Discover scroll loses nothing.
counter: 303 assets unfiltered versus 298 under the gate at both a 2000 and a 2020 floor, so it is null-exclusion and not a date boundary. FQL rejects both :null and :!null with HTTP 400, so no widening recovers them.
source:  research/falcon-fql-null-field-matching.md (verifier, 2026-08-30) (verifier, 2026-08-30)
verify:  grep -n 'BuildLastSeenTimestampGateFilter' src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/SharedFlows/FalconHostFilters.cs
do not:  do not treat a base-dated or re-anchored Falcon run as full-inventory coverage; hosts with a null last_seen are unreachable

## L-2186c9a2 — A8 A15
belief:  An 89-character combined id is safe in parser output columns that have only ever carried 32-character AIDs.
counter: No consumer downstream of the parser was probed. asset_match_key was scoped out of the task, so the join most likely to notice was deliberately not examined.
source:  review/verifier-2.md A8 (verifier, 2026-08-30) (verifier, 2026-08-30)
verify:  grep -rn 'partitionBy("aid")' libs/packages/parsers/deprecated/crowdstrike/CrowdstrikeAssetsFindingsCorrelated.py
do not:  do not assume a widened identity column lands downstream without checking the match key that consumes it

## L-35b88c57 — A4
belief:  The Falcon checkpoint's boundary-key list has a ceiling that fails visibly if it grows.
counter: The collector imposes no cap on that path and neither AdapterCheckpoint.AdapterState nor CommitPage documents one. Measured 460,022 bytes at a full 5,000-key set; it is pinned by a test, not capped.
source:  FalconTwoPhaseFindingsTests R3b_CheckpointCarryingTheSameBoundarySet_HasNoCeilingOfItsOwn_SoItsSerializedSizeIsPinnedHere (verifier, 2026-08-30) (verifier, 2026-08-30)
verify:  grep -n 'R3b_CheckpointCarryingTheSameBoundarySet' src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test/FalconTwoPhaseFindingsTests.cs
do not:  do not assume a control artifact carrying a growable list is bounded because a sibling artifact is

## L-76ed5c8b — A10 A11 A12 A13 A14
belief:  The Discover cursor leg observes a Falcon 404 as an HTTP 404 status line, so its re-anchor arm fires on an expired cursor.
counter: No probe of the 404-under-200 behaviour was run in this task and the changed cursor leg was never exercised against one, so the re-anchor arm remains unproven on the code path this task edited. Four neighbouring beliefs stayed untested for a stated reason rather than an unexamined one: remediation-entity ordering is moot because the fallback key is the combined id verbatim, settings_hash is unreachable while prevention enrichment ships off, and device-entities batch membership is unchanged because it derives from the sensor AID, which is null for every AID-less host.
source:  review/verifier-2.md A10-A14 (verifier, 2026-08-30) (verifier, 2026-08-30)
verify:  grep -rn 'FalconCursorExpiredException' src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/Findings/TwoPhase/FalconHostSpooler.cs
do not:  do not read an absent re-anchor count as evidence the cursor never expired; the vendor can deliver 404 under a 200 header
