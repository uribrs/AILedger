# W0 — collector and S3 evidence

## Result

**PASS.** The live local Falcon findings run completed successfully and published
the parser-facing object to the configured S3 prefix.

- correlation ID: `a2d4279b-38dd-4293-a66f-7496d402f012`
- terminal result: success
- generation: `gen_08defea9bad06a70`
- staged hosts: 47 in one page
- final findings reported: 124,983
- final records/chunks: 91 across 47 distinct hosts
- final object bytes: 324,046,289
- final SHA-256:
  `98dc0daf5a515e8a8d7c340c61d95bfd1e4efb029a86dc658371da55daecc48e`

The S3 prefix contained the staging host page, manifest, and
`findings_000001.json`. The manifest recorded policy enrichment enabled with
policy envelope schema version 1. All 47 staged wrappers already carried an
enriched `host.device_policies` envelope; the enriched staged page is the frozen
authority consumed by final correlated publication.

All 91 final records carried `host.device_policies` with:

- `collection_status=complete`: 91
- `prevention.definition_status=resolved`: 91
- missing policy envelopes: 0
- different policy content across repeated chunks for one host: 0

The sum of `findings[].length` and `findingsInChunk` both equaled 124,983, with
zero per-record count mismatches.

The object exceeded the configured 50 MiB soft warning threshold, but the runner
published it atomically and emitted the successful terminal event. This is an
operational sizing observation, not a correctness failure.

Sensitive payload copies remain only under
`/private/tmp/falcon-policy-e2e-20260820` and were not copied into task evidence.
