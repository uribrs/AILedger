* VALIDATED: The user's immediate need is an operational runbook, not an automated uploader.
* VALIDATED: S3 upload examples may use AWS CLI syntax without executing network calls.
* VALIDATED: `RunGlue` config values in `appsettings.local.json`, `clients/*.json`, and `file_paths.json` are treated as the local source of truth for environments and integration identifiers.
* VALIDATED: Parser input format should be inferred from `cymulate-integration-parsers` code and test fixtures when available.
* VALIDATED: Current RunGlue paths are direct-prefix paths, not `.zip` object paths.
