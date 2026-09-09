what a run actually costs, measured

the first five runs with cost fields are recorded, and the answer is not where anyone was looking.
this entry is the baseline `measure-before-scoring` and `score-the-governance` were waiting for.
it is a measurement, not a defect. it holds the numbers so the next reading has something to
compare against.

## the shape of a run's cost

R26 is the expensive one: 104 turns, 12.1M cache reads, about 117,000 tokens of context re-read on
every turn. reconstructing what was in that context from its own stream:

| | share of the run's cost |
|---|---|
| fixed brief — manifest plus system prompt | ~56% |
| the agent's own output | ~24% |
| everything it read with tools | 22% |

the three add to 1.23M billed-equivalent against 1.21M measured, so the shape is not a guess.

**the manifest is the largest single cost in the system.** it is pasted into the prompt — both
adapters do this, `CodexAgentAdapter.cs:92` says so plainly — and re-read on every turn. one built
today measured 144,339 bytes, roughly 36,000 tokens, **92% of it artifacts** (28 of them). the five
measured runs carried between 43 and 257 artifacts.

**the agent's own output is the second largest.** R26 generated 55,335 output tokens, every one of
which became context that was re-read on each following turn. verbosity is quadratic.

## where the tool calls go, which is the smallest third

by carrying cost — a result pulled in early is re-read on every remaining turn, so early bulk costs
more than late bulk:

| R26 (coding run) | calls | tokens in | share of tool cost |
|---|---|---|---|
| read whole file | 9 | 21,592 | 70% |
| slice file (sed/head) | 21 | 6,543 | 13% |
| grep source | 20 | 4,406 | 13% |
| ledger read | 6 | 1,057 | 1% |
| everything else | 47 | 1,828 | 3% |

| RPROOF1 (analysis run) | calls | tokens in | share of tool cost |
|---|---|---|---|
| ledger read | 25 | 8,795 | 96% |
| grep source | 3 | 428 | 3% |

the two profiles are different systems. a coding run's tool cost is whole-file reads. an analysis
run's tool cost is hand-parsing `events.jsonl` and `status` output, twenty-five calls deep, because
there is no query surface. that second row is the whole argument for the sqlite projection.

## the three things the numbers refuted

**summing the token buckets overstates cost by 7 to 9×.** weighted at 1× uncached, 1.25× cache
write, 0.1× cache read: R26 12,286,391 naive against 1,410,550 billed-equivalent, 8.7×. across the
five runs, 7.2× to 8.7×. C7 argued for three fields instead of one total on exactly this ground and
it is an order of magnitude, not a rounding.

**the 1.25× weight is anthropic-only.** codex states `cache_write_input_tokens: 0` explicitly — the
field is present and zero, not absent. openai charges no write premium; the write cost is already
inside its uncached bucket. a shared weight table is harmless today and silently wrong the day that
changes. and a billed-equivalent token is still not money: pricing needs the model, and `Model` is
null on every codex run because codex names no model in the exec stream.

**per-bucket comparison across providers means nothing.** claude reported 178 uncached tokens on
R26; codex reported 140,324 on R24. that is not claude being efficient. it is the two providers
putting the same tokens in different buckets, which is C6, and `RunCostReader`'s mapping was
verified correct against both providers' real terminal events while writing this.

## the settled decision that came out of it

the embedding index over the repositories updates **at run close, not per edit.** three reasons, all
from the data above:

- within a run nothing goes stale. the agent re-reads its whole history every turn — that is what
  R26's 12.1M cache reads *are* — so it cannot forget an edit it made at turn 20.
- across runs the kernel already serialises: runs against one work item are sequential, and live
  items hold disjoint scopes. a closeout index always lands before the next reader who could care.
- `src`, `tests` and `cognitive` are 170 files and 1.4 MB. a full re-embed is seconds. incremental
  machinery exists to avoid a pass that is already free.

it hooks where the sidecar is written, **not where a run succeeds** — cancelled runs edited files
too, and an index that only updates on clean completion diverges silently on a quarter of runs. and
it takes the same precautions as the cost read beside it: own deadline, own token, swallows its own
failures. it must never be able to fail a run close (C15).

## what is still dark

- **the coordinator's cost.** item 21. the session dispatching the runs holds no closeable run, so
  none of the above describes it.
- **the manifest in tokens rather than artifacts.** `ManifestArtifactCount` counts artifacts and the
  artifacts are 92% of the largest cost in the system. the count does not say how big they were.
- **claude's own `total_cost_usd` and `subagent_stats`,** sitting on the same terminal event and
  currently unread.
