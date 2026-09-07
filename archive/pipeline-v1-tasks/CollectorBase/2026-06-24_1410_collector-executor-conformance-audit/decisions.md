# Decisions

- Native collectors are the reference of record; YamlCollector and CollectorExecutor are both measured against them (CollectorExecutor is not graded against its own prior task notes).
- One cross-verifier covers BOTH YamlCollector and CollectorExecutor (single comparison frame, native baseline).
- Three independent code-reviewers (not one) over the CollectorExecutor implementation, isolated/minimal-context, for coverage and diversity per the user's explicit request.
- Read-only: any must-fix is reported for separate approval, never applied in this task.
- Decompose path (the four parts are separable: baseline reading, cross-verification, 3 reviews, synthesis).
