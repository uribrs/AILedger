* VALIDATED: Month segments are calendar months, not rolling 30-day windows.
* VALIDATED: The effective end date is the collector run date/current host collection upper bound because the existing Falcon request model does not carry a specific end date.
* VALIDATED: Segment boundaries use the same UTC normalization already used by FalconCollector for host-provided base dates.
* VALIDATED: If a run starts with a base date six months back, the first segment is the current/latest partial month, then each prior calendar month down to the base-date month.
* VALIDATED: Existing checkpoints without a month segment remain readable and fall back to legacy unsegmented resume behavior.
* OPEN: The typo example `Mar2` means `Mar26`.
