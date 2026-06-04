# AccountaBall — Progress Notes
_Last updated: 2026-06-04 (v3.1 implemented + multi-agent verified: 223 tests passing, build clean, committed)_

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
- **Manual QA not yet run** (needs a human with Screen Recording permission +
  Ollama running, then repeat with `AI_PROVIDER=openrouter`). Checklist from the
  plan's Task 12: Ollama-down launch shows the card and auto-resumes within ~5s
  (and capture actually restarts — the critical fix above); no prompt in the
  first 15s of a fresh session; session-log screen doesn't trigger "what are you
  doing?"; an aligned excuse is accepted with Ollama running; completion shows
  the timeline ranges + per-task comments with faster/slower-vs-last for a
  repeated task.
- **Security:** rotate the leaked v2 OpenRouter key at openrouter.ai (it's in git history before commit `9c4ddf3`)
- **Residual prompt-tuning:** re-test classification accuracy with Ollama actually
  running (findings #2/#3) — deferred until manual QA; not a code issue (Phase A
  fixed the mis-scoring root cause)
