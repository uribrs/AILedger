# Decisions

- ValidateConfigurationAsync becomes a real connection test (auth + capped probe); never unconditional Success; fail-closed on no yaml.
- Config (yaml + creds) is captured via SetConfiguration and retained, mirroring YamlCollector.
- Validation wired on both the adapter method and the bus-entrypoint delegate.
- dry-run is honored by reading IsDryRun off the request the bus already passes (stop discarding it), threaded through to the runner.
- dry-run is a real-but-capped run (auth→request→map→publish, page_size→1, first emitting page only) — a true end-to-end smoke test, not a no-op.
- dry-run does not persist a resumable checkpoint.
- Reuse Shared session/auth/egress + failure classifier; runner stays thin; no vendor branches.

## Post-review decisions
- Probe issues the first step's request (real auth+endpoint validation) rather than "auth-only for mutating verbs": api_key/hmac sessions do no token fetch, so skipping the request would make Tenable/Cortex (api_key/hmac + POST-first) ALWAYS pass — reintroducing the false-"valid" bug. Real validation beats a side-effect-free probe that can't catch bad creds. Accepted cost: an export-job vendor's probe creates one throwaway export at setup time.
- `ValidateConfigurationAsync` wraps the probe in a broad catch → `Failure("PROBE_ERROR")` (rethrowing cancellation): a connection test must never throw to the platform (e.g. malformed base_url → UriFormatException).
- Only the adapter interface `ValidateConfigurationAsync` is the real probe; the bus-entrypoint delegate stays a lightweight stub (it's the collection path, not the setup-time validate entry — don't probe on every collection).
