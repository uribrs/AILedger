add a single-provider relaxation:
start by checking provider availability at entry, stated confirmation "provider available: {x}, {y}, beginning"
if only one available, a single agent continues the flow. it's basically like the original pipeline did work, anyway

The important thing is that single-provider mode should be an explicit degraded mode, not an implicit exception. Entry preflight says what exists, records it, and the workflow derives its verification policy from that fact. That preserves provenance instead of pretending cross-provider verification happened.