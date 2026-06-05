# AccountaBall — Progress Notes
_Last updated: 2026-06-05 (v3.1.1 post-QA bug fixes — all 7 fixed; code committed; manual QA pending)_

---

## v3.1.1 — Post-QA bug fixes (IN PROGRESS)

First hands-on QA of v3.1 surfaced 7 real bugs. Root causes were traced from the
code + the debug log (`~/Library/Application Support/AccountaBall/logs/accountaball.log`)
+ three screenshots. Status of each:

| # | Symptom | Root cause | Fix | Status |
|---|---|---|---|---|
| 1 | Session completion shows **no breakdown** | `engine.beginSession()` **never called in production** (only `state.startSession()` was) → `engine.currentSession` always nil → `record()` / `finalizeSessionRecap()` / `summarizeCompletion()` all early-return. The whole v3 timeline/recap/history layer was dead in the real app. | Call `engine.beginSession(tasks:)` in `TaskSetupView.handleLetsGo()`; `endSession()` after the recap in the AppDelegate phase-watcher. | ✅ done (uncommitted) |
| 2 | Completion shows **"Total time --:--"** | `completeTaskAt()` calls `endSession()` (nils `sessionStartTime`) before `.complete`. | Snapshot `AppState.lastSessionDuration` before `endSession()`; CompletionView reads it. | ✅ done |
| 3 | **"AI unavailable" card flapped ~18×** with Ollama running | Capture loop treated a cancelled request (`-999 cancelled` — a normal lifecycle cancel) as a provider outage. | `AccountabilityEngine.isBenignCancellation()`; the loop skips cancellations instead of entering `.aiUnavailable`. | ✅ done |
| 4 | **Session log half-transparent** (white desktop bleeds through) | Views used `Color.black.opacity(0.85)`, and `AccountaProgressView` placed it as a VStack **sibling** (not a backdrop). | Opaque `Color.black` everywhere; `AccountaProgressView` rebuilt as a ZStack with a backdrop layer. | ✅ done |
| 5 | Task-match "you did this before" card **pushes buttons off-screen** | `.setup` panel fixed at 520×440; inline card overflows; panel sizes not clamped to the display. | Clamp all `FloatingPanel` sizes to the screen's visibleFrame; taller setup panel; rows now in a `ScrollView` with the "Let's go!" button pinned below it. | ✅ done |
| 6 | App **unusable when launched with Ollama down** | `recoverFromAIUnavailable()` always jumped to `.session` even with no session started. | Remember `phaseBeforeUnavailable`; recovery returns there (`.welcome` on a fresh launch). | ✅ done |
| 7 | Excuse could get **stuck**; no accepted/escape feedback | `handleExcuse` only resumed when the verdict had a `taskIndex`; the "accepted" stage was dead code, and after `handleExcuse` stopped flipping phase the justified path resumed nothing — the view froze. | `handleExcuse` no longer flips phase (resume moved to the view) + `resumeAfterExcuse(graceSeconds:)`. OffTaskView now shows an **accepted** stage (👍 "Carry on" → "Got it" resumes) and a **rejected** stage with "Back to it" + a **"Continue anyway"** escape hatch (resumes with a 120s grace via `AppConstants.continueAnywayGraceSeconds`). | ✅ done |

**Product decision (excuse policy):** keep the model's honest judgment (the log
shows it correctly rejecting "just checking linkedin" etc.) but add a "Continue
anyway" escape hatch on rejection + clear accepted/rejected feedback.

**All 7 bugs now fixed.** Verification (all green):
- `make test` → **233/233** passing (added a grace-window suite asserting "Continue
  anyway" suppresses past the default 15s settle but re-prompts after the 120s grace).
- `make test-integration` → **10/10** live `qwen2.5:7b` tests passing.
- `make build` → clean.

**Still TODO before this is shippable:**
- Run the human-eyes pass in `v3.1-manual-qa.md` (Screen Recording + Ollama, then
  repeat with `AI_PROVIDER=openrouter`).
- **Security:** rotate the leaked v2 OpenRouter key at openrouter.ai (in git history
  before commit `9c4ddf3`).

---

## v3.1 — Provider availability + settle window + session breakdown

**All 12 tasks in the plan are implemented.** The three root-cause bugs from v3 testing are fixed:

- AI errors no longer silently score as off-task → the cycle is skipped, suspicion is never bumped
- Provider unreachability → `.aiUnavailable` pause card + auto-resume via health polling
- 15s settle window at session start / resume suppresses early false prompts
- Off-task prompts only raised during `.session` phase (never from `.progress`, `.complete`, etc.)
- Rich session-completion breakdown: mechanical timeline ranges + per-task AI commentary with local comparison fallback

### Stats
- **223 tests passing** (`make test`, deterministic across repeated runs)
- **Build clean** (`make build`)
- **30 files changed**, 703 insertions, 15 deletions (cf45c86..HEAD)
- No new dependencies, no external config files
- Commits: `c329543` (feature) → `b6e1342` (Phase A/B test backfill) → `06347b6` (wiring fixes) → `9996f93` (critical capture-restart fix + cleanup)

### Phase A — Provider availability (Tasks 1–5)
- `AIService.healthCheck()` protocol method + implementations (Ollama: `GET /api/tags`, OpenRouter: key presence, Claude: stub)
- New `.aiUnavailable` AppPhase + `aiUnavailableHint` on AppState
- Classify errors → skip cycle (never off-task), connection failure → pause capture loop
- Auto-resume: poll `healthCheck()` every 5s, dismiss card + restart on recovery
- Startup health probe in AppDelegate
- AIUnavailableView: angry ball, hint text, spinning ProgressView, "Retrying automatically…"
- FloatingPanel.resize(for:) handles `.aiUnavailable` (300×300 anchor-right)
- RootCoordinatorView: `case .aiUnavailable → AIUnavailableView()`

### Phase B — Grace window + prompt suppression (Tasks 6–7)
- Injectable clock (`now: () -> Date`) for deterministic testability
- 15s settle window (`settleWindow: TimeInterval = 15`)
- `beginSession()`, `resumeAfterExcuse()`, `recoverFromAIUnavailable()` all arm the settle window
- Off-task escalation gated on `!inSettleWindow && state.appPhase == .session`
- Timeline still recorded in all phases; only the prompt is suppressed

### Phase C — Session completion breakdown (Tasks 8–11)
- `TimelineRange { startOffset, endOffset, label, taskIndex? }` model
- `TimelineCoalescer.ranges()` — coalesces contiguous `(taskIndex, label)` groups
- `PerTaskSessionInput`, `SessionRecap`, `PerTaskComment` types (all existed from v3 design)
- `AIService.summarizeSession()` protocol method + Ollama/OpenRouter implementations with JSON schema
- `AIPrompts.sessionSystem`, `buildSessionPrompt()`, `parseSessionComments()` — prompt + parser
- Engine `finalizeSessionRecap()`: builds ranges + local comparisons per task, calls AI for prose, falls back to local comparison string on failure
- CompletionView renders timeline ranges + per-task comment cards

### Phase D — Verification (Task 12)
- `make test` → 223/223 passing (run multiple times, deterministic)
- `make build` → clean

### Multi-agent verification pass (post-implementation)
The initial v3.1 commit (`c329543`) built green but was unreviewed and missing
the Phase A/B test coverage and two wiring steps. A subagent-driven review
closed the gaps:

- **Backfilled the 4 missing Phase A/B TDD suites** (`b6e1342`):
  `EngineErrorHandlingTests` (AI error skips the cycle, never off-task),
  `EngineAvailabilityTests` (pause + resume + `isCapturing` edge contract),
  `EngineSettleWindowTests` (injected clock; in-window suppress / post-window
  prompt), `EnginePromptGateTests` (`.progress` records but never prompts).
  The existing engine logic matched spec — no engine changes were needed to
  pass them.
- **Spec-compliance review found 2 omitted wiring steps** (`06347b6`):
  `finalizeSessionRecap()` was dead code (never called) → wired into the
  AppDelegate phase-watcher on `.complete` with a double-build guard; and the
  Task 4 startup `healthCheck` probe was missing → added so the unavailable
  card shows at launch, not only after the first failed cycle.
- **Code-quality review caught a CRITICAL silent failure** (`9996f93`):
  after AI recovery the screen-capture loop never restarted — `enterAIUnavailable`
  stopped capture but left `state.isCapturing == true`, so the AppDelegate
  edge-watcher saw no `false→true` edge on recovery and never re-called
  `start()`. The app would return to a smiling on-task ball while no longer
  watching the screen. Fixed by clearing `isCapturing` on outage (regression
  test added). Same commit: health-poll thrash guard (sleep before first
  probe), robust per-task comment realignment (model drift can't drop a task's
  card), `enumerated()` task-index fix, hoisted off-task count, named
  `AppConstants.cycleSeconds`, deleted dead `mmss` helpers, consolidated
  triplicated test fake.

Test files: existing v3 suites + `TimelineRangeTests`, `SummarizeSessionParseTests`,
`EngineSessionRecapTests`, plus the 4 backfilled Phase A/B suites and a shared
`TestFakes.swift`.

### Still open / non-code
- **Manual QA not yet run** — checklist in [`v3.1-manual-qa.md`](v3.1-manual-qa.md)
  (needs Screen Recording permission + Ollama running, then repeat with
  `AI_PROVIDER=openrouter`). Includes the critical post-recovery capture-restart
  check.
- **Security:** rotate the leaked v2 OpenRouter key at openrouter.ai (it's in git history before commit `9c4ddf3`)

### Resolved
- **Findings #2/#3 (prompt-tuning) — CLEARED.** The opt-in live suite
  `make test-integration` (see `Tests/IntegrationTests/`) runs the real
  `qwen2.5:7b` and confirms classification + excuse evaluation behave correctly
  with Ollama running (aligned excuse → justified, on/off-task classified
  correctly). 10/10 live tests pass. The earlier symptoms were the v3
  error-mis-scoring root cause, fixed in Phase A.
