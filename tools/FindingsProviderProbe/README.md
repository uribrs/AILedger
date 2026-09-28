# Disposable provider acceptance probe

This explicitly invokes a real provider and consumes usage. It creates a new isolated ledger and
Git workspace outside the checkout, launches one researcher through the production CLI, revokes
AddEvidence from the trusted test process after the stable-key retry, and checks canonical state,
transport observations and the single provider completion. It does not govern development.

Build the CLI outside the checkout, then run each provider with an unused output directory:

```sh
python3 tools/FindingsProviderProbe/run.py claude --cli /tmp/ailedger-task4-build/bin/AILedger.Cli/debug/AILedger.Cli.dll --output /tmp/findings-claude-trial
python3 tools/FindingsProviderProbe/run.py codex --cli /tmp/ailedger-task4-build/bin/AILedger.Cli/debug/AILedger.Cli.dll --output /tmp/findings-codex-trial
```

Authentication and supported local clients must already exist. The script changes no global
configuration, installs nothing, and supplies no approval bypass. The disposable actor has
BuildContext, AddClaim and AddEvidence; after revocation it retains the first two. The stage waiver
belongs only to this acceptance fixture. The genuine production launch creates/ends the run and
captures its original manifest and usage. Logs, ledger and acceptance.json remain under `--output`.
An exception/nonzero exit is a failed probe, not provider acceptance. Review launch.err/out when
that occurs; do not broaden permissions to make it pass. Run this only with explicit authorization
for live trials. Ordinary automated tests use real local MCP with scripted external model output.

The probe checks file spoofing using an agent-written host.json and verifies that it cannot change
the host's researcher binding. Direct OS-level ledger authority remains outside this MCP boundary.
The transcript can include ordinary permission denials (for example a shell polling loop); those
are independent of the record_findings grant. Inspect the actual transcript if claims about tool
interaction counts, protocol versions or permission outcomes beyond this probe are needed.
