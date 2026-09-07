# Task: Shared-substrate decomposition

Carve the transport-fault **floor** out of the Shared project into a new **Kernel** unit, then
present **Session** (Conversation) and **Resilience** (FaultGovernance) as independent peers that
both stand on the floor.

## Why
The floor (`Session/TransportErrorHandling`) is a true dependency leaf that both Session and
Resilience consume, but it currently lives *inside* the Session namespace — implying Resilience
depends on Session when it does not. Relocating the floor to a Kernel unit makes the real layering
explicit: `Session ⊥ Resilience`, both depend only on Kernel.

## Scope
1. New Kernel project/namespace; move the floor (`TransportErrorHandling`) into it. Floor first — it
   unblocks both peers.
2. Move cross-cutting envelope shapes (`CollectorError`, partial-completion DTOs) into Kernel.
3. Reclaim `FlowExceptionHandling` + `AdapterFlowFailureHandling` from `Orchestration` into Resilience
   (FaultGovernance) — they are governance classification types, currently mis-filed.
4. Rewire all importers; keep the solution building and all tests green throughout.

## Pure structural carve
No behavior change. This is relocation + reference rewiring only.
