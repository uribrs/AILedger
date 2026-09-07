# Assumptions

Every entry carries a status. OPEN assumptions must be resolved to VALIDATED or REJECTED with recorded
evidence before the dimension that depends on them is reported.

---

## A1 — The vendored copy is a faithful baseline for the pre-extraction engine — VALIDATED

All 65 authored `.cs` files SHA-compared against `5e5ca52`: **64 identical**, one divergent
(`Auth/OAuth2ClientCredentialsAuthenticator.cs`). So either source may be used, and `git show
5e5ca52:<path>` is usually more convenient.

Consequence worth stating: the single divergence is not noise, it is a finding in its own right —
see A2.

## A2 — The vendored divergence means production is ahead of HEAD — OPEN

Two capabilities exist in the vendored `OAuth2ClientCredentialsAuthenticator` and not at HEAD: the
`prefix.TrimEnd()`, and `ExtraFields` resolution via `static_value` → `credential_key` → credentials.

**What is established:** the code differs, our `ExtraFieldDefinition` declares both fields, and
`AwsSigV4Authenticator.cs:229-233` honours them while our OAuth authenticator does not. Zero of 279
definitions use either key (`grep -rl "static_value\|credential_key"` over the corpus → no files).

**What is NOT established, and must be before this is called a production gap:** that the vendored tree
is what actually executes in production. The adapter may reference it by project reference, compile it
directly, or the deployed collector may be built from something else entirely.

**Resolve by:** reading the adapter's csproj and the `YamlCollector` wiring to confirm which engine
source it compiles. If production does not compile the vendored tree, this is a stale fork rather than a
capability gap, and its severity drops sharply.

## A3 — 279 definitions are the universe of real usage — OPEN

Measured: 279 `.yaml` files in `cymulate-magic-integration/integrations`. But
`cymulate-magic-integration/CLAUDE.md` says **275**, and its `ARCHITECTURE`-adjacent text says "one
adapter serves all 275 vendors".

**Resolve by:** reconciling the count, and checking whether definitions are also authored or stored
anywhere else (the ISB dispatch payload carries YAML inline, per that repo's ADR-0002, which raises the
possibility of definitions that never land in this directory). If definitions can arrive from outside the
corpus, "zero of 279 use this key" is a weaker statement than it sounds and every liveness claim in the
table must be qualified accordingly.

## A4 — A side-by-side executable differential is feasible — OPEN, but likely

`5e5ca52` carries its own `.slnx`, `csproj`, `Directory.Build.props` and `Directory.Packages.props`, so
it should build in an isolated worktree independently of HEAD.

**Risks that could reject this:** its `Directory.Packages.props` pins package versions that may no longer
resolve; the test project at that ref may not compile against the current SDK; and the original's public
API differs from HEAD's, so a single harness cannot call both without an adapter shim per entry point.

**Resolve by:** attempting the build early — this is step I0, deliberately first, because the whole
evidence standard depends on it. **If it fails, say so and fall back to inspection**, and mark every
affected row's evidence as inspection-only rather than silently presenting weaker evidence as equivalent.

## A5 — Exact error-message text is comparable between the two engines — OPEN

Depends on A4. If both engines run, message text can be compared byte for byte, which is the standard the
loader work already achieved over this corpus. If only one runs, text comparison degrades to reading two
files, which cannot detect a message that moved between paths or one whose *interpolated values* changed.

**Resolve by:** A4's outcome. Record which standard each message-text row actually rests on.

## A6 — Performance drift can be assessed without benchmarks — OPEN

D18's precedent is that an O(1) → O(chunk) regression passed every test and was never measured. The
inventory is expected to find *shape* changes (buffering where there was streaming, materialisation where
there was laziness) by reading allocation and enumeration patterns.

**What this assumption is:** that identifying such shape changes by inspection is sufficient for the
table, and that actual benchmarking is out of scope.

**If REJECTED** — a candidate regression is found whose severity genuinely cannot be judged without
measurement — then say so and recommend a benchmark as follow-up rather than guessing at a magnitude.
`tenable.io`'s `chunk_size: 1000` is the concrete case already on file.

## A7 — Every capability in the original is discoverable by reading it — OPEN

The original is 11,364 lines. An inventory built by reading has a recall problem: the capabilities most
likely to be missed are exactly the small defensive ones that vanish quietly, which is the class D18 and
the two deleted guards belong to.

**Resolve by:** working from *enumerable surfaces* rather than from prose reading wherever possible — the
YAML key set the original's model classes declare, its `throw` sites, its `if` guards on nullable
members, its checkpoint field names. Each of those is greppable and countable, so coverage becomes
measurable instead of asserted. State the enumeration used for each dimension, and its count.

## A8 — The operator's five trusted definitions are representative for adjudicating severity — VALIDATED as an instruction, not as a fact

The operator designated `tenable.io`, `qualys`, `defender-vm`, `crowdstrike-falcon` and
`insightvm-cloud` as most trusted for questions of semantics and expectation. That is an instruction and
is binding for adjudication.

It does not follow that a capability unused by those five is unimportant — the other 274 definitions still
run. So: use the five to decide what *matters most*, and the full 279 to decide what is *live at all*.
Keep the two claims distinct in the table.
