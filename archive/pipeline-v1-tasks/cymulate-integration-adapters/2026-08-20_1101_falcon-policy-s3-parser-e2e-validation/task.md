# Falcon policy S3-to-PostgreSQL validation

Validate one real local Falcon collection end to end: establish its terminal result, inspect the actual S3 artifacts, run the merged CrowdStrike parser against those artifacts when safely possible, verify policy persistence in PostgreSQL, and confirm the receiving contracts present on the master branches of the database-model and Exposure Analytics repositories.

Produce an evidence-backed readiness verdict without changing product code or the receiving repositories.
