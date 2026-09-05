# Constraints

- Use `AILedger_2_Design_Dossier_v2.1.docx` as the primary design source.
- Use `/Users/user/Dev/AILedger` at commit `5bdb627` as the authoritative AILedger 1.x source.
- Copy the six canonical pipeline skill directories verbatim before codifying any of their rules.
- Preserve copied skill files as a readable cognitive layer.
- Implement in .NET and follow the repository AGENTS.md conventions.
- Keep methods small and responsibilities close to their owning feature.
- Do not introduce abstraction without a demonstrated boundary.
- Keep the initial persistence local and inspectable.
- Ensure one component owns authoritative writes.
- Treat Codex and Claude as provider adapters rather than domain concepts.
- Support real local Codex and Claude launch or resume paths using verified host capabilities.
- Do not add provider-native swarms, automatic implementation, LangGraph, a generic policy language, or hard filesystem enforcement in this slice.
- Do not weaken the existing task-orchestrator delegation, verifier, or isolated code-reviewer semantics.
- Do not modify the authoritative `/Users/user/Dev/AILedger` source repository as part of product implementation.
- Preserve the two dossier files already present.
- Do not commit or push unless explicitly requested.
