# Mapping stage: manifest-described records to target-shaped rows

Add the mapping stage to the Node manifest-driven normalizer in this repo.

Input: records as the existing generic reader yields them — `{lineNumber, recordIndex, parentKey,
parent, record}` — plus the lane manifest that describes them.

Output: rows shaped for `integration.parser_output_assets_enrich` and
`integration.parser_output_exposures_enrich`. That column set is the promotion boundary, not
today's staging shape.

The mapping must be data applied by a generic engine. Adding a vendor must not edit engine code.
Each mapping declares which manifest fields it reads, so the engine can verify it against the
manifest before reading a single record.

Deliverable is target-shaped rows plus measured numbers. No Postgres write, no create-entities
call. Production grade is not required.
