# How AccountaBall Works

_Operational reference for AccountaBall as it actually runs today (v3.2 Phase 1 built)._
_Last updated: 2026-06-07._

This is the single "how does the whole thing operate" document. It covers what
AccountaBall is, what it needs, the moment-to-moment loop, the 3-state
accountability model, the deterministic drift engine, every prompt-suppression
mechanism, the data it stores, the AI providers, and where everything lives in the
code. For env-var/config details see [config-system.md](config-system.md); for the
version history see [../progress.md](../progress.md).

---

## 1. What it is

AccountaBall is a macOS floating-widget app: a small always-on-top basketball that
watches your screen every few seconds and holds you to the task you declared. It is
**fully local** — screen capture, OCR, and (by default) the language model all run
on your machine. There is no backend.

The product idea behind v3.2 is a **calm mirror, not a nagging boss**: it classifies
what you're doing into three states, makes drifting *conscious* without shaming you,
and tracks your drifts against a limit *you set before you started* — a
pre-commitment the in-the-moment you cannot renegotiate.

---

## 2. Requirements & permissions

| Need | Why | Notes |
|---|---|---|
| **macOS 14 (Sonoma) or later** | SwiftData + ScreenCaptureKit minimum | Hard requirement |
| **Screen Recording permission** | To capture the screen for OCR | One-time system prompt; stored in System Settings → Privacy |
| **Notifications permission** | Off-task nudges | Optional but recommended |
| **An AI provider** | Classifies the screen | Ollama (local, default) or OpenRouter (cloud) — see §11 |

On first run macOS will prompt for Screen Recording. If it's denied, capture yields
no text and the app can't classify.

---

## 3. The app lifecycle (phase machine)

AccountaBall is a state machine over `AppPhase` (`Models/AppPhase.swift`). The
visible UI is chosen by `RootCoordinatorView` and the floating panel is resized per
phase by `FloatingPanel.resize(for:)`.

```
idle → welcome → setup → session ⇄ (ambiguous | offTask) → … → complete
                            │
                            ├─ whatsUp     (you tapped the ball)
                            ├─ progress    (your task/progress panel)
                            └─ aiUnavailable (provider down; auto-recovers)
```

| Phase | What it is |
|---|---|
| `idle` | Launched, no saved tasks |
| `welcome` | Bounce-in greeting; pre-fills tasks from last session |
| `setup` | Declare up to 5 tasks (+context) **and set the drift limit** (§7) |
| `session` | The basketball on the screen edge; silent monitoring |
| `ambiguous` | The calm one-time "is this part of {task}?" ask (§6) |
| `offTask` | The calm "you've drifted" break/resume choice (§6) |
| `whatsUp` | Tap-the-ball menu |
| `progress` | Per-task time + check-off panel |
| `complete` | 3-point-shot animation + the **session recap / transparency log** (§9) |
| `aiUnavailable` | Provider unreachable; pause card that auto-resumes via health polling |

A returning session pre-fills the saved tasks and the remembered drift limit, which
you re-confirm on the setup screen before starting.

---

## 4. The monitoring loop

Once a session starts, `AccountabilityEngine` runs a capture loop
(`Engine/AccountabilityEngine.swift`) on a fixed cadence:

1. **Capture** — `ScreenCaptureService` grabs a frame every
   `AppConstants.cycleSeconds` (**3 seconds**). It prefers the focused window, with
   a truncated pass over the whole screen as lighter peripheral context.
2. **OCR** — `OCRService` (Apple Vision, offline) extracts text from the frame.
3. **Classify** — the text + your active tasks (+ any allowances) go to the AI
   provider's `classifyMulti`, which returns one of four perceptions (§6).
4. **Decide** — the engine acts on that perception deterministically. Crucially, the
   **model only perceives; Swift owns every decision** ("Architecture B"). The model
   never decides whether you're in trouble, only what it sees on screen.

If a classify call fails because the provider is down, the cycle is **skipped** (it
is never scored as off-task), and a repeated failure flips the app to
`aiUnavailable` until health polling recovers it. A *cancelled* request (normal when
the loop ticks or stops) is treated as benign, not an outage.

Each on-task cycle credits `cycleSeconds` to that task's running time and writes a
`TimelineEntry` (used later for the recap timeline).

---

## 5. Output schema (what the model returns)

`classifyMulti` returns a `MultiTaskResult` parsed from a one-line
`RESULT | <label>` string (`Models/MultiTaskResult.swift`):

| Token | Meaning |
|---|---|
| `TASK:N` | On the Nth declared task (0-based) |
| `AMBIGUOUS` | Honestly can't tell if it relates to a task |
| `OFFTASK` | Clearly not any declared task |
| `DONE:N` | Task N appears complete |

The `label` is a short concrete description of what's on screen ("Editing the Q3
proposal in Google Docs"). Decoding is deterministic (temperature 0) so the same
screen yields a stable label, which several mechanisms rely on.

---

## 6. The 3-state accountability model (the heart)

This is what v3.2 added. Each cycle's perception drives one deterministic action:

### ON_TASK (`TASK:N`)
Silent. Credit time, record a timeline entry, reset the consecutive-off counter, set
the ball's on-task face. The most important behavior is **doing nothing well**.

### DONE (`DONE:N`)
Mark the task complete; if it's the last one, run the completion animation + recap.

### AMBIGUOUS (`AMBIGUOUS`) — *ask once, take your word*
The model genuinely can't tell. The prompt is **biased to prefer ON or AMBIGUOUS
over a false OFFTASK** — a wrong "get back to work" costs more trust than a missed
slack-off. On an ambiguous read the engine raises **one calm card**
(`AmbiguousAskView`):

> Quick check — is this part of *{your first task}*? [ what's it for? (optional) ]
> [ Yes, it's related ]  [ No, I drifted ]

- **Yes** → your word is taken (no AI re-judging). An *allowance* is created so the
  classifier treats this activity as on-task going forward, the moment is logged as a
  resolved check, and monitoring resumes.
- **No, I drifted** → logged as a **confirmed drift**, then the OFF card appears.
- The same activity is **never asked twice** in a session (tracked in `askedActivities`).
- Ambiguous is **not** a drift by itself — it resets the off counter.

### OFF_TASK (`OFFTASK`) — *calm break/resume, no scold*
After **2 consecutive** off-task reads (outside the settle window, §8), the engine
**logs a confirmed drift** and shows `OffTaskView`:

> You've drifted from *{your task}*.
> [ Take a timed 5-min break ]  [ Jump back in ]

- The drift counts on the **fact** of being off-task — *not* on which button you
  press. You can't button your way out of having drifted.
- **Take a 5-min break** → the ball goes quiet for 5 minutes (§8), with a countdown.
- **Jump back in** → silent return to monitoring.
- If you simply return to a task while the card is up, it auto-dismisses and that
  self-return is logged (and does **not** count against you).
- If you ignore the card for 2 minutes, an **auto-return** is logged (honest record
  of the non-answer) and monitoring resumes.

---

## 7. The drift engine & pre-commitment

This is the accountability mechanism, and it is **deterministic Swift, not the
model** — so it can't be hallucinated or talked out of.

- **Drift limit** — set once on the setup screen (a stepper, default **3**, range
  1–10). Stored in UserDefaults, remembered across sessions, re-shown each time for
  re-confirmation. It is editable **only at setup**; the in-the-moment user cannot
  raise it. That immovability is the whole point.
- **`driftCount`** — **derived**, never a stored tally: it is literally the count of
  this session's confirmed off-task events (`JustificationEvent`s with
  `kind == "offtask"`). Because it's derived from the persisted record, it can never
  desync from what the recap shows.
- **`commitmentBroken`** — derived: `driftCount >= driftLimit`. When true, there is
  **no in-the-moment punishment** (the ball keeps offering the calm choice). The
  consequence lands only at session end, in the recap (§9) — the one place the app
  speaks firmly.

The consequence in Phase 1 is **honor-system**: a visible, loud record in the recap.
(Cross-session streaks and a quoted-back commitment line are Phase 2 — §13.)

---

## 8. Prompt-suppression mechanisms (so it isn't annoying)

Several deterministic guards keep the ball from over-prompting. They interact
cleanly — no gap where you're over-nagged, no overlap where a real drift goes
uncounted:

| Mechanism | Window | What it suppresses |
|---|---|---|
| **Settle window** | 15s after start / resume | Early false prompts right after you begin or come back |
| **Ask-once** (`askedActivities`) | Whole session | Re-asking about an activity you already clarified |
| **Timed break** | 5 min (opt-in) | *All* prompting + suspicion + drift counting while you rest |
| **2-consecutive rule** | — | A single stray off-read can't trigger the OFF card |

During a timed break, completion is still honored and returning to work early still
credits time — it just never prompts, bumps suspicion, or logs a drift.

> **Two dormant mechanisms** (present in the engine, not wired into the current UI):
> the typed-excuse path (`handleExcuse`/`evaluateExcuse`) and the activity-scoped
> "give me 2 minutes" grace (`resumeAfterExcuse(graceForCurrentActivity:)`). The
> 3-state redesign replaced both — the AMBIGUOUS ask is the new "explain yourself"
> moment and the timed break is the new breather. They are retained for tests and
> guarded with comments; they have no live effect today.

---

## 9. The session recap & transparency log

When all tasks are done (or the session ends), `finalizeSessionRecap()` builds
`SessionRecap` and the completion screen renders it. It has three parts:

1. **Your checks** — the transparency log: every moment the ball asked or you chose,
   in time order, calm past tense:
   - `◐` ambiguous you clarified — *"you marked related: '…'"*
   - `●` a confirmed drift — *"you drifted"*
   - `○` an auto-return — *"resumed watching"* / *"returned to work"*
2. **Per-task commentary** — AI-written notes on how each task went (with a local
   faster/slower comparison fallback if the AI call fails).
3. **The drift summary** — *"Drift N of LIMIT"*, in calm white if under the limit. If
   the limit was hit, a single firm red line: *"✕ Commitment broken — you set a limit
   of L, you hit N."* This is the only place the app is not soft.

The recap is built from the persisted record, so it's faithful even if the AI
commentary call failed.

---

## 10. Data model & persistence

Three storage layers (`Models/Persistence.swift`, `Models/AppState.swift`):

- **SwiftData** (app's Application Support container) — the durable history:
  - `WorkSession` — one focus session (start/end, declared titles)
  - `TimelineEntry` — one per cycle (`taskIndex?`, activity label)
  - `JustificationEvent` — the **unified check log**: every ambiguous ask, confirmed
    drift, and auto-return, tagged by `kind` (`"ambiguous"` / `"offtask"` /
    `"auto-return"`). `driftCount` and the transparency log both read these.
  - `KnowledgeTask`, `Allowance`, `TaskCompletion` — cross-session task memory: a
    task's history, the "this counts as on-task" allowances, and finished-run records.
- **UserDefaults** — light, fast settings: the declared tasks (autosaved on edit) and
  the **drift limit**.
- **Debug log** — a rotating file at
  `~/Library/Application Support/AccountaBall/logs/accountaball.log`.

---

## 11. AI providers

Selected by the `AI_PROVIDER` env var; both implement the same `AIService` protocol
and share prompts via `Util/AIPrompts.swift`, so they behave identically.

| Provider | Default | Config |
|---|---|---|
| **Ollama** (default, local) | `qwen2.5:7b` | `OLLAMA_HOST` (`http://localhost:11434`), `OLLAMA_MODEL`. Requires Ollama running + the model pulled. |
| **OpenRouter** (cloud) | `anthropic/claude-haiku-4-5` | `OPENROUTER_API_KEY` (required), `OPENROUTER_MODEL`. |

Both decode at **temperature 0** for stable, repeatable classification. There is no
silent auto-fallback: a misconfigured provider sets a setup hint and launches on
local Ollama so the hint is visible. The classify prompt is the same 3-state,
bias-toward-ON/AMBIGUOUS instruction for both (and there's a live integration test
that confirms a real model honors the bias). See [config-system.md](config-system.md).

---

## 12. Privacy

Everything is local by default. Screen frames are OCR'd in memory and **not stored** —
only the AI's short text *label* for a cycle is persisted (in `TimelineEntry` /
`JustificationEvent`), never the screenshot. With the default Ollama provider, screen
text never leaves the machine. Choosing OpenRouter sends the OCR'd text (not images)
to that cloud API for classification.

---

## 13. Roadmap

- **Phase 1 — the spine (BUILT).** Everything above: 3-state classify, the AMBIGUOUS
  ask, the OFF break/resume choice, the timed break, the deterministic drift engine
  against a setup-locked limit, and the transparency-log recap. ~299 unit tests + a
  live Ollama integration test pass.
- **Phase 2 — the habit engine (planned).** Cross-session **streak** (don't-break-the-
  chain), an optional **commitment line** quoted back when broken, and **adaptive
  cadence** (lengthen checks when sustained on-task, tighten on drift) consuming the
  model's confidence.
- **Phase 3 — anti-gaming (planned).** Suspicious-claim clustering → raised scrutiny,
  varied phrasing.

Full design: [../planning/plans/2026-06-07-3state-accountability-design.md](../planning/plans/2026-06-07-3state-accountability-design.md).
Phase 1 implementation plan: [plans/2026-06-07-3state-phase1.md](plans/2026-06-07-3state-phase1.md).

---

## 14. Code map (where to look)

| Concern | File |
|---|---|
| App entry, provider wiring, phase watchers | `src/Sources/AccountaBall/AppDelegate.swift` |
| The monitoring loop + all decisions + drift engine | `src/Sources/AccountaBall/Engine/AccountabilityEngine.swift` |
| App state / phases / drift limit | `src/Sources/AccountaBall/Models/{AppState,AppPhase}.swift` |
| 3-state result + parse | `src/Sources/AccountaBall/Models/MultiTaskResult.swift` |
| SwiftData entities + check log | `src/Sources/AccountaBall/Models/Persistence.swift` |
| Recap / transparency-log types | `src/Sources/AccountaBall/Models/SessionRecap.swift` |
| Shared AI prompts | `src/Sources/AccountaBall/Util/AIPrompts.swift` |
| Providers | `src/Sources/AccountaBall/Services/{Ollama,OpenRouter}AIService.swift` |
| Capture / OCR / notifications | `src/Sources/AccountaBall/Services/{ScreenCapture,OCR,Notification}Service.swift` |
| The cards | `src/Sources/AccountaBall/Views/{AmbiguousAskView,OffTaskView,CompletionView,TaskSetupView,SessionBallView}.swift` |

**Build & run:** `make -C src build` (or `make -C src run`). **Tests:**
`make -C src test` (unit) and `make -C src test-integration` (live, needs Ollama).
