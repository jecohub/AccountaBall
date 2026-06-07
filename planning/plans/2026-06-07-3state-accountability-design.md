# AccountaBall — 3-State Accountability Redesign (design, IN PROGRESS)

_Status: brainstorm paused at Section 2 (approved). Sections 3–5 still to design.
Resume with the `superpowers:brainstorming` flow. Nothing implemented yet._

This redesigns AccountaBall's core from a binary on/off nag into a calm
"mirror" that classifies into three states, makes drift conscious without
shaming, and tracks a pre-committed limit toward an honor-system consequence.

---

## The source vision (user-provided prompt)

A four-step accountability model:

1. **Classify** CURRENT_ACTIVITY into **ON_TASK / OFF_TASK / AMBIGUOUS**. Judge
   against the relevance criteria, not the model's own opinion. **Bias toward ON
   or AMBIGUOUS when unsure** — a false "get back to work" destroys trust faster
   than a missed slack-off.
2. **Act by state:** ON → say nothing, lengthen the interval. AMBIGUOUS → ask
   once, briefly, for a one-line reason + rough time box; accept provisionally,
   log it, don't re-ask the same activity. OFF → no scolding; surface a choice
   (a short TIMED break, or resume).
3. **Track the pattern (the real engine):** a single answer is a data point.
   Increment DRIFT_COUNT on each OFF event (and, later, AMBIGUOUS claims that
   don't hold up). Clustering of vague "related" claims → raise scrutiny.
4. **Consequence:** only the user's pre-set one, triggered at DRIFT_THRESHOLD.
   The in-the-moment user cannot renegotiate it — that pre-commitment is the
   whole mechanism.

Plus: **adaptive cadence** (sustained ON → back off; drift → tighten; vary
phrasing) and a **calm, non-adversarial tone** (mirror, not boss; no nagging,
guilt, or exclamation marks).

Full output schema the prompt envisioned (one JSON per check):
`{ state, confidence, action, message, log_note, next_check_seconds,
trigger_consequence }`.

---

## Decisions made (locked)

1. **Consequence = visible record + streak break (honor system).** No
   enforcement. A "broken commitment" is logged loudly in the session recap and
   breaks a productivity streak. Fits the "mirror, not boss" ethos; teeth can be
   added later.
2. **Pre-commit at setup = drift limit + optional commitment line + a
   cross-session streak.** Drift limit defaults to 3 (editable, low friction). An
   *optional* "I commit to: ___" line is quoted back when broken (self-authored
   words bind harder). The streak = consecutive sessions under the limit
   (Seinfeld don't-break-the-chain — the real habit engine).
3. **Architecture = B: model perceives, Swift decides.** The model returns only
   `{ state, confidence, label }` (perception). All control logic — drift count,
   threshold, streak, cadence, and triggering the consequence — is deterministic,
   testable Swift. Rationale: the spec demands the in-the-moment user can't
   renegotiate the consequence, so that logic must be tamper-proof code, not a
   7B model's (hallucination-prone, gameable) opinion. The model's JSON is split
   at the perception/decision line.
4. **Phasing:**
   - **Phase 1 — the spine (ship first):** 3-state classify; AMBIGUOUS one calm
     ask (provisional accept, log, don't re-ask same activity); OFF calm
     break/resume choice (no scold); Swift drift engine (limit at setup,
     DRIFT_COUNT, threshold → broken-commitment record); transparency log in the
     session-complete screen; tone reframe throughout.
   - **Phase 2 — the habit engine:** cross-session streak (persist + show);
     optional commitment line; adaptive cadence (lengthen on sustained ON,
     tighten on drift; uses the stored confidence).
   - **Phase 3 — anti-gaming polish:** suspicious-claim clustering → raised
     scrutiny; varied phrasing. Deliberately late — with an honor-system
     consequence you're only fooling yourself, so it's low-value early.

---

## Design — Section 1: Data model & the 3-state signal (APPROVED)

- **3-state classification.** Extend `MultiTaskResult` with `.ambiguous(label:)`:
  ```swift
  enum MultiTaskResult {
      case onTask(index: Int, label: String)
      case ambiguous(label: String)   // NEW — model genuinely can't tell
      case offTask(label: String)
      case done(index: Int, label: String)
  }
  ```
- **Classify prompt** rewritten to Step 1: `TASK:N` (clearly matches),
  `OFFTASK` (clearly doesn't — game, unrelated social), `AMBIGUOUS` (honestly
  can't tell), with the explicit bias-toward-ON/AMBIGUOUS-when-unsure
  instruction. `MultiTaskResult.parse` learns the `AMBIGUOUS` token.
- **Confidence:** the model also returns a `0–1` confidence. Phase 1 stores it
  but doesn't act on it (the 3-state already captures uncertainty); Phase 2's
  adaptive cadence consumes it. Schema is right from the start.
- **What each cycle records.** Reuse the existing SwiftData `JustificationEvent`
  as the unified "check log" (already has `at`, `excuse` = the user's response,
  `justified`, `activity` = the label, `rule`). Add one field: `kind`
  (`ambiguous` / `offtask` / `auto-return`) so the recap renders each check
  faithfully. On-task cycles still write a `TimelineEntry`; only *checks* (an
  ambiguous ask or an off-task choice) write a `JustificationEvent`.

## Design — Section 2: Per-cycle decision engine (APPROVED)

Each cycle the model returns a state; the engine acts deterministically:

| State | Action |
|---|---|
| **ON_TASK** | Silent. Record on-task timeline entry, credit time, reset the consecutive-off counter. (Phase 2: signal cadence to lengthen.) |
| **DONE** | Mark task complete (unchanged). |
| **AMBIGUOUS** | If this activity was already asked this session → stay silent. Otherwise raise **one** calm ask. Resets the consecutive-off counter (not a confirmed drift). |
| **OFF_TASK** | Debounced: needs 2 consecutive OFF reads (outside the settle window) before surfacing the calm break/resume choice. That confirmed event is a **drift** → `DRIFT_COUNT++`. |

- **DRIFT_COUNT is derived, not a mutable tally:** `count of confirmed off-task
  JustificationEvents this session (kind == .offtask)`. Can't desync, survives
  anything, trivially testable. At the drift limit, `commitmentBroken` is also
  derived → the trigger, with no in-the-moment punishment (consequence = recap
  record + streak break at session end).
- **Reuse:** the 15s **settle window** still suppresses early prompts; the
  **activity grace** ("give me 2 minutes") still timeboxes re-asking one
  activity; an AMBIGUOUS answered "yes, it's for X" creates an **allowance**
  (today's mechanic) — that's how we "log the claim and don't re-ask."
- **Only genuinely new state:** `askedActivities` (a Set, for "ask once") and the
  derived `DRIFT_COUNT` / `commitmentBroken`.

---

## Still to design (resume here)

- **Section 3 — Prompts / UI:** the AMBIGUOUS ask ("Quick check — is this part of
  {task}? What's it for?"), the OFF calm choice ("drifted — timed 5-min break, or
  jump back in?"), tone reframe of all copy, and the **transparency log** in the
  session-complete screen (every check + the user's response documented, e.g.
  "Excel spreadsheet — you marked related: '…'").
- **Section 4 — Commitment / threshold / consequence + setup:** the setup fields
  (drift limit default 3 + optional commitment line), the broken-commitment
  record, and the cross-session streak model/persistence.
- **Section 5 — Testing strategy:** deterministic engine tests (drift count,
  threshold, ask-once dedupe, streak), parse tests for the AMBIGUOUS token, and
  a live integration case for 3-state classification.

## Open questions for later

- Exact debounce for AMBIGUOUS (ask on first read vs. require persistence?).
- Does an AMBIGUOUS claim that "doesn't hold up" increment DRIFT_COUNT (Phase 3)?
- Streak granularity: per session vs. per day.
