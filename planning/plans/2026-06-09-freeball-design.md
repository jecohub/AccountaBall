# FreeBall — Design

**Date:** 2026-06-09
**Status:** Design approved (Sections 1–7)
**Mode:** New passive-observation mode, alternative to the v3.2 accountability mode.

## 1. What FreeBall is

FreeBall is a **passive observation mode** — the structural opposite of the
v3.2 accountability mode. Accountability mode is an active *mirror*: it
classifies ON / AMBIGUOUS / OFF, nudges, and tracks drift. FreeBall is a silent
*recorder*: it watches richly, judges nothing, interrupts never, and learns
what you spend your time on.

**Main purpose:** learn the user's real time-allocation patterns over time. No
task is declared, nothing is enforced, no on/off-task question is ever asked.

### Entry flow
- On the welcome screen, below the primary **"Let's get started"** button and
  the "start fresh" link, add a secondary **"FreeBall"** button — visually
  subordinate (outline/ghost, same orange family) so it reads as "the other
  mode," not the default.
- Tapping FreeBall starts tracking **instantly** — no setup, no task prompt.
- The panel collapses to the floating ball in a calm **"observing"** face
  (neutral, eyes-open — not the happy/angry accountability expressions, since
  there is nothing to judge).
- New `AppPhase.freeBall` (live) → `AppPhase.freeBallRecap` (post-session).
  `BallState` gains a calm `.observing` case.

### Mode exclusivity
FreeBall and accountability mode are mutually exclusive entry points from
welcome. One active session at a time. Ending either returns to welcome.

## 2. Capture pipeline & data model

### Capture loop
Reuse the existing `ScreenCaptureService` + `OCRService` at the same ~3s
cadence (`AppConstants.cycleSeconds`). The difference is downstream:

- Accountability mode → AI classify call per cycle, keeps only a short label.
- FreeBall → **zero AI calls mid-session**; **stores the full OCR text** of
  each cycle.

### Dedup
At 3s, consecutive cycles are usually near-identical (same screen). Each cycle,
compare OCR text to the previous stored entry; if effectively unchanged
(normalized equality / high overlap), **extend the prior entry's time range**
instead of writing a new row. Genuinely new screen → new entry. Gives "full
text, full session" without thousands of duplicate rows, and naturally yields
time-on-screen for the recap.

### New SwiftData models
- `FreeBallSession` — `id`, `startedAt`, `endedAt`, `cycleCount`, and generated
  recap fields: `narrative`, `categories: [CategorySpan]`, `insight`. One per
  session. Kept forever.
- `FreeBallCapture` — `sessionRef`, `firstSeenAt`, `lastSeenAt`, `text` (full
  OCR). The deduped raw transcript. Kept forever.
- `CategorySpan` — `{ label, minutes }`, stored on the session so the breakdown
  survives without re-running AI.

Capture + OCR + dedup write all happen off-main, per the existing engine's
threading rule.

## 3. Live session UI

### Ball (resting)
During a session the floating ball shows the calm `.observing` face and stays
out of the way — no nudges, no notifications, no color changes. Deliberately
boring.

### Clicking the ball → live session log (`FreeBallSessionView`)
Minimal by design:
- The **running timer** (elapsed since start), styled like the accountability
  session timer.
- A calm **"watching…"** line (no content shown — honest, since no AI has run).
- A single prominent **"End Session"** button.

Clicking the ball again (or a dismiss affordance) collapses back to the bare
ball **without ending** — viewing the log and ending the session are distinct
actions.

### Panel sizing
`FloatingPanel.resize(for:)` gains cases for `.freeBall` (ball-sized) and the
recap phase (taller card), mirroring existing per-`AppPhase` sizing.

### End Session
Transitions `.freeBall → .freeBallRecap`, stops the capture loop, kicks off
summarization. While the AI works, show a brief calm loading state ("making
sense of your session…").

## 4. Summarization on End Session

The **only** time FreeBall touches the AI.

### Inputs (assembled off-main)
1. **This session's deduped transcript** — `FreeBallCapture` rows with text and
   time range (so the model knows *how long* each screen was up).
2. **Past session recaps** — `narrative` + `categories` + `insight` from prior
   `FreeBallSession` rows. Recaps, **not** raw transcripts, so cross-session
   context stays small as history grows.

### Context budgeting
- **Time-weighted condensation:** longest-lived screens kept fullest, brief
  blips trimmed; if still too large, chunk-then-stitch.
- Past recaps **capped** (most recent N sessions) so input size stays bounded
  regardless of history depth.

### One structured AI call
New `AIService.summarizeFreeBall(...)`, implemented for both Ollama and
OpenRouter, returning JSON:
- `narrative` — plain-language paragraph.
- `categories` — `[{label, minutes}]` time breakdown.
- `insight` — cross-session pattern note; the prompt explicitly permits
  referencing past recaps ("you usually…").

### Persistence
Results written onto the `FreeBallSession`, saved with its captures. Transcript
retained. If the AI is unavailable, save the session with raw data intact + a
**recap-pending** marker so it can be summarized later rather than lost.

## 5. Recap UI (`FreeBallRecapView`)

Renders the saved `FreeBallSession`, in order:
1. **Header** — session length + calm title ("Here's where your time went").
2. **Narrative** — the human paragraph up top.
3. **Categorized breakdown** — each `CategorySpan` as a row: label, minutes, and
   a proportional bar (Coding ▓▓▓▓▓ 45m, Research ▓▓ 20m). Sorted longest-first.
4. **Insight note** — the cross-session pattern, set apart in a quieter "what
   FreeBall noticed" card.

A single **"Done"** button returns to welcome. Session already persisted;
nothing lost on dismiss.

Reuses the visual language of the accountability `CompletionView`/`RecapCard`
(card layout, typography) so it feels native — **no** hoop/confetti, since
FreeBall is not a task-completion celebration.

### Scope cut
**No history browser in v1.** Cross-session learning surfaces *through the
insight note*, not a dedicated history screen. The data model fully supports a
history/trends view later; deferred to v2 (YAGNI).

## 6. Edge cases, coexistence & privacy

- **Quit / crash mid-session.** Captures written incrementally → session
  survives as raw data. On next launch, an un-ended session (`startedAt`, no
  `endedAt`) is recap-pending; finalize silently or offer to summarize. No
  auto-resume of the capture loop.
- **AI unavailable at End Session.** Save with transcript + recap-pending
  marker; surface via existing `setupHint`. Retry later.
- **Empty / trivial session.** Skip the AI call; show a gentle "not enough to
  summarize yet" recap.
- **Screen Recording permission revoked.** Capture fails gracefully (same as
  accountability mode); session can still end.
- **Mode exclusivity.** Guarded — welcome is the only entry; one session phase
  active at a time.
- **Privacy.** Everything local (SwiftData + local Ollama default), consistent
  with the app's no-backend design. FreeBall stores **full screen text
  forever** — materially more sensitive than accountability mode's short
  labels. A "clear FreeBall history" affordance is a sensible fast-follow,
  out of v1 scope.

## 7. Testing (TDD + MicroTest)

- **Dedup logic** — identical/near-identical consecutive OCR extends the range;
  new text creates a row.
- **Session lifecycle** — start sets phase/state; end stops capture and
  transitions to recap.
- **Summarization parsing** — JSON → `narrative`/`categories`/`insight`, with a
  malformed-response fallback (mirrors `ExcuseVerdict`/recap parser tests).
- **Cross-session input assembly** — past recaps included, capped at N, raw
  transcripts excluded.
- **Recap-pending / empty-session** paths.
- New test fakes get `summarizeFreeBall()` like the existing `MultiMockAI`
  family.

## Open items / future (v2)
- History/trends browser over past `FreeBallSession`s.
- "Clear FreeBall history" affordance.
- Deeper cross-session pattern-mining (time-of-day habits, drift triggers).
