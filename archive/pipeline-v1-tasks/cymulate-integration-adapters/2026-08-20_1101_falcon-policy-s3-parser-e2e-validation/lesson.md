# Lessons — 2026-08-20_1101_falcon-policy-s3-parser-e2e-validation (2026-08-20)

## L-f3058a37 — A7 — Policy edge keys are AIDs; EA resolves hostnames/IPs
belief:  The CrowdStrike parser's policy edge keys resolve against Exposure Analytics' asset match key, so policy-to-asset edges land.
counter: Real-artifact replay produced 47 distinct AID edge keys against 43 distinct asset values with zero matches. EA joins a.value = de.asset_match_key, while the CrowdStrike parser emits hostname/current IP as the asset value — two different identity domains behind type-compatible columns.
source:  review/verifier-1.md, Criterion results (Edge semantic compatibility); policy.repository.ts:784-800 (verifier, 2026-08-20)
verify:  Replay the parser on a real artifact, then compare: select count(*) from cybi.asset_policy ap join cybi.asset a on a.value = ap.asset_match_key — a zero count reproduces the defect.
do not:  Do not read physical schema compatibility as identity-domain compatibility. Check the join key's value domain, not just its column type.

## L-5de41efb — A7 — Rule values persist double-encoded as JSON strings
belief:  rules[].value reaches Postgres as native JSON, so Exposure Analytics' JSON field extraction reads toggle and slider values.
counter: All 939 rule values persist as JSONB strings containing encoded objects. Native extraction returned toggle 822/0 enabled, slider 8/0 detection, ml_slider_pair 109/0 detection — every consumer projection empty.
source:  review/verifier-1.md, Criterion results (Rule JSON semantic compatibility) — independent PostgreSQL expansion of 939 rule values, all jsonb_typeof=string (verifier, 2026-08-20)
verify:  select distinct jsonb_typeof(value) from cybi.security_policy_rule — 'string' reproduces the defect, 'object' is correct.
do not:  Do not treat a successful JDBC write as proof the payload is consumable. Assert jsonb_typeof on nested values before calling a persistence path verified.

## L-bd5a48b3 — A8 — EA deployed revision never queried
belief:  The Exposure Analytics revision running in production was confirmed to contain the policy ingestion path.
counter: Only source presence at origin/master 32c6f8ed was proven, plus historical DB snapshots that evidence schema rather than the deployed binary. The live deployed revision was never queried.
source:  review/verifier-1.md, Criterion results (Deployed-version boundary, PASS bounded) (verifier, 2026-08-20)
verify:  Query the running EA deployment's image tag in the target cluster and compare it against 32c6f8ed3c6f172ae1668d5b3a99ac970037fc7f.
do not:  Do not treat colleague confirmation plus schema snapshots as proof of a deployed revision.
