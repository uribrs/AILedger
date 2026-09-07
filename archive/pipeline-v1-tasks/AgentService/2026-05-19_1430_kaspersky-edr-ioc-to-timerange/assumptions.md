# Assumptions

- A1 [VALIDATED — HIGH confidence]: Removing the `EVP_FTX_QUERY` field from the `pFilter` JSON object in `EventProcessingFactory.CreateEventProcessing2` returns all alerts that match the remaining filters (time range + hostname). `EVP_FTX_QUERY` is a Kaspersky free-text-search constraint applied on top of `pFilter`; omitting it removes the constraint.
  - Two vendor pages directly verified: KSC 13 attribute catalogue `https://support.kaspersky.com/help/ksc/13/kscapi/a00126.html` and KSC 13.2 mirror `https://support.kaspersky.com/help/KSC/13.2/KSCAPI/a00128.html`. Both list `EVP_FTX_QUERY` as `paramString` "Full-text search condition" — no "required" marker — alongside the three keys we keep (`KLEVP_EVENT_HOST_NETBIOSNAME`, `KLEVP_EVENT_RISE_TIME_LEAST`, `KLEVP_EVENT_RISE_TIME_GREATEST`), each with matching type and description. The full-text DSL doc `a00002.html` describes FTX as an optional search mechanism layered on top of the structured filter.
  - Independent client confirmation: `pixfid/go-ksc` (third-party Go SDK) serialises `pFilter` with `json:"pFilter,omitempty"` and exposes a sibling `EventProcessingFactory.CreateEventProcessing` (no `2`) that takes no filter at all — proving both `pFilter` and any key inside it are protocol-level optional.
  - Inferred (not a vendor sentence): the conjunctive (AND) combination rule for `pFilter` keys. Caveat documented in `research/kaspersky-edr-event-processing-verified.md`. Justified by KSC's RFC2254-style filter-syntax doc and the integration's prior production behaviour, but the PR description should not claim docs verbatim assert AND combination.
  - Primary source file: `research/kaspersky-edr-event-processing-verified.md` (live-vendor research, written by a research subagent). `research/kaspersky-edr-event-processing.md` is the original inference-only doc, kept for comparison.

- A2 [VALIDATED]: `eQueryResultTypes.Alert` remains the only result type supported by this integration (consistent with the base ctor passing `[eQueryResultTypes.Alert]`). The refactor does not add `Event` or `Incident` support.

- A3 [VALIDATED]: `QueryByTimeRangeGroupModel` carries `Hostname`, `DateTimeRange`, `PrivateIps`, `QueryResultType`, `QueryIds`, `IsCustomQuery`, `ResultsLimit` — sufficient to populate the Kaspersky request without the IOC value. Source: `Source/.../Models/CustomQuery/Models/QueryGroups/QueryGroupBase.cs` and `QueryByTimeRangeGroupModel.cs`.

- A4 [VALIDATED]: `KeywordRequestDetailsModel.Query` is a logging/diagnostic field. Setting it to empty string when no IOC keyword is used is acceptable (Humio precedent did the same — see `ai/active/2026-05-18_1713_humio-ioc-to-timerange/decisions.md`).

- A5 [VALIDATED]: No Kaspersky tests exist under `Tests/`. `find Tests -iname "*kaspersky*"` returned zero results. Test creation is therefore out of scope.

- A6 [VALIDATED]: The pagination range `nStart=0, nEnd=50000` in `writeAlertsToCacheAsync` was already sized for a time-range fetch (50k records) rather than per-IOC. The refactor does not change pagination; if the configured time-range window can exceed 50k events on a busy host, that risk pre-dates this PR and is tracked separately.
