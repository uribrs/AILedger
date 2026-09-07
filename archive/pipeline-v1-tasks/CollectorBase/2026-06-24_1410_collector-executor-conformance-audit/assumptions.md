# Assumptions

- A1 — VALIDATED: The native collectors and YamlCollector + the Shared library are present and readable in the adapters monorepo; the same Shared code is present in the CollectorBase repo under Shared/. (Confirmed throughout the prior task.)
- A2 — VALIDATED: CortexXdr collector exists in the adapters repo (a cortex-xdr.yaml profile exists; the native collector should be present). If the native CortexXdr collector is absent, the baseline uses the other four — note it rather than block.
- A3 — OPEN: Whether the just-asserted conformance is fully correct in code is exactly what this audit tests; treat all prior claims as unverified until re-checked against source.
