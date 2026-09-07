# Decisions

- D-authorize: The verbatim-preservation rule is explicitly lifted for S1/C4/C1 only (operator-authorized behavior changes). (VALIDATED)
- D-S1: Scrub `bodySnippet` + `url`/`urlDisplay` with `LogRedaction.Scrub` at the `AdapterHttpRequestFailedException` raise site (the `message` and the stored `url`/`bodySnippet`). The classifier call keeps RAW inputs (needs raw to classify; not logged). (VALIDATED)
- D-C4: Kind-switch — `Utc`⇒value; `Local`⇒`value.ToUniversalTime()`; `Unspecified`⇒`SpecifyKind(value, Utc)` (assume already-UTC, documented). Keep the `MinValue` sentinel. (VALIDATED)
- D-C1a: Wrap host callbacks invoked before the last-word failure publish in try/catch (log at Warning, continue). The failure publish is never gated on callback success. (VALIDATED)
- D-C1b: Terminal error/completion publishes use `CancellationToken.None` (match the runner's documented last-word invariant). (VALIDATED)
- D-scope: C2 (dedup reshape), C3 (broad test pass), M7 (ratified design), cert-bypass, hydrator catch, and resume-leg M3/M12 semantics are OUT of scope. (VALIDATED)
