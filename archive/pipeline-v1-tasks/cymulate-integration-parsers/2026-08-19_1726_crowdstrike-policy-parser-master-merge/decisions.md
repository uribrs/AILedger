# Decisions

- Treat `master` as authority for current parser and PostgreSQL architecture.
- Treat the feature branch as authority for the prevention-policy payload contract.
- Proceeding on unverified: PostgreSQL may need explicit mapping changes. If wrong: limit changes to tests proving existing generic JSON handling is sufficient.
- Commit the resolved merge only after verification and review pass.
