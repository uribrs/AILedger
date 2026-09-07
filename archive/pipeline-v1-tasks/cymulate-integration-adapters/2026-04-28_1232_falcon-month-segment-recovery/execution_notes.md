* Read FalconCollector request construction before choosing where to split dates into month segments.
* Read existing checkpoint/recovery serialization and deserialization before adding fields.
* Check for existing tests around FalconCollector pagination, checkpointing, and recovery before adding new cases.
* Validate whether request date range fields are inclusive or exclusive before implementing segment boundaries.
* Added calendar month segment helper using newest-first UTC ranges with inclusive lower and exclusive upper bounds.
* Added month segment start/end fields to Falcon checkpoint state serialization for assets and findings.
* Assets use last_seen_timestamp range filters per segment when an explicit host base date is supplied.
* Unfiltered findings use updated_timestamp range filters per segment when an explicit host base date is supplied.
* Fresh AID-scoped findings stay legacy/unsegmented to preserve bounded AID prepass behavior.
* Empty/out-of-segment asset pages do not emit batch/checkpoint events or consume published page numbers.
* Commands run: dotnet test src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test.csproj --no-restore
* Verification passed: FalconCollector test project, 47 tests.
* Updated logging so segmented Falcon assets and findings query logs include SegmentStartUtc and SegmentEndExclusiveUtc.
* Updated Falcon configuration metadata/comments to document newest-first calendar month segmentation and segment-aware request logging.
