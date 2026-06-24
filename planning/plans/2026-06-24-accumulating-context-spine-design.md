# Accumulating Context Spine — Design

**Date:** 2026-06-24
**Status:** Approved (brainstorm complete; not yet planned for implementation)
**Applies to:** Both platforms (macOS `src/`, Windows `windows/`). Cross-cutting data/context architecture.

## Purpose

Turn AccountaBall from a per-session journal into a *continuously accumulating
context graph* that is rich enough to eventually **recall, suggest, and act** on
the user's behalf — with autonomous **execution** as the bar the captured data
must eventually clear.

The user's framing: *"a time will come where it has a lot of context on all that
I'm working on and it can do it for me or suggest things already. The purpose is
to make sure the data we're getting is enough to build a context that can do
that."* This applies to **both** FreeBall (passive) and task mode (declared task).

## Decisions locked during brainstorm

| Question | Decision |
|----------|----------|
| Target capability | **All three, progressively** — recall → suggestions → execution. Design the data for the hardest case (execution). |
| Durable memory unit | **Entity layer (both)** — Projects contain Threads contain session Contributions. |
| Per-cycle capture fidelity | **Unify on full OCR** — task mode also retains full screen text, like FreeBall. One high-fidelity spine. |
| Entity formation | **AI proposes, user confirms** — corrections become the learning signal. |
| Retention/privacy | Keep raw **indefinitely** (default), prune optional. Protections: local-only + encrypted at rest, capture exclusions, pause + redaction. Judged sufficient. |

## Today's data (baseline)

- **Task mode** — `WorkSession` (taskTitles), `TimelineEntry` (one per 5s cycle:
  timestamp + taskIndex|nil + 3–5 word label), `JustificationEvent` (off-task
  excuse, verdict, "why"), cross-session `KnowledgeTask` / `Allowance` /
  `TaskCompletion`. **Screen OCR text is discarded after classification** — only
  the label survives.
- **FreeBall mode** — `FreeBallSession` (narrative, categories, insight,
  workingOn/people/codeContext/openThreads) + `FreeBallCapture` (**full OCR
  text** + firstSeenAt…lastSeenAt span).

**Core gap:** data is session-scoped. Only `KnowledgeTask` accumulates across
sessions. `openThreads` are re-derived strings, not living objects. The two modes
are silos. And task mode is far lower-fidelity than FreeBall (label-only).

---

## Section 1 — The accumulating spine

New durable entities (persist across sessions, span both modes):

- **Project** — a long-lived workstream (e.g. "AccountaBall Windows port").
  Fields: title, aliases, status (active/dormant/done), created/last-touched,
  rolling AI summary, accumulated `people` / `codeContext` / external refs.
- **Thread** — an open loop under a project ("fix panel sizing", "reply to X").
  Fields: description, status (open/resolved), opened/resolved timestamps,
  evidence cycles. Threads power *suggestions* ("you left this open").
- **Contribution** — the join between a session and a project/thread: "in
  session S you advanced thread T for 25 min; here's what changed." Lets a
  project get *richer* each session instead of being re-derived.

Existing entities keep their jobs but point upward:
`TimelineEntry` / `FreeBallCapture` (raw cycles) → `Contribution` → `Thread` →
`Project`. `KnowledgeTask` becomes a specialization of / merges into `Project`.

**Key property:** both FreeBall and task mode write into the *same* Project/Thread
graph. An hour of either mode deepens the same spine. No silos.

## Section 2 — Capture & ingestion

**Unified capture primitive**, one per cycle:
`Capture = { timestamp, fullOCRText, app/window hint, mode (task|free), taskIndex? }`.
Task mode **stops discarding** the OCR it already ran for classification — zero
extra capture cost, just stop throwing it away. The two modes converge on one table.

**De-dup stays and moves to the shared layer.** Existing `FreeBallDedup` /
`FreeBallCondenser` collapse unchanged screens into `firstSeenAt…lastSeenAt`
spans (a static screen for 10 min = one row, not 120). This is what makes
full-OCR retention affordable; task mode now benefits too.

**Two-stage processing:**
1. **Live (per cycle, cheap):** classify on/off-task (task mode) or just store
   (FreeBall). Unchanged — no added latency to the 5s loop.
2. **End-of-session (batch, expensive):** the existing recap pass *also* does
   entity resolution — cluster captures, propose Project/Thread attachments,
   write `Contribution` rows. The 14B model earns its keep here; runs once per
   session with the whole transcript, not cycle-by-cycle.

Only hot-path change: "task mode: persist the OCR you already computed."

## Section 3 — Entity formation & the learning loop

**At session end, the model proposes; the user confirms.** The recap gains an
editable "this session touched:" block:

- **Proposed attachments:** "Advanced *Windows port* → thread *fix panel sizing*
  (25 min)." Actions: accept / rename / merge / split / reassign / close.
- **Proposed new entities:** "Looks like a new project: *Clay outreach*?"
- **Proposed thread closes:** "*fix panel sizing* looks resolved — close it?"

**Every correction → a `Judgment` row:**
`{ captureRange, modelProposal, userDecision, timestamp }`. This is the gold:
- audit trail of *why* the spine looks the way it does;
- eval set ("would the model have gotten this right?");
- few-shot / fine-tune signal so proposals improve and the system earns the
  right to act autonomously.

**Trust ladder.** The system graduates *suggest → act* **per-project**, gated on
its track record in `Judgment` history. A project whose last N proposals were
accepted unchanged becomes one the agent can act on; a noisy one stays
suggestion-only. "It can do it for me" becomes an *earned, measurable* state, not
a switch.

**Failure handling.** If the AI is unreachable at session end (existing
`recapPending` flag), captures are retained raw and entity-resolution re-runs
later — no data lost, just deferred.

## Section 4 — Retention, privacy & storage reality

**Storage is smaller than it feels.** OCR is text; de-dup collapses static
screens. A heavy 8-hour day ≈ low tens of MB pre-compression; months ≈ gigabytes,
not terabytes. Fine for local SQLite. Keep raw, as chosen.

**Raw screen-text is the most sensitive data the app holds.** Three protections,
none of which reduce fidelity:
1. **Local-only, encrypted at rest.** Store stays on-device; add encryption so a
   stolen laptop ≠ a readable life-log (macOS: SQLCipher / encrypted container;
   Windows: DPAPI-wrapped key).
2. **Capture exclusions.** Denylist of apps/windows (1Password, banking,
   incognito) where OCR is skipped entirely — never captured.
3. **Pause + redaction.** One-click pause capture (FreeBall start/stop already
   exists) and end-of-session "forget this" on any capture/thread.

**Retention policy:** default **keep raw indefinitely**; auto-prune raw after
distillation past N days available as a setting (keeps `Contribution`/summaries
forever regardless).

**Honest trade:** the more you exclude, the thinner the spine for those
activities. Fidelity vs. privacy is a dial.

## Section 5 — What it enables & how we prove "enough"

**Progression (each stage ships value alone):**
1. **Recall (first):** "What was I doing on the Windows port, where did I leave
   off?" → query Project → open threads + last contributions + summary. Pure read
   over the spine; validates it end-to-end.
2. **Suggestions (next):** open threads + patterns drive nudges. Powered by
   Thread status + `Judgment` history.
3. **Execution (earned):** only for projects past the trust ladder, and likely
   needs artifact-level data. **Honest caveat: full-OCR alone supports recall +
   suggestions strongly, but execution will probably need a later fidelity bump**
   (clipboard / active doc / file paths — the deferred "capture artifacts"
   option). Not blocked; just don't promise execution on OCR text alone.

**Validating sufficiency (the actual ask).** The `Judgment` log doubles as a
measurement harness via a **reconstruction test**: periodically have the model
answer "what is this project / what's the open thread / what's the next step?"
*from the stored spine only*, and compare to the user's corrections. Rising
accuracy = the data is becoming enough; flat/low accuracy on some activity = a
capture gap to fix. This turns "is the data enough?" from a guess into a tracked
metric.

**Testing:**
- Unit-test capture/de-dup + entity-resolution on fixture transcripts.
- Integration-test a multi-session fixture → assert the spine accumulates
  correctly.
- The reconstruction test as an ongoing eval.

## Out of scope (YAGNI for now)

- The consumer-side agent UX (how recall/suggestions/execution are surfaced).
  This design is the **data/context layer**; the agent is a downstream consumer.
- Artifact-level capture (clipboard/active doc). Deferred; revisit when execution
  is the active goal.
- Fine-tuning. The `Judgment` log is structured to *enable* it later; not built now.

## Open follow-ups

- Schema migration plan for existing `FreeBallSession` / `WorkSession` data into
  the new spine.
- Whether `KnowledgeTask` merges into `Project` or stays a specialization.
- Per-platform encryption-at-rest implementation (SQLCipher vs. DPAPI).
