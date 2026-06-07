# AccountaBall — 3-State Accountability Redesign (design, IN PROGRESS)

_Status: design APPROVED (Sections 1–4). Section 5 testing folds into the Phase 1
implementation plan. Next step: build Phase 1 (the spine) so it can be felt.
Nothing implemented yet._

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

## Design — Section 3: Prompts / UI + transparency log (APPROVED)

Tone is "mirror, not boss" — calm, single-breath, no exclamation marks, no scold.

**AMBIGUOUS ask** (raised once per activity):
> Quick check — is this part of *{taskLabel}*? [ what's it for? ]
> [ Yes, it's related ]  [ No, I drifted ]

- "Yes" + reason → creates an **allowance**, logs the claim, never re-asks this
  activity, resets the consecutive-off counter. Empty reason allowed (the moment
  of consciousness matters, not the paperwork).
- "No, I drifted" → converts to a confirmed OFF event (`DRIFT_COUNT++`), then
  shows the break/resume choice.

**OFF calm choice** (after 2 consecutive OFF reads):
> You've drifted from *{taskLabel}*.
> [ Take a timed 5-min break ]  [ Jump back in ]

- **Drift counts on the fact of the OFF event**, not on which button is pressed
  (locked) — the button only governs what happens next. "Break" starts a visible
  countdown (ball quiet until it elapses); "Jump back in" → silent return.

**Transparency log** (session-complete screen) — the payoff of the mirror.
Source: this session's `JustificationEvent` rows (checks only, not every on-task
read), time-ordered, `kind` picks icon + phrasing:
```
  Your checks
  ◐ 0:12  Excel spreadsheet — you marked related: "budget for the deck"
  ● 0:28  YouTube — you drifted · took a 5-min break
  ● 0:41  Twitter — you drifted · jumped back in
  ○ 0:33  no response, resumed watching        (auto-return)
  Drift 2 of 3   ·   streak: 4 sessions  (Phase 2)
```
`◐` ambiguous-clarified, `●` confirmed drift, `○` auto-return (ignored 60s).
Calm factual past tense, no red, no "failed." Bottom line surfaces
`DRIFT_COUNT of limit` (the pre-commitment made visible); if the limit was hit,
the broken-commitment record appears here (Section 4). Extends the existing
session-complete summary; adds the checks panel above the task breakdown.

---

## Design — Section 4: Commitment / threshold / streak + setup (APPROVED)

**Setup screen** gains a small commitment block below the task table:
- **Drift limit** — stepper, default `3`, range 1–10. This is `DRIFT_THRESHOLD`.
  Editable *only at setup*; the in-the-moment user can never raise it (that
  immovability is the whole mechanism). Persists to `UserDefaults`; **remembered
  across sessions but always re-shown for re-confirmation** (see it, change it,
  no retyping). Ships in **Phase 1**.
- **Commitment line** — *optional* free text, **Phase 2**. Quoted back verbatim
  when broken. Empty → neutral system phrasing.

**Broken-commitment record** (when `DRIFT_COUNT` reaches the limit): **no
in-the-moment punishment** — the ball keeps offering the calm break/resume choice
mid-session. The consequence lands only at session end, in the recap — factual,
not cruel, but the one place copy is *not* softened (it's the loudest the app
gets):
```
  ✕ Commitment broken — you set a limit of 3 drifts, you hit 4.
  You committed to: "finish the deck before lunch"   (Phase 2)
  Streak reset to 0 (was 4)                          (Phase 2)
```
Under the limit → quiet win + streak increments.

**Streak (Phase 2 — the habit engine):** consecutive *sessions* finished
at-or-under the drift limit (Seinfeld chain). **Per-session granularity** (not
per-day). Persist a tiny singleton: `currentStreak`, `bestStreak`,
`lastSessionEndedAt`. Increment on a clean session, reset to 0 on a broken one.
Surfaced in the recap bottom line and small on the welcome screen for returning
users (`🔥 4-session streak`).

## Section 5 — Testing

Folds into the Phase 1 implementation plan. Coverage: deterministic engine tests
(derived drift count, threshold → commitmentBroken, ask-once dedupe), parse tests
for the AMBIGUOUS token, and a live integration case for 3-state classification.

## Open questions (deferred — do not block Phase 1)

- Exact debounce for AMBIGUOUS (ask on first read vs. require persistence?).
- Does an AMBIGUOUS claim that "doesn't hold up" increment DRIFT_COUNT (Phase 3)?
- Minimum session length to count toward the streak (anti-padding, Phase 2).
