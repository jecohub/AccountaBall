# AccountaBall — Progress Notes
_Last updated: 2026-06-04 (v3.1 implemented: 209 tests passing, build clean, committed)_

---

## v3.1 — Provider availability + settle window + session breakdown

**All 12 tasks in the plan are implemented.** The three root-cause bugs from v3 testing are fixed:

- AI errors no longer silently score as off-task → the cycle is skipped, suspicion is never bumped
- Provider unreachability → `.aiUnavailable` pause card + auto-resume via health polling
- 15s settle window at session start / resume suppresses early false prompts
- Off-task prompts only raised during `.session` phase (never from `.progress`, `.complete`, etc.)
- Rich session-completion breakdown: mechanical timeline ranges + per-task AI commentary with local comparison fallback

### Stats
- **209 tests passing** (`make test`)
- **Build clean** (`make build`)
- **18 files changed**, 330 insertions, 12 deletions
- No new dependencies, no external config files

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
- `make test` → 209/209 passing (run multiple times, deterministic)
- `make build` → clean
- 6 test files: existing + `TimelineRangeTests`, `SummarizeSessionParseTests`, `EngineSessionRecapTests`

### Still open / non-code
- **Security:** rotate the leaked v2 OpenRouter key at openrouter.ai (it's in git history before commit `9c4ddf3`)
- **Deferred:** re-test with Ollama actually running to see if any residual prompt-tuning is needed for classification accuracy (not a code issue — see Phase A, which fixed the mis-scoring bug)
