* Produce a Markdown runbook in the Planning directory so it can be reused outside this chat.
* Treat `RunGlue` as a trigger/monitor tool only; S3 upload is a separate preparation step.
* Avoid AWS network actions in this pass to prevent accidental workflow runs or data uploads.
* Document both direct-prefix and zip modes because DEX behavior depends on whether `ZIP_FILE_KEY` ends with `.zip`.
* Prefer `.json` filenames for NDJSON shards because the AWS S3 resolver filters numbered files by `.json` extension.
* Treat lower-environment parser code deployment as Glue script uploads plus a Python wheel upload, analogous to deploying compiled DLLs but split by Glue entrypoint and shared library artifact.
* Use `cymulate-integration-parsers` side branches for executable dev artifacts and `RunGlue` only for triggering/monitoring those dev workflows.
* Treat enhanced Spark/Glue logging as branch-scoped diagnostic code unless it proves broadly useful.
