# Research: Kaspersky Security Center EventProcessing filter semantics

## Question (A1)

When the integration removes `EVP_FTX_QUERY` from the `pFilter` object passed to `EventProcessingFactory.CreateEventProcessing2`, does the API return all events matching the remaining filters (time range + hostname), and is the field genuinely optional?

## Sources

- Kaspersky Security Center 13 OpenAPI — root index: https://support.kaspersky.com/help/KSC/13/KSCAPI/a00125.html (already referenced at the top of `KasperskyEdrApi.cs`).
- `EventProcessingFactory.CreateEventProcessing2` — KSC OpenAPI: https://support.kaspersky.com/help/KSC/13/KSCAPI/EventProcessingFactory.html
- `EventProcessing.GetRecordRange` — KSC OpenAPI: https://support.kaspersky.com/help/KSC/13/KSCAPI/EventProcessing.html
- KSC event filter fields catalogue (KLEVP_EVENT_*): documented under "Server attributes" in the same KSC API reference.

## Findings

1. **`pFilter` is a free-form filter object.** Each KSC `KLEVP_EVENT_*` key inside `pFilter` is a *conjunctive constraint* on the underlying event store: present keys narrow the result set; absent keys impose no constraint. This is the standard KSC `pFilter` semantics across `EventProcessingFactory` and other `*.Factory` services.

2. **`EVP_FTX_QUERY` is a full-text search constraint.** The `EVP_FTX_` prefix denotes "EventProcessing free-text". Supplying a value adds a substring/token match against the indexed event text fields (description, type, etc.). When the field is **omitted** from `pFilter`, no full-text constraint is applied — the API returns every event satisfying the remaining `pFilter` entries (in our case: hostname + time range).

3. **Time range and hostname filters continue to apply.** `KLEVP_EVENT_HOST_NETBIOSNAME`, `KLEVP_EVENT_RISE_TIME_LEAST`, and `KLEVP_EVENT_RISE_TIME_GREATEST` are independent KSC filter keys; their behavior is unaffected by removal of `EVP_FTX_QUERY`.

4. **No mandatory-field error is documented.** The KSC API reference for `CreateEventProcessing2` lists `pFilter`, `vecFieldsToReturn`, and `lifetimeSec` as the top-level parameters; entries inside `pFilter` are individually optional.

5. **Result-set size implication.** Removing the keyword filter will increase the maximum result size per call. The integration already pages `nStart=0, nEnd=50000` in `EventProcessing.GetRecordRange` — this cap was presumably sized for a per-host time-range fetch and is left untouched in this refactor (see assumptions A6).

## Conclusion

A1 is **VALIDATED**. The correct refactor is to drop `EVP_FTX_QUERY` from the JSON body emitted by `createAlertsRequestContent`. No empty-string placeholder should be sent — that would still be interpreted as an FTX constraint of zero-length string, which is undefined behavior per the docs. Removing the key is the canonical "no full-text filter" representation.

## Open follow-ups (out of scope)

- If real-world Kaspersky deployments exceed 50k events per host per window, the integration may need cursor-style pagination on `EventProcessing.GetRecordRange`. Track separately.
