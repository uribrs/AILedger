# Constraints

- Behavior preserved verbatim — esp. RunPayloadCredentialHydrator's exact behavior (untyped Dictionary
  output + exception-swallowing). Namespace-only rewrite + the rewires + the D8 renames. No logic change.
- Source repo `/Users/user/Dev/Uri/localprojects/IntegrationsInfra` is reference-only — do not mutate.
- Kernel AND the merged Emission (AdapterGlobalDefaults) are not modified by this carry. Job depends on
  them, never the reverse.
- D8 naming: neutralize the listed Collector* type names → Adapter*. JsonPropertyName VALUES unchanged.
  Wire-safety: verify NO type-name-based deserialization ($type / JsonDerivedType) before renaming a
  deserialized envelope type; if one is on the wire, leave that type un-renamed and STOP/surface.
- No parse→typed-job reshape of the hydrator (verbatim only; flag the seam).
- Do NOT move DefaultLookbackDays out of Emission (that would change merged Emission — flag as reshape).
- Outbound/Reporting shapes are NOT carried here.
- net8.0; build clean + tests pass; XML docs on every public member; no vendor identity introduced.
