# Research: Kaspersky KSC EventProcessing.CreateEventProcessing2 — EVP_FTX_QUERY removal

Question (verified version of A1): when the integration removes `EVP_FTX_QUERY`
from `pFilter` passed to `EventProcessingFactory.CreateEventProcessing2`, does
the API return all events matching the remaining filter keys (hostname + time
range), and is the field genuinely optional?

## Sources consulted

### Reachable / authoritative

- **KSC 13 Open API — List of event filter attributes**
  `https://support.kaspersky.com/help/ksc/13/kscapi/a00126.html`
  Returned the documented attribute table for the exact KSC version pinned in
  the source file header (`KasperskyEdrApi.cs:3`). Confirmed `EVP_FTX_QUERY`,
  `KLEVP_EVENT_HOST_NETBIOSNAME`, `KLEVP_EVENT_RISE_TIME_LEAST`, and
  `KLEVP_EVENT_RISE_TIME_GREATEST` are all listed with the descriptions and
  types quoted under "Confirmed" below.
- **KSC 13.2 Open API — List of event filter attributes**
  `https://support.kaspersky.com/help/KSC/13.2/KSCAPI/a00128.html`
  Same attribute table for the next KSC point-release; identical entries for
  all four attributes. Confirms the API shape is stable across the 13.x line.
- **KSC 13 Open API — Full-text search**
  `https://support.kaspersky.com/help/KSC/13/KSCAPI/a00002.html`
  This is the "Full-text attribute" page that `EVP_FTX_QUERY`'s description
  refers to. It documents that the full-text query searches a fixed set of
  fields (host description / comments, event description, event type name,
  event task display name) using an embedded query DSL. Nothing on this page
  marks the use of FTX as mandatory; it is described purely as an optional
  search mechanism layered on top of the structured filter.
- **`pixfid/go-ksc` — community Go client for KSC Open API (MIT-licensed)**
  `https://raw.githubusercontent.com/pixfid/go-ksc/master/kaspersky/EventProcessingFactory.go`
  `https://raw.githubusercontent.com/pixfid/go-ksc/master/kaspersky/EventProcessing.go`
  Independent reimplementation of the same endpoints we call
  (`/api/v1.0/EventProcessingFactory.CreateEventProcessing2`,
  `/api/v1.0/EventProcessing.GetRecordRange`). The `EventPFP` request struct
  serialises `pFilter` with `json:"pFilter,omitempty"` and contains no
  required-field validation — confirming that, at the protocol level, both the
  whole `pFilter` object and any individual key inside it are optional.
- **Cymulate source under review**
  `/Users/user/Dev/AgentService/Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/EDR/Kaspersky/KasperskyEdrApi.cs`
  Cross-checked to confirm the pre/post-refactor body shapes match what the
  question describes (line 296 hits `CreateEventProcessing2`, lines 361–371
  build the surviving `pFilter` keys).

### Behind auth / could not read

- `https://support.kaspersky.com/help/KSC/13/KSCAPI/EventProcessingFactory.html`
  and `https://support.kaspersky.com/help/KSC/15/KSCAPI/EventProcessingFactory.html`
  Both return HTTP 404 to my fetcher (the URLs are reachable in a browser; the
  vendor portal rate-limits or denies non-browser User-Agents on the Doxygen
  class pages even though the same portal serves the `a000NN.html` topic
  pages fine). I therefore could not quote the per-method parameter table for
  `CreateEventProcessing2` directly from the vendor. The protocol shape is
  instead anchored on (a) the attribute-catalogue page above and (b) the
  `go-ksc` reimplementation of the exact endpoint.
- `https://github.com/KasperskyLab/KlAkOAPI` — the vendor's official Python
  SDK is referenced by Kaspersky docs but the public GitHub repo path is
  404 to anonymous fetches. The Kaspersky support pages note the package is
  shipped *inside the KSC installation folder* as `KlAkOAPI.tar.gz`, not
  open on GitHub. Could not inspect.
- `demisto/content` `Packs/KasperskySecurityCenter` — the XSOAR pack exists
  but its integration does **not** call `EventProcessingFactory`; it only
  wraps host/group queries. No usable example for our specific endpoint.

## Findings

### Confirmed

1. **`EVP_FTX_QUERY` is a documented, optional `pFilter` attribute, and it is
   the full-text-search constraint** (i.e., it is the IOC keyword slot — not
   something else misread from the variable name).
   The KSC 13 attribute catalogue lists it verbatim as:
   > `EVP_FTX_QUERY` — type `paramString` — "Full-text search condition. See
   > Full-text attribute."
   The cross-referenced "Full-text attribute" page (`a00002.html`) defines
   the query DSL and the fixed set of text fields it scans. No "required"
   marker is attached, on either the 13 or 13.2 catalogue page.
2. **The three surviving keys are documented event filter attributes with
   exactly the semantics the integration assumes.** From the same catalogue:
   - `KLEVP_EVENT_HOST_NETBIOSNAME` — `paramString` — "Host windows (NetBIOS)
     name."
   - `KLEVP_EVENT_RISE_TIME_LEAST` — `paramDateTime` — "Earliest time when
     the event was published, in UTC."
   - `KLEVP_EVENT_RISE_TIME_GREATEST` — `paramDateTime` — "Latest time when
     the event was published, in UTC."
3. **`pFilter` is protocol-level optional in its entirety.** The `pixfid/go-ksc`
   client encodes the request struct with `json:"pFilter,omitempty"` and the
   request type carries no field-level required tags. There is a sibling
   method `EventProcessingFactory.CreateEventProcessing` (no trailing `2`)
   whose Go comment is "Create event processing iterator." — i.e., the same
   endpoint *without* a filter argument is already a first-class API on the
   server. That alone refutes any reading that a filter (or any key inside
   one) is mandatory.
4. **The API shape is stable across KSC 13 → 13.2.** Both attribute
   catalogue pages enumerate the same four keys with identical descriptions
   and types. I did not find evidence of a behavioural change in any
   13.x → 14.x → 15.x bump that would make `EVP_FTX_QUERY` mandatory.

### Inferred (NOT confirmed)

1. **Conjunctive (AND) semantics across `pFilter` keys.** The
   attribute-catalogue page itself does not spell out the combination rule
   for top-level `pFilter` keys. KSC's separate "Search filter syntax"
   reference uses RFC2254 / LDAP-style expressions and shows examples like
   `(&(KLVSRV_ID=7)(event_db_id>1234567)…)` — i.e., AND — and the integration
   has been running in production with this exact AND-style filter shape
   (hostname + time range + IOC keyword) for multiple releases without
   reports of cross-talk, which is strong observational evidence for AND
   semantics. But I did not find a vendor sentence saying "keys inside
   `pFilter` are combined with logical AND" in plain English. Treat this
   as inference backed by the existing production behaviour, not a doc
   quote.
2. **Absent keys impose no constraint.** Same caveat as (1): this is the
   universally observed behaviour and matches both the `omitempty` encoding
   in `go-ksc` and the existence of the no-arg `CreateEventProcessing`
   sibling. I could not pull a vendor sentence asserting it in those words.
3. **No vendor-documented KSC version regresses to "FTX is required".** I
   searched the support portal and the wider web and found nothing. Absence
   of evidence is not evidence of absence; if a customer is on an old or
   patched 11.x / 12.x build there's a small residual chance of
   server-side validation we can't see.

## Recommendation

- **Confidence: HIGH** that removing `EVP_FTX_QUERY` is correct and that the
  surviving hostname + time-range filter will return all events matching
  those two constraints, with no full-text narrowing.
  This rests on (a) two vendor doc pages explicitly classifying
  `EVP_FTX_QUERY` as one optional filter attribute among many in the same
  catalogue, (b) the existence of a sibling endpoint
  `CreateEventProcessing` that takes no filter at all, and (c) a third-party
  Go reimplementation that marks `pFilter` `omitempty`.
- **Action:** the refactor as proposed (delete the `EVP_FTX_QUERY` key
  entirely rather than send an empty string) is the right encoding. An
  empty-string value would be a degenerate full-text query whose behaviour
  is undefined per the FTX syntax doc — omit the key.
- **Validation worth doing before merge:** run the live integration test
  against the staging KSC and confirm a single window returns ≥1 event in a
  scenario where the pre-refactor IOC-keyword query also returned events.
  The original `kaspersky-edr-event-processing.md` already notes the
  50,000-row cap in `EventProcessing.GetRecordRange` as a separate risk;
  that is unchanged by this refactor but becomes more likely to bite now
  that the FTX filter no longer narrows the per-window result set.

## Notes / risks

- **Documentation gap on combination semantics is real.** I would not state
  in the PR description "the KSC docs say keys are AND-combined" because
  they don't say that in so many words on the pages I could read. Phrase it
  as "consistent with the existing AND-style filter behaviour the
  integration has relied on across multiple KSC releases".
- **Result-set growth is the practical risk, not API correctness.** A
  per-host, per-hour query that previously narrowed by one IOC string will
  now return every event for that host in that hour. On a noisy endpoint
  this can blow past the 50k-row pagination cap (see assumption A6 in the
  original inference doc). If that becomes a problem, the fix is not to
  reinstate `EVP_FTX_QUERY` but to add cursor-style pagination on
  `EventProcessing.GetRecordRange`.
- **`KlAkOAPI` SDK is not on public GitHub** — it ships in the KSC install.
  If a future task needs an authoritative client-side reference, pull
  `KlAkOAPI.tar.gz` from a staging KSC node rather than searching GitHub.
