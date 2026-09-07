* Treat this as a FalconCollector behavior change with narrowly scoped shared changes only if required by existing checkpoint contracts.
* Design checkpoint handling around a composite recovery state: existing next cursor plus current month segment.
* Preserve old-to-new paging within a segment only if Falcon API semantics require it; the required ordering applies to month segments.
* Segment fresh assets runs and unfiltered findings runs only when the host supplied an explicit base date.
* Keep legacy/no-base-date runs unsegmented to preserve default lookback behavior and existing checkpoints.
* Keep fresh AID-scoped findings runs on the existing bounded prepass path to avoid refetching the same host/AID set once per month.
