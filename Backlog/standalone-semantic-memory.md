# standalone semantic memory

Build a local, optional knowledge index over AILedger's durable records, then extend it to source
repositories only after the first corpus proves that retrieval quality improves. The index runs in
**shadow mode**: it can ingest, search and evaluate real data, but no kernel command, transition,
manifest or provider launch reads it.

This is intentionally a major feature with a small first release. Phase 0 and phase 1 now deliver
the persistence, identity, embedding and evaluation seams without turning the governance kernel into
a long-running application. Phases 2 to 4 remain backlog work.

Governed task: `2026-09-08_1909-standalone-memory-index`.

## delivered phase 0/1 surface

The delivered first release is a separate .NET 8 library and `ailedger-memory` executable. It reads
canonical event histories, the published lesson store and best-effort refusal journals without
taking the kernel writer lock, then projects purpose-shaped documents into a disposable SQLite
database. The documents include minted and recalled lessons, decisions, constraints, alternatives,
claims joined to evidence, work and run summaries, artifact material, and refusal-journal records.
They preserve lifecycle, authority, relations and exact canonical citations.

SQLite relational tables remain the metadata authority for the projection. FTS5 supplies lexical
candidates; versioned vectors are exact-ranked in process and fused with lexical results. This is
the deliberately simple backend for the measured durable-record corpus, not a commitment for later
repository scale.

The command surface is explicit and manual: `rebuild`, `update`, `search`, `inspect`, `stats`,
`evaluate` and `drop`. With no `--database`, every command derives the same disposable database
identity from the resolved physical canonical root and places it in the platform-local AILedger data
directory. Physical-path aliases converge where the filesystem treats them as the same directory;
distinct roots remain distinct. `stats` reports the resolved path, `drop` removes only the derived
projection, and an explicit `--database` remains exact.

Lexical-only mode is the default. Hybrid mode can use deterministic in-process embeddings for tests
and repeatable offline evaluation, or an explicitly confirmed Ollama adapter. The production adapter
accepts only a loopback endpoint, calls `/api/tags` and `/api/embed`, disables proxy use, clears any
configured proxy, and disables redirects and cookies on every accepted built-in transport. It
refreshes the runtime model digest for each identity-resolution and embedding operation. It records
that digest and the runtime dimensions as the active embedding identity, sends document and query
inputs distinctly with
`truncate: false`, and never downloads or pulls a model.

The final normal-host verification run built all eight projects in Release with zero warnings or
errors and passed 519 of 519 tests: 449 kernel tests and 70 memory tests. That proves the delivered
deterministic and isolation contracts at those bytes; it is not a live-model quality result and does
not activate any integration.

## the problem it addresses

The kernel's durable knowledge is spread across append-only task histories, materialised projections,
the cross-repository lesson store and refusal journals. That arrangement is correct for authority and
replay, but increasingly expensive and imprecise as a retrieval mechanism.

Today, task opening:

1. enumerates archived sibling task directories;
2. replays each archived history to find locally minted lessons;
3. republishes them into the cross-repository lesson store;
4. reads that store in full;
5. filters by exact tag overlap and recency;
6. returns at most ten tagged lessons, with at most three from one source task.

The relevant implementation is `FileGovernedTaskService.cs:11-16,159-178,181-236,239-328`.
`FileLessonStore.cs:26-35,72-80,83-118,132-215` also gives the shared store a 16 MiB limit,
reads it sequentially and copies the existing file into a replacement on append.

The limits are not arbitrary mistakes. Every recalled lesson enters the manifest and costs provider
tokens, so removing the final context budget would reduce accuracy. The missing layer is a retrieval
system that can consider the full corpus before choosing a small, relevant and diverse result set.

The same limitation applies more strongly to records that are not recalled at all:

- accepted and superseded decisions;
- active and historical constraints;
- claims and their supporting or refuting evidence;
- rejected alternatives;
- work, run and artifact history;
- refusal telemetry and the later actions that satisfied or bypassed it;
- eventually, source-code symbols and repository structure.

These records contain the reasons behind the current state. Searching filenames and replaying tasks
can recover them, but it makes cross-task and cross-repository questions progressively slower and
encourages hard caps chosen before semantic relevance is known.

## goals

- Index all meaningful durable ledger records without changing their authority.
- Make indexing incremental, idempotent and safe to repeat.
- Support exact, lexical and semantic retrieval over the same corpus.
- Preserve a navigable citation from every result to its canonical event, journal row or source file.
- Rank authoritative, current and scope-matching records ahead of merely similar prose.
- Measure retrieval quality and cost against a fixed evaluation set before integration is considered.
- Establish a later path for incrementally indexing several Git repositories by changed blob.
- Keep the first version local, manually invoked and operationally boring.

## non-goals

The first release does **not**:

- replace `events.jsonl`, `state.json`, `lessons.jsonl` or `refusals.jsonl`;
- change replay, validation, command handling or transition rules;
- change task opening, lesson recall or context assembly;
- automatically add retrieved material to a manifest or provider request;
- run a file watcher, daemon, service or scheduled job;
- train a classifier or embedding model;
- index repository source code;
- decide that a refusal is a lesson;
- score an actor from its refusals;
- provide multi-user access, remote synchronization or a UI;
- promise lower total disk consumption.

The index reduces repeated I/O and the active context working set. Text, metadata, embeddings and
search indexes normally occupy more disk than the canonical files alone. It is a retrieval and
latency feature, not permission to discard evidence.

## the architectural boundary

```text
canonical sources                          disposable projection

.ailedger/tasks/*/events.jsonl  ─┐
.ailedger/tasks/*/refusals.jsonl ├──> ailedger-memory ───> memory.sqlite
published lessons               ─┤          │                   │
Git repositories (later)        ─┘          └── search/evaluate ┘

ailedger kernel  ───────────────────── no dependency ────────────────────────┐
context build    ───────────────────── no reads ─────────────────────────────┤
provider launch  ───────────────────── no reads ─────────────────────────────┘
```

The delivered executable is `ailedger-memory`, not another command routed through `ailedger`.
Keeping a process and package boundary is what makes "not engaged" testable rather than a feature
flag somebody can accidentally default to true.

The executable may reference declarative contracts from `AILedger.Core` and `AILedger.Storage`.
It must not call `IGovernedTaskService.GetHistoryAsync` or `GetStateAsync`: both acquire the kernel's
exclusive task mutation lock, and `GetStateAsync` may rewrite materialised projections. Memory owns
lock-free, read-only canonical-source readers instead. The kernel, storage service and CLI must not
reference the memory assemblies. The solution and packaging scripts may know that the executable
exists; production kernel behavior must not.

The database is a read model. Deleting it loses search availability and nothing else. Rebuilding it
from canonical sources must restore equivalent documents and metadata, modulo an explicitly changed
normalizer or embedding model version.

## delivered project shape

```text
src/AILedger.Memory/
  ingestion and normalization contracts
  document identity and content hashing
  projection store
  lexical and vector retrieval
  ranking and citations

src/AILedger.Memory.Cli/
  explicit commands and process composition

tests/AILedger.Memory.Tests/
  fixtures, rebuild/update/search/evaluation tests
```

Dependencies point inward:

```text
AILedger.Memory.Cli -> AILedger.Memory -> AILedger.Core contracts
                                      -> declarative Storage contracts only

AILedger.Cli        -X-> AILedger.Memory
AILedger.Core       -X-> AILedger.Memory
AILedger.Storage    -X-> AILedger.Memory
```

The memory feature owns its read-only event and refusal readers. Do not broaden or reuse
`IGovernedTaskService` merely to make the experiment aesthetically symmetrical: its read behavior is
correct for governed state, but wrong for a background-capable projection that must never delay a
writer.

## operating model

Every action is explicit in shadow mode:

```text
ailedger-memory rebuild [--root PATH] [--database PATH]
ailedger-memory update  [--root PATH] [--database PATH]
ailedger-memory search  QUERY [--root PATH] [--database PATH] [--kind KIND] [--repo REPO] [--task TASK] [--limit N]
ailedger-memory inspect DOCUMENT-ID [--root PATH] [--database PATH]
ailedger-memory stats [--root PATH] [--database PATH]
ailedger-memory evaluate --cases PATH [--root PATH] [--database PATH] [--mode baseline|lexical|hybrid]
ailedger-memory drop [--root PATH] [--database PATH]
```

`rebuild` creates a new database beside the old one and replaces it only after ingestion,
`PRAGMA integrity_check`, and FTS5's `integrity-check` command with `rank=1` succeed. The latter is
the check that compares an external-content FTS index against its content table. `update` reads
source checkpoints and processes only added or changed content. `search` is for humans and offline
tests; its output is never injected into governed work. `inspect` returns the normalized document,
metadata and exact source citation used to create it.

The default database lives in the platform-local AILedger data directory under a `memory` component,
not inside a task and not in Git. Its subdirectory is a stable digest of the resolved physical
canonical root, so several ledgers do not silently replace one another and path aliases converge
according to the actual volume's case behavior. An explicit `--database` bypasses derivation and is
preserved exactly for tests and portable experiments.

## the retrievable unit

One event is not automatically one retrieval document. Event envelopes are the units of replay;
retrieval needs units that carry enough meaning to answer a question.

The stable internal contract is:

```text
MemoryDocument
  id                    stable derived identity
  kind                  lesson, decision, claim-evidence, alternative, refusal-chain, ...
  text                  normalized retrieval text
  repository            nullable source repository
  taskId                 nullable task
  workItemId             nullable work item
  runId                  nullable run
  actorId                nullable actor
  sourceTimestamp
  sourceVersion          event version or journal checkpoint
  authority              deterministic record class
  lifecycle              active, resolved, superseded, rejected, deleted
  tags
  relatedIds
  citation               exact canonical source locator
  contentHash
  normalizerVersion
```

An embedding is attached to a document version, not treated as part of its identity:

```text
DocumentEmbedding
  documentId
  contentHash
  provider
  model
  dimensions
  embeddingVersion
  vector
  embeddedAt
```

This permits a changed model to coexist during migration and prevents unchanged text from being
embedded again.

## first corpus: durable ledger memory

The first release indexes the highest-value records in this order:

1. minted and imported lessons, including classification, verification instruction, `doNot`,
   provenance and supersession;
2. accepted or superseded decisions with their rationale and dependency claims;
3. constraints, including source and scope;
4. rejected alternatives and their rejection rationale;
5. claims joined to supporting and refuting evidence and final disposition;
6. refusal chains;
7. work and run summaries;
8. artifact metadata and bodies where the artifact kind is useful for later retrieval.

Raw events remain inspectable but are not all embedded individually. The normalizer produces
purpose-shaped documents that cite every source event contributing to them.

### refusal chains

`RefusalJournal.cs:20-33` deliberately defines refusals as best-effort telemetry outside replay and
governed state. The memory projection must preserve that status.

A useful refusal document is not only the exception message. It joins, when the evidence supports
the join:

```text
attempted command
-> refusal site and exact message
-> task version and relevant obligation at that version
-> later command that satisfied, waived or abandoned the obligation
-> outcome
```

The join is an indexer's interpretation and therefore carries the journal row and event ids from
which it was derived. An unmatched refusal remains searchable as an unmatched refusal. It must not
be fabricated into a resolution, lesson or judgment of the actor.

Promotion from a repeated refusal pattern to a governed lesson remains a future kernel workflow.
The index may surface candidates; it cannot mint or validate them.

## source identity and incremental updates

Ledger documents use canonical identities derived from repository/root identity, task id, record
kind and record id. Composite documents also include a deterministic grouping key. Their
`contentHash` covers normalized text plus ranking-relevant metadata.

Each indexed source keeps a checkpoint appropriate to how that source is written:

- event history: last indexed event version and file fingerprint. The writer builds a replacement
  file and atomically renames it, so a lock-free reader must reopen by path and sees either the old
  or new complete history;
- refusal journal: byte offset plus prefix fingerprint. It is appended in place with read sharing,
  so a concurrent lock-free reader may see a torn final row and must leave that row uncheckpointed
  until a later pass;
- lesson store: row identities plus file fingerprint;
- repository content later: repository identity, Git object id and path/symbol identity.

Append-only growth takes the fast path. A prefix mismatch, truncation, schema-version change or
normalizer-version change invalidates the affected source and reprojects it. The index never tries
to repair a canonical source.

Updates are transactional per source. A crash may leave the old checkpoint in place and repeat work;
it must not advance the checkpoint while only part of the corresponding documents are visible.

Deletion means deleting or tombstoning projection rows whose canonical source no longer exists.
It never deletes canonical input. A rebuild is the final repair path for every incremental-index
failure.

## storage choice for the first release

The delivered store uses SQLite because the first corpus is personal, local and bounded enough to
evaluate without operating a server. Ordinary relational tables hold metadata, SQLite FTS5 supplies
lexical search, and embeddings remain behind a store interface while the initial candidate set is
exact-ranked in process. A vector extension is not a prerequisite for proving the feature.

The live September 2026 corpus measured 26 tasks, 2,857 events, 144 published lessons and 19
refusals, projecting to roughly 1,241 retrieval documents. At 1,536 float32 dimensions its vectors
occupy about 7.3 MiB and the whole first-release database should remain below 50 MB. The default
`Microsoft.Data.Sqlite` native bundle documents FTS5 support, and the locally shipped macOS, Linux
and Windows native libraries contain `ENABLE_FTS5`. The delivered store verifies the packaged
runtime at both store-open paths with `sqlite_compileoption_used('ENABLE_FTS5')` and fails closed
when FTS5 is unavailable.

This is a hypothesis to measure, not a permanent platform commitment. Move to a vector extension or
PostgreSQL/pgvector only when repository-scale measurements show that exact vector ranking,
concurrency or database size is the bottleneck.

Minimum logical tables:

```text
sources              canonical roots and checkpoints
documents            normalized text, metadata, lifecycle and citation
document_relations   typed links between records
document_embeddings  versioned vectors
index_runs           rebuild/update provenance, counts, duration and failures
documents_fts        external-content FTS index
```

The schema carries its own version. Startup refuses an unknown newer schema and asks for a compatible
binary. A known older disposable schema may be migrated or rebuilt; the simpler safe choice wins.

## embedding and classification boundaries

Embedding generation is a replaceable capability:

```text
IEmbeddingGenerator
  provider/model/version identity
  document or query input kind
  runtime dimensions
  embed(chunks)
```

The index is usable in lexical-only mode and defaults to it. A missing or unavailable local model or
a failed embedding batch does not make already-indexed lexical search unavailable. Deterministic
in-process embeddings exercise hybrid behavior in automated tests without pretending to measure a
real model.

The only production embedding adapter in phase 1 is opt-in Ollama. The CLI requires explicit local
confirmation and a model name, refuses non-loopback endpoints, disables redirects and cookies, and
does not pull a model. Every accepted built-in transport also has proxy use disabled and its proxy
cleared before a request. The adapter refreshes the configured model's digest for each identity
resolution and embedding operation, discovers vector dimensions at runtime, persists them with
provider and embedding version as one active identity, and refuses cross-identity reuse. Documents
are chunked before embedding; chunk index and count are stored, and retrieval uses the maximum
similarity over a document's chunks.

The first release does not need an AI classifier. Most useful metadata is already deterministic:
record kind, task, repository, role, lifecycle, tags, dependencies, source time and authority.
Premature classification would add model cost and an unmeasured source of stale labels before the
retrieval baseline exists.

Later repository classification can add model-versioned labels for subsystem, architectural layer,
domain, artifact type and risk. Those labels are hypotheses in the projection. They never become
kernel claims merely by being stored.

## retrieval

Search is hybrid rather than vector-only:

1. parse explicit filters such as repository, task and record kind;
2. collect lexical candidates for identifiers, commands, filenames and exact language;
3. collect semantic candidates from embeddings when available;
4. fuse candidate ranks;
5. rerank deterministically by authority, lifecycle, scope match, freshness and source diversity;
6. return a bounded result set with canonical citations and component scores.

Authority is not inferred from confident prose. It is derived from record type and lifecycle. A
starting precedence is:

```text
active constraint / accepted decision / minted lesson
validated or rejected claim with cited evidence
rejected alternative / resolved escalation
resolved refusal chain
run or work summary
unresolved claim
unmatched refusal / raw historical event
```

This precedence is a ranking input, not a permission decision. Superseded material remains searchable
when explicitly requested but is suppressed from ordinary results. Results from several source tasks
should be preferred over a page filled by one verbose task when relevance is otherwise comparable.

Every search result reports enough detail to debug retrieval:

```text
document id and kind
final rank
lexical, semantic and policy components
lifecycle and authority
repository/task/work/run
canonical citation(s)
embedding model/version, when used
```

## evaluation before integration

An index that merely feels clever is not evidence. The delivered release includes a checked-in,
non-empty evaluation set whose expected source records are known. Its cases cover:

- exact identifier and error-message lookup;
- conceptual questions phrased differently from the source;
- a newer decision superseding a semantically similar older decision;
- lessons from several repositories competing for results;
- a refusal with a later satisfying action;
- an unmatched refusal;
- a question with no supported answer;
- queries where lexical search should beat embeddings;
- queries where embeddings should beat tags and lexical search.

Evaluation reports:

- expected-source recall at 1, 5 and 10;
- reciprocal rank;
- stale or superseded records incorrectly returned in the default top set;
- source-task and repository diversity;
- citation correctness;
- embedding calls avoided through content hashing;
- database size and bytes per document;
- search latency, reported separately for lexical-only and hybrid search.

The baseline is the existing tag-and-recency recall, not an imaginary perfect system. Integration is
eligible for discussion only when the shadow index improves the agreed retrieval cases without
returning uncited prose or hiding lifecycle state.

## failure behavior

- Canonical parse corruption fails the affected source visibly; it is not skipped.
- An embedding-provider failure records the failed index run and leaves lexical documents usable.
- A failed rebuild leaves the previous database untouched.
- A failed incremental transaction leaves its checkpoint unchanged.
- An unknown database schema fails closed with a diagnostic.
- An unavailable database makes only `ailedger-memory` unavailable.
- No memory failure may change an `ailedger` exit code or outcome while shadow mode holds.
- Cancellation stops between bounded batches and does not expose a partial checkpoint.

## privacy and model use

Repository code, artifacts, evidence and refusal messages may contain secrets or proprietary text.
No default sends them to a model. Phase 1 has no remote adapter: its only production provider is the
explicitly confirmed, loopback-only Ollama adapter. Adding any remote provider is successor work and
requires a new privacy decision.

Logs must not print source bodies or vectors by default. Credentials stay outside the database.
The index records provider and model identity, never the credential. A local-only or lexical-only
configuration remains supported.

## staged delivery

### phase 0 — contract and benchmark corpus — delivered

- Freeze `MemoryDocument`, citation and checkpoint semantics.
- Choose representative archived tasks and write expected-query cases.
- Measure current file-scan and recall behavior.
- Confirm SQLite/FTS availability in the supported .NET packaging path.

Delivered exit: the checked-in corpus scores the existing tag-and-recency baseline before embeddings
and supports repeatable lexical and deterministic-hybrid comparisons.

### phase 1 — durable-record shadow index — delivered

- Add the standalone assemblies and explicit CLI.
- Implement atomic rebuild, incremental update, inspect, stats and lexical search.
- Normalize lessons, decisions, constraints, alternatives and claim/evidence pairs.
- Add refusal-journal material and run/work summary normalization.
- Add provider-agnostic, versioned embeddings and hybrid search.

Delivered exit: rebuild identities and content hashes are deterministic, a no-op update writes no
documents and requests no embeddings, and deterministic hybrid evaluation can be compared with the
baseline on the checked-in cases. Nothing in the kernel or context path references or invokes the new
assemblies. Live Ollama quality remains an activation measurement, not a delivery claim.

### phase 2 — repository ingestion

- Register local repositories explicitly.
- Index Git-tracked content by blob hash.
- Parse supported languages into symbol-shaped chunks, with a documented text fallback.
- Reuse embeddings for unchanged blobs and renames.
- Update only changed chunks and dependent summaries.

Exit: a change in one repository causes no embedding calls for unchanged content in the other
repositories, and every result names the indexed commit and source span.

### phase 3 — model-versioned classification and summaries

- Add repository, subsystem and directory summaries.
- Add optional semantic labels only where deterministic metadata is insufficient.
- Track classification dependencies so affected summaries are rebuilt after changes.
- Evaluate whether classification improves retrieval before using it as a default rank input.

Exit: labels and summaries demonstrably improve held-out queries and can be rebuilt independently.

### phase 4 — optional kernel integration, separately authorized

This phase is not included in the present feature authorization. It requires a new decision and
governed task.

Possible integration begins with an explicit opt-in such as a context-source adapter or
`context build --memory`. It must retain the kernel's context budget, citations, role filtering and
supersession behavior. Shadow evaluation results are its entry evidence.

## delivered first-release guarantees

1. The new executable builds and runs without changing existing `ailedger` command output or exit
   behavior.
2. Deleting the database and running `rebuild` restores the same document identities and normalized
   content for the same inputs and normalizer version.
3. A second `update` over unchanged sources writes no documents and requests no embeddings.
4. Appended events and refusal rows update only their affected documents and checkpoints.
5. Superseded lessons and decisions are retained but excluded from ordinary search by default.
6. Every result carries a canonical source citation that `inspect` can resolve.
7. Lexical search works with no embedding provider configured.
8. A failed embedding batch leaves prior searchable state intact and is visible in index-run stats.
9. Evaluation reports recall, reciprocal rank, baseline order, superseded-result count, diversity,
   latency, embedding reuse and database size against a fixed non-empty corpus.
10. Dependency tests prove that Core, Storage and the existing CLI do not reference Memory.
11. A concurrency test proves indexing does not acquire `.writer.lock` and does not delay or change
    the result of a governed command against the same task.
12. The final normal-host verification run is green: 519/519 tests and a zero-warning Release
    build.
13. No watcher, daemon, scheduled job, startup hook, repository ingestion, classifier, automatic
    invocation or context integration is installed.

## activation gates

The memory index stays disengaged until a later, separately authorized task establishes all of the
following:

- retrieval quality exceeds the existing baseline on representative queries;
- citations resolve reliably;
- lifecycle and supersession filtering has no known correctness defect;
- index freshness can be detected rather than assumed;
- partial embedding coverage is directly measurable, uncovered documents are retried, and a
  zero-work update cannot replace a degraded health signal with a healthy one;
- local privacy behavior is acceptable;
- a named local Ollama model is installed and a model-identified live evaluation demonstrates useful
  quality and acceptable latency on the real corpus;
- performance and disk costs are measured on the actual repositories;
- failure of the projection cannot prevent governed work;
- the operator explicitly accepts the integration scope.

## open questions to earn with evidence

- What is the measured end-to-end exact-ranking latency after real embeddings are present? Corpus
  arithmetic establishes headroom but is not a benchmark.
- Which local Ollama model offers the best measured quality/cost balance, and what dimensions does
  that model report at activation time?
- Should normalized text live in the database or be reconstructed from source on result hydration?
- What stable repository identity survives moving a checkout without conflating unrelated clones?
- Which artifact kinds are useful enough to index rather than only retain as citations?
- Can refusal resolutions be joined deterministically, or do some require a model-generated summary?
- At what corpus size does repository ingestion require an approximate-nearest-neighbour index?
- Which code parsers cover enough of the operator's repositories to justify structural chunking?

These are research questions, not reasons to make the first version generic. The evaluation corpus
decides them.

## relation to existing backlog work

- `a-productive-task-starves-its-successor.md` demonstrates why ranking over the full lesson corpus
  matters; semantic retrieval should address relevance before the final context cap.
- `did-the-lesson-matter.md` provides `fromLesson` links that become valuable authority and outcome
  signals in the index.
- `record-the-refusals.md` supplies the telemetry from which refusal chains can be projected.
- `measure-before-scoring.md` supplies deterministic causal measurements and reinforces that the
  index must not turn similarity into a governance score.
- `archived-memory-conservation.md` concerns canonical archive storage. Semantic indexing does not
  make canonical compression or deletion safe and should not silently absorb that feature.
- `the-append-is-quadratic.md` concerns canonical write cost. The projection may remove read pressure
  but does not change that write path while shadow mode holds.

## remaining work decomposition

Phase 0/1 contracts, storage, durable-source normalization, retrieval, embeddings, application,
evaluation, CLI composition and permanent verification tests are delivered. Remaining work follows
the phase boundary above:

1. phase 2 adds explicitly registered repository ingestion and changed-blob/symbol indexing;
2. phase 3 earns model-versioned classification and summaries through measured retrieval gains;
3. phase 4 considers separately authorized, explicit kernel/context integration only after every
   activation gate is met.

Before phase 2 starts, harden partial-embedding recovery across storage, retrieval and application:
statistics must report embedded-document coverage, hybrid search must disclose uncovered documents,
and incremental update must retry missing vectors without letting a no-op run hide degraded state.
Independent phase 1 verification proved that the current shadow index remains isolated and safely
rebuildable, but also proved that its present chunk count is not a reliable coverage measure. This
hardening is therefore deferred from the dormant release, not waived for later use.

## cost and risk

This is infrastructure, persistence and model-boundary work. Its principal risks are not cosine
similarity mathematics; they are stale indexes presented as current, unverifiable summaries,
accidental kernel coupling, remote disclosure of source content, non-idempotent updates and an
evaluation set that rewards the implementation it was written after.

The disciplined first release buys a real searchable corpus and measured evidence while preserving
the current kernel. The undisciplined version buys a daemon, a database server and a new source of
truth before proving that retrieval improved. This backlog item authorizes the former.
