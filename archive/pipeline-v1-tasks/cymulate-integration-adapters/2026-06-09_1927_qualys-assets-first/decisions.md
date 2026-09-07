# Decisions

- Keep the single combined feed `{HOST_DETAILS, DETECTION_LIST}`; left-join detections onto the host spine. (Parser is built around one feed → assets+findings.)
- No separate assets flow / `IAssetsCollectorAdapter`. (No parser/pipeline exists for it; would be larger and divergent.)
- Host list is the uniform asset spine for ALL hosts; detection HOST_DETAILS not used as a second asset source. (Avoids two asset shapes; host list is a superset.)
- Stream host list page-by-page; never hold all host details. (K8s bounded-memory requirement.)
- SPARK fix = decouple asset extraction from explode (assets pre-explode + dedup; findings inner-explode), not `explode_outer`. (Avoids junk null-detection findings.)
- Apply the change to BOTH collectors for parity, treating each as independent code despite shared design.
- FQDN: derive from `DNS_DATA.FQDN` with `DNS` fallback — in-scope-optional, pending user confirmation if it widens parser risk.
- Build parser changes against the live probe fixtures as the source-of-truth shape.
