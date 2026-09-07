# Task: Unify adapter identity resolution in IntegrationServiceBus

## Problem
ISB resolves which adapter to run from an inbound message `vendor` string. That resolution is scattered across several maps that drift and contradict each other, so a correctly-deployed, correctly-registered adapter can fail to match. A real staging message (`vendor: "InsightVM Cloud"`, topic `collector`) was DLQ'd even though the InsightVM Cloud collector was registered and healthy.

## Goal
One total, idempotent resolver: `(inbound name, topic) -> (PlatformType, Category)`. Every ISB resolution site delegates to it; the duplicate/contradictory maps are retired. A contract test keeps the ISB resolver and the adapter repos aligned.

## Scope
- Primary: IntegrationServiceBus repo — introduce the single resolver; simplify `ProductPlatformMapper` and `VendorPlatformResolver` to delegate; converge `RegisteredAdapterPlatformResolver` (incl. its `FallbackPlatforms`) and the other resolution sites; retire duplicate maps.
- Add a guarantee test asserting every live-catalog integration `name` resolves to a `(PlatformType, Category)` that a registered adapter actually serves.
- Secondary: adapters repo changes only if the audit proves a real mismatch — and only on a fresh branch.

## Out of scope
- Replacing the `PlatformType` enum with a different key type.
- Changing the `ClientId` (multi-tenant) registry axis.
- Adapter runtime/business logic beyond identity declaration.

## Identity model (locked)
- Adapter identity = `(PlatformType, Category)`. Neither name, PlatformType, nor brand alone is sufficient.
- Resolution is keyed on name AND topic jointly.
- `PlatformType` (shared SDK enum) stays the canonical token; cleaned only where ambiguous.
