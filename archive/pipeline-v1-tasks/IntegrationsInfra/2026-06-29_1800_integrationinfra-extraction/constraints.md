# Constraints

- PLANNING ONLY this run: no structural edits, renames, rewiring, or fat-removal. Read-only analysis only.
- Hard success criterion: single-entry capability bank — an adapter imports the composition layer + `Cymulate.Integration.Sdk` contracts and ZERO mechanics namespaces. If the census shows this cannot be reached cleanly, say so plainly; do not paper over it.
- `Cymulate.Integration.Sdk` v3.2.0 is the immovable contract floor — bind to it, never remove.
- Auth/secrets only via `http.package` mechanisms (proven); no bespoke auth in infra.
- Generic infra — NO vendor identity (vendor catalog / naming belongs to the bus/BE).
- Runtime-plugin deploy model must still hold (NuGet ships inside each adapter's `deps.json` closure).
- Target framework net8.0.
- Do not let an interface sketch precede the usage census (census is the gating artifact).
- No dependency surgery: graph is already acyclic (see decisions D1).
