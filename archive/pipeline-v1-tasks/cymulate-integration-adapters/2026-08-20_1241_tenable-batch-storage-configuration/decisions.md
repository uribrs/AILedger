# Decisions

- Keep this change within the TenableIo collector/test cluster.
- Mirror the existing InsightVM Cloud and Qualys configuration-property shape with a default of `false`.
- Preserve the current flat behavior by passing configuration rather than a literal.
- Proceeding on unverified: existing local tests can observe configuration propagation without vendor connectivity. If wrong: stop at the missing seam rather than redesigning the collector.
- Run independent verification and isolated code review.
