# Prompt Contract

Role:
You are a principal integrations/distributed-systems engineer auditing a multi-repo execution
path for production readiness.

Goal:
Produce a grounded end-to-end trace of the YamlCollector execution path (backend run message →
ISB → YamlCollectorAdapter → tenable.io collection), a correctness/readiness assessment, and a
detailed flow diagram.

Context:
- Repos: magic=/Users/user/Dev/cymulate-magic-integration (YAML defs, canonical engine
  Platform.Integrations.Sdk, ADRs in docs/adr/); ISB=/Users/user/Dev/IntegrationServiceBus
  (run-message ingress + dispatch); adapters=/Users/user/Dev/cymulate-integration-adapters
  (YamlCollectorAdapter at src/Cymulate.Integration.Adapters/Collectors/YamlCollector/ + engine
  copy Cymulate.Integration.Yaml.Engine).
- Locked direction: ADR-0001 (Magic orchestrates; Direct + ISB routes), ADR-0002 (ISB route =
  in-process YamlCollector adapter; peer bridge retired), ADR-0003 (engine dual-home, Proposed).
- Use case: tenable.io run. Trace phases A (backend run message), B (ISB handling + dispatch to
  adapters), C (YamlCollector pre-collection: selection, YAML resolution, credentials,
  checkpoint/resume), D (collection: auth, export/pagination, batching, S3, events, completion).

Constraints:
- Read-only: no product-code modifications in any repo.
- Every behavioral claim cites file:line evidence from actual source.
- Distinguish confirmed bug / gap / drift / working-as-intended in findings.
- Compare the two engine copies for drift (ADR-0003).
- Record per-repo branch + HEAD commit as the snapshot the assessment applies to.
- Speculation must be labeled.

Success Criteria:
- Phases A–D each answered concretely for tenable.io: exact message shape, exact dispatch
  mechanism, exact adapter-selection logic, exact pre-collection steps, exact collection loop
  including checkpoint, S3 and event emission, and error paths.
- Readiness verdict with an enumerated, severity-ranked findings list (each with evidence).
- Engine-drift status between Platform.Integrations.Sdk and Cymulate.Integration.Yaml.Engine.
- A detailed flow diagram covering A→D incl. error/checkpoint paths, delivered rendered +
  source committed to the task directory.
- Write-up saved to the task directory (execution_notes.md + final report file).

Execution Rules:
- Do not assume missing data; validate assumptions A2–A5 in assumptions.md and update statuses.
- Respect constraints strictly.
- If the trace reveals the flow is not wired at all (e.g., no path from ISB to
  YamlCollectorAdapter), that is a finding, not a blocker — document and continue.

Output Format:
- Final report: markdown, sections = Executive verdict / Phase A / Phase B / Phase C / Phase D /
  Findings (severity-ranked table + prose) / Engine drift / Diagram.
- Diagram: mermaid source in task dir + rendered artifact.

Stop Conditions:
- When all success criteria are met.
- If a repo is missing or unreadable — surface to user.
