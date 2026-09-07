# Parser Output Exposure Duplicate Key Mitigation

Investigate where this error can originate in the code:

`Error Category: UNCLASSIFIED_ERROR; An error occurred while calling o1411.jdbc. ERROR: duplicate key value violates unique constraint "parser_output_exposures_pkey"`

Determine the exact mitigation point for Spark retry behavior so a retried batch is not lost when the target object already exists in Postgres.

The desired behavior is:

- if the object being written is identical to the persisted row, treat the write as already successful
- if the object differs from the persisted row, replace the persisted row with the current object
- keep the mitigation scoped to this specific issue unless investigation proves the same contract is needed elsewhere

This task is documentation and execution planning only. Do not modify product code until explicitly instructed.
