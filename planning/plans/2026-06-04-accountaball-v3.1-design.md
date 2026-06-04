# AccountaBall v3.1 — Design

_Date: 2026-06-04 · Status: validated, ready for implementation plan_

This design responds to the first hands-on test of v3. It fixes a root-cause
class of false behavior, adds two UX guards, and builds the rich session
breakdown the user asked for.

---

## Background: what the test actually revealed

The user ran `make run` **without Ollama installed/running**. The default
provider is local Ollama, and the v3 code swallows AI errors:

- `AccountabilityEngine.start()` does `(try? await classifyMulti(...)) ?? .offTask`,
  so every failed cycle is silently scored **off-task** → nags immediately and
  forever.
- `handleExcuse` returns `false` when `evaluateExcuse` throws → every excuse is
  judged **not justified** → "you should get back to your tasks" regardless of
  what the user typed.

So three of the five reported findings are **one root cause** (provider
unreachable + errors mis-scored as off-task), not separate bugs:

| Finding | Real cause |
|---|---|
| #1 immediately asks "what are you doing?" | errored cycles default to `.offTask`; 2 cycles (~10s) → prompt |
| #2 justified reason still rejected | `evaluateExcuse` throws → `handleExcuse` returns false |
| #3 aligned activity never recognized; keeps asking | every classify throws → always `.offTask` |
| #4 prompts while session-log screen open | `processResult` only guards the `.offTask` phase, not `.progress`/`.whatsUp` |
| #5 wants a rich completion breakdown | new feature |

#2/#3 may have **residual** prompt-quality issues once Ollama actually runs;
those are deferred until re-test (see Rollout).

---

## Decisions captured during brainstorming

1. **On AI failure → pause accountability entirely.** Show a clear "AI
   unavailable — set up Ollama" card, stop the watch loop, auto-resume when the
   provider is reachable. Never accuse the user based on a failure.
2. **Session timeline = mechanical ranges + one AI commentary call.** Build the
   time-range breakdown locally from the ~5s labels we already capture; make a
   single AI call at session end for per-task "faster/slower + suggestions".
3. **Grace period kept** (~15s settle window at session start / resume) even
   when the AI works.

---

## Section 1 — Provider availability (fixes #1, #2, #3 at the root)

**`AIService.healthCheck() async -> Bool`** (new protocol method):
- Ollama: cheap `GET /api/tags`; also verify the configured `OLLAMA_MODEL` is
  present in the list. False on connection failure or missing model.
- OpenRouter: true iff `OPENROUTER_API_KEY` is set (optionally a cheap ping).
- Claude (legacy stub): returns the existing configured state.

**New `AppPhase.aiUnavailable`** + `AppState.aiUnavailableHint: String?`:
- Entered when a health check fails at launch, **or** a live classify/excuse
  call throws a connection-class error mid-session.
- The engine **stops the capture loop**; the ball shows a card with the specific
  hint (e.g. "Ollama isn't running — `ollama run qwen2.5:7b`", or
  "Set `OPENROUTER_API_KEY`").
- **Auto-resume:** while unavailable, poll `healthCheck()` every ~5s; on success,
  dismiss the card and restart the loop (re-entering the settle window).

**Never accuse on error:** outside the unavailable state, a one-off classify
error is a **skipped cycle** — no suspicion increment, never `.offTask`.

---

## Section 2 — Grace period (#1) + prompt suppression (#4)

**Settle window (#1):** on session start and on each resume (after a prompt or
after `.aiUnavailable`), the engine enters a ~15s window during which it still
records timeline entries but does **not** raise an off-task prompt. Default 15s,
a single configurable constant. The window start time uses an **injectable
clock** so it is unit-testable.

**Prompt only in an active session (#4):** `processResult` raises off-task
prompts **only** when `appPhase == .session`. In `.progress`, `.whatsUp`,
`.setup`, `.complete`, `.aiUnavailable` it still **records the timeline** (so
history stays complete) but suppresses the prompt. Returning to `.session`
resumes watching after the settle window.

---

## Section 3 — Rich session-completion breakdown (#5)

**A. Session timeline (mechanical).** Coalesce *contiguous* `TimelineEntry`s
with the same label into ranges, timed relative to session start; include
off-task stretches, labeled:
```
00:00–00:45  Terminal + VS Code — working on AccountaBall   (Task 1)
00:45–01:30  LinkedIn — browsing                            (off-task)
```
No AI required; reliable even if the model is flaky.

**B. Per-task commentary (one AI call).** On all-tasks-complete, the engine
calls `summarizeSession(perTask:)` once, passing each task's stats: this run's
duration, the **last** completion's duration, the **average** across prior
completions (`KnowledgeTask.completions`), off-task count, and steps.
Comparisons (vs last + vs average) are computed **locally** (reuse
`DurationDelta`) so the numbers are always correct; the AI writes only the prose
+ optional suggestion ("if there's any"):
```
Task 1 — Finished in 12m, faster than last time (15m) and your avg (16m).
         Tip: ~3m lost to Slack mid-task; try snoozing it.
Task 2 — Mostly browsing LinkedIn; no work comment. 8m vs your 5m avg.
```
**Fallback:** if the AI call fails, show the timeline + raw local comparison
numbers — never a blank screen.

The existing during-session per-task recap (v3 Task 18, in the progress panel)
stays as-is; this is the comprehensive end-of-session view.

---

## Section 4 — Technical changes, testing, rollout

**New/changed types**
- `AIService`: `healthCheck() async -> Bool`, `summarizeSession(perTask:) async throws -> [PerTaskComment]`.
- `TimelineRange { startOffset, endOffset, label, taskIndex? }`,
  `SessionRecap { ranges: [TimelineRange], perTask: [PerTaskComment] }`,
  `PerTaskComment { taskTitle, comment, suggestion? }`.
- `AppPhase.aiUnavailable`; `AppState.sessionRecap`, `AppState.aiUnavailableHint`.
- Extend `TimelineCoalescer` with a range-builder; reuse `DurationDelta`.

**Engine**
- Settle-window timestamp (injectable clock); suppress prompts within 15s of
  start/resume.
- `processResult` raises prompts only when `appPhase == .session`; always records.
- Classify/excuse errors → skip cycle (no suspicion bump); connection failure →
  `.aiUnavailable` + `healthCheck` polling to auto-resume.
- On all-tasks-complete: build `[TimelineRange]`, compute local comparisons, call
  `summarizeSession`, publish `SessionRecap` (fallback on throw).

**Views**: AI-unavailable card; completion screen renders the timeline list +
per-task comment cards.

**Testing (TDD, the micro-runner)**
- Range coalescing into `[TimelineRange]` (pure).
- Average + last comparison (`DurationDelta`).
- "Classify error never increments suspicion / never `.offTask`."
- "No prompt unless `appPhase == .session`."
- "No prompt within settle window" (injected clock).
- Health-check pause/resume with a fake AI.
- `summarizeSession` parse + throw-fallback.
- UI remains manual QA.

**Rollout order** (each independently shippable):
1. **Provider availability** — the biggest fix. After this, **re-test with Ollama
   running** to see whether #2/#3 need residual prompt-tuning.
2. Grace period + prompt suppression.
3. Session-completion breakdown.

**Still open / non-code:** rotate the leaked v2 OpenRouter key (carried over
from v3 progress notes).
