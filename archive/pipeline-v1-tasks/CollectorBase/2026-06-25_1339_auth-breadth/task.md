# Task: CollectorExecutor auth breadth

Extend CollectorExecutor's declared-auth coverage so more native vendors can be expressed as
pure YAML. Next item on the parity gap table — chosen because it unlocks the most vendors
(Guardicore, SentinelOne, ServiceNow) for the least code.

## Where the work happens
`CollectorExecutorSessionFactory.BuildAuthSelection` (`CollectorExecutor/Execution/CollectorExecutorSessionFactory.cs:36`)
— NOT the runner (refactored out). Today it has 4 cases + `default: return null`:
`oauth2_client_credentials`, `api_key`, `basic`, `hmac`.

## The extension rule (hard constraint)
A new auth flow = ONE more `case` mapping a declared auth `type` (non-secret shape, from YAML)
to an `http.package` `AuthSelection`. Secret VALUES come from dispatch `credentials` by
convention, never from YAML. No vendor identity in the engine; no bespoke transport.

## Deliverable
- BuildAuthSelection case(s) ONLY for auth types that (a) a target vendor actually uses AND
  (b) Shared's `AuthSelection` can already express.
- At least one previously-unexpressible native vendor turned into a working YAML profile, auth
  verified against native source (file:line).
- Tests per new auth type (correct AuthSelection / probe authenticates; bad creds fail).
- `auth-note.md`: declared-type -> AuthSelection mapping, per-vendor auth trace (file:line),
  explicitly-excluded set (e.g. AwsSigV4) with reason.
- Anything Shared cannot express is REPORTED as a gap, not hacked in.
