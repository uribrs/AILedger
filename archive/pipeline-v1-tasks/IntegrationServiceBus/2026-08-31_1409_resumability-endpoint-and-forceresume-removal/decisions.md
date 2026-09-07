# Decisions

* The endpoint answers **"is this resumable?"**, not "does a row exist?" — presence plus `HoldsResumableProgress` plus the collector's `CanResumeFrom` verdict. Presence alone reintroduces the drift the task exists to remove.
* The endpoint is **batch**. Admin renders unbounded rows and refreshes every 60 seconds.
* The endpoint is named for the verdict it returns, not for a row read. Proposed `POST /api/v1/Events/resumability`.
* Gate 4 calls the evaluator **in-process**. ISB does not HTTP-call itself.
* Gate 5 stays. Its reason survives the change: a silent fall-through would write a full result set into an S3 prefix already holding a partial one.
* The decline path stops renewing the retention clock. A refusal must let the row age out rather than push its expiry forward.
* The cancel sweep's guard becomes `startedAt: { $gt: new Date() }` instead of `forceResume: { $ne: true }`. **This is a behaviour change, not a refactor** — it also stops the sweep cancelling a genuinely backlogged pending run whose slot has passed. Accepted deliberately.
* `forceResume` is deleted rather than kept as a hidden override. Its only function was overriding an adapter declination, and that override was never verified to work.
* Admin keeps its nine server-side refusals. The endpoint governs what is *offered*; the refusals govern what is *accepted*.
* The `?tid=` mismatch is recorded, not fixed. It predates this work and affects the existing Stop button.
* The endpoint inherits ISB's unauthenticated posture. It is a read, and a strictly smaller exposure than the existing unauthenticated mutating `POST stop`.

## Proceeding on unverified

* Proceeding on unverified: `ResumeAsync` seeds its progress counter from `AdapterCheckpoint.CurrentSequenceId`. If wrong: a resumed run emits sequence ids from 1 against a BE high-water mark of several hundred, and every progress message is silently dropped by the `$lt` guard until it climbs past the old mark. This is why S0 blocks S1-S8.
* Proceeding on unverified: the render-time answer decaying between page render and click is acceptable, because gate 5 makes the dispatch-time failure loud and the next render removes the button. If wrong: an operator sees a button that fails once before disappearing.
* Proceeding on unverified: calling `CanResumeFrom` per row in an HTTP request is affordable. If wrong: the endpoint needs a cheaper first pass (presence plus progress) with the full check at dispatch — which reopens a narrow form of the livelock and must then be measured, not assumed.
